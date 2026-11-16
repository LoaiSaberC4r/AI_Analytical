using System.Security.Cryptography;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed class AnalyticsIngestor(AnalyticsDb db, IMqttPayloadReader reader, TimeProvider clock,
    IOptions<AnalyticsOptions> analyticsOptions, IOptions<MqttOptions> mqttOptions,
    IAnalyticsNotifications notifications, ILogger<AnalyticsIngestor> logger) : IAnalyticsIngestor
{
    private AnalyticsOptions Policy => analyticsOptions.Value;

    public async Task<IngestResult> Process(string topic, ReadOnlyMemory<byte> payload, DateTime receivedAtUtc, CancellationToken ct)
    {
        var maxBytes = mqttOptions.Value.MaxPayloadBytes;
        var hash = Convert.ToHexString(SHA256.HashData(payload.Span[..Math.Min(payload.Length, maxBytes)]));
        var parsed = payload.Length <= maxBytes ? reader.Read(topic, payload.Span) : new PayloadRead(null, "payload_too_large");
        var now = clock.GetUtcNow().UtcDateTime;
        if (parsed.Observation is not { } data)
            return await Reject(topic, hash, receivedAtUtc, parsed.Rejection!, ct);
        if (data.IsLive && data.SourceTimestampUtc > receivedAtUtc.AddSeconds(Policy.ClockSkewToleranceSeconds))
            return await Reject(topic, hash, receivedAtUtc, "future_live_timestamp", ct);

        IngestResult result;
        CommittedAnalytics? notice = null;
        await using (var transaction = await WriteTransaction.Begin(db, ct))
        {
            var camera = await db.Cameras.Include(x => x.Site).SingleOrDefaultAsync(x => x.ExternalCameraId == data.ExternalCameraId, ct);
            if (camera is null)
                return await Reject(topic, hash, receivedAtUtc, "unknown_camera", ct, transaction);
            var session = await db.AnalyticsSessions.SingleOrDefaultAsync(
                x => x.CameraId == camera.Id && x.ExternalSessionId == data.ExternalSessionId, ct);
            if (session is not null)
            {
                var original = await db.ReceivedAnalyticsMessages.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.AnalyticsSessionId == session.Id && x.Sequence == data.Sequence, ct);
                if (original is not null)
                    return original.PayloadHash == hash ? IngestResult.Duplicate
                        : await Reject(topic, hash, receivedAtUtc, "sequence_payload_conflict", ct, transaction);
                if (data.SourceType != session.SourceType || data.SourceName != session.SourceName)
                    return await Reject(topic, hash, receivedAtUtc, "session_source_changed", ct, transaction);
            }
            if (!camera.IsActive || !camera.Site.IsActive)
                return await Reject(topic, hash, receivedAtUtc, "inactive_camera_or_site", ct, transaction);

            var externalZoneIds = data.Zones.Select(x => x.ExternalZoneId).ToArray();
            var zones = await db.CameraZones.AsNoTracking()
                .Where(x => x.CameraId == camera.Id && externalZoneIds.Contains(x.ExternalZoneId))
                .ToDictionaryAsync(x => x.ExternalZoneId, StringComparer.Ordinal, ct);
            if (zones.Count != data.Zones.Count)
                return await Reject(topic, hash, receivedAtUtc, "unknown_zone", ct, transaction);

            var current = await db.CameraCurrentStates.FindAsync([camera.Id], ct);
            var currentSession = current is null ? null : await db.AnalyticsSessions.FindAsync([current.AnalyticsSessionId], ct);
            var isNew = session is null;
            var ordered = isNew
                ? currentSession is null || !ObservationRules.IsBeforeCurrentSession(data, currentSession)
                : current?.AnalyticsSessionId == session!.Id && ObservationRules.IsOrdered(data, session);
            session ??= NewSession(camera.Id, data, receivedAtUtc, now);
            var startsLoop = ordered && !isNew && ObservationRules.StartsVideoLoop(data, session);
            if (startsLoop)
                await CloseVisits(session, null, now, "video_loop_changed", ct);
            // An omitted loop continues the last known loop under the normal position check.
            if (ordered && data.IsVideo && data.VideoLoop is null)
                data = data with { VideoLoop = session.LastVideoLoop };
            var message = new ReceivedAnalyticsMessage
            {
                AnalyticsSessionId = session.Id, Sequence = data.Sequence, Topic = topic, PayloadHash = hash,
                SourceTimestampUtc = data.SourceTimestampUtc, ReceivedAtUtc = receivedAtUtc, ProcessedAtUtc = now,
                ProcessingStatus = ordered ? MessageStatus.Accepted : MessageStatus.HistoricalOnly,
                RejectionReason = ordered ? null : "out_of_order_or_archived_session"
            };
            db.ReceivedAnalyticsMessages.Add(message);
            var cameraSnapshot = new CameraSnapshot
            {
                ReceivedMessageId = message.Id, CameraId = camera.Id, AnalyticsSessionId = session.Id,
                Sequence = data.Sequence, SourceTimeMs = data.SourceTimeMs, VideoLoop = data.VideoLoop,
                SourceTimestampUtc = data.SourceTimestampUtc,
                ReceivedAtUtc = receivedAtUtc, ProcessingFps = data.ProcessingFps, StaffCount = data.StaffCount,
                AnalyticsStatus = data.Status, IsOrdered = ordered
            };
            db.CameraSnapshots.Add(cameraSnapshot);
            var stateVersion = (current?.StateVersion ?? 0) + 1;
            var zoneUpdates = new List<ZoneCurrentState>();
            foreach (var observation in data.Zones)
            {
                var zone = zones[observation.ExternalZoneId];
                if (!zone.IsActive)
                    continue;
                var zoneSnapshot = SaveZoneSnapshot(zone.Id, session.Id, message, data, observation, ordered);
                if (ordered)
                    zoneUpdates.Add(await ApplyZone(camera.Id, zone.Id, session.Id, message, zoneSnapshot, data, observation,
                        stateVersion, now, ct));
            }

            if (ordered)
            {
                if (!isNew && !startsLoop)
                    await CloseCameraObservation(session, data, ct);
                else if (isNew && currentSession is not null && currentSession.EndedAtUtc is null)
                {
                    currentSession.Status = "superseded";
                    currentSession.EndedAtUtc = receivedAtUtc;
                    await CloseVisits(currentSession, null, now, "session_superseded", ct);
                }

                session.LastSequence = data.Sequence;
                session.FirstSourceTimeMs ??= data.SourceTimeMs;
                session.LastSourceTimeMs = startsLoop ? data.SourceTimeMs : data.SourceTimeMs ?? session.LastSourceTimeMs;
                session.LastVideoLoop = data.VideoLoop;
                session.LastSourceTimestampUtc = data.SourceTimestampUtc;
                session.LastReceivedAtUtc = receivedAtUtc;
                session.Status = data.Status;
                if (data.Status == "ended")
                {
                    session.EndedAtUtc = receivedAtUtc;
                    await CloseVisits(session, data, now, "session_ended", ct);
                }

                var statusChanged = current is null || current.AnalyticsStatus != data.Status ||
                    current.DataFreshness != DataFreshness.Fresh || current.AnalyticsSessionId != session.Id;
                if (current is null)
                {
                    current = new() { CameraId = camera.Id };
                    db.CameraCurrentStates.Add(current);
                }
                current.AnalyticsSessionId = session.Id;
                current.LastSequence = data.Sequence;
                current.LastVideoLoop = data.VideoLoop;
                current.AnalyticsStatus = data.Status;
                current.SourceType = data.SourceType;
                current.ProcessingFps = data.ProcessingFps;
                current.StaffCount = data.StaffCount;
                current.LastSourceTimestampUtc = data.SourceTimestampUtc;
                current.LastReceivedAtUtc = receivedAtUtc;
                current.StateVersion = stateVersion;
                current.DataFreshness = DataFreshness.Fresh;
                notice = new(camera.SiteId, current, zoneUpdates, statusChanged);
            }
            else if (isNew)
            {
                session.Status = "historicalOnly";
                session.EndedAtUtc = receivedAtUtc;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            result = ordered ? IngestResult.Accepted : IngestResult.HistoricalOnly;
        }

        if (notice is not null)
        {
            try { await notifications.AnalyticsCommitted(notice, ct); }
            catch (Exception e) { logger.LogWarning(e, "SignalR notification failed for committed camera {CameraId}", notice.Camera.CameraId); }
        }
        return result;
    }

    private AnalyticsSession NewSession(Guid cameraId, CameraObservation data, DateTime received, DateTime now)
    {
        var result = new AnalyticsSession
        {
            CameraId = cameraId, ExternalSessionId = data.ExternalSessionId, SourceType = data.SourceType, SourceName = data.SourceName,
            FirstSequence = data.Sequence, LastSequence = data.Sequence, FirstSourceTimeMs = data.SourceTimeMs,
            LastSourceTimeMs = data.SourceTimeMs, FirstSourceTimestampUtc = data.SourceTimestampUtc,
            LastSourceTimestampUtc = data.SourceTimestampUtc, StartedAtUtc = received, LastReceivedAtUtc = received, CreatedAtUtc = now
        };
        db.AnalyticsSessions.Add(result);
        return result;
    }

    private ZoneSnapshot SaveZoneSnapshot(Guid zoneId, Guid sessionId, ReceivedAnalyticsMessage message, CameraObservation data,
        ZoneObservation observation, bool ordered)
    {
        var counters = observation.Counters;
        var snapshot = new ZoneSnapshot
        {
            CameraZoneId = zoneId, AnalyticsSessionId = sessionId, ReceivedMessageId = message.Id, Sequence = data.Sequence,
            SourceTimeMs = data.SourceTimeMs, VideoLoop = data.VideoLoop, SourceTimestampUtc = data.SourceTimestampUtc,
            ReceivedAtUtc = message.ReceivedAtUtc,
            Occupancy = observation.Occupancy, ZoneArrivals = counters.Arrivals, ZoneDepartures = counters.Departures,
            Entry = counters.Entry, Exit = counters.Exit, WrongWayIn = counters.WrongWayIn, WrongWayOut = counters.WrongWayOut,
            ReportedAverageDwellSeconds = observation.AverageDwellSeconds, ActiveVisitorsCount = observation.Visitors.Count,
            LongestActiveDwellSeconds = observation.LongestActiveDwell, IsOrdered = ordered
        };
        db.ZoneSnapshots.Add(snapshot);
        return snapshot;
    }

    private async Task<ZoneCurrentState> ApplyZone(Guid cameraId, Guid zoneId, Guid sessionId, ReceivedAnalyticsMessage message,
        ZoneSnapshot snapshot, CameraObservation data, ZoneObservation observation, long stateVersion, DateTime now, CancellationToken ct)
    {
        var counter = await db.ZoneCounterStates.FindAsync([sessionId, zoneId], ct);
        var elapsed = counter is null ? 0 : ObservationRules.Elapsed(data, counter.LastSourceTimestampUtc,
            counter.LastSourceTimeMs, counter.LastVideoLoop);
        var contiguous = counter is not null && data.Sequence - counter.LastSequence == 1 && elapsed <= Policy.MaximumSnapshotGapSeconds;
        Counters? prior = counter is null ? null : new(counter.ZoneArrivals, counter.ZoneDepartures, counter.Entry, counter.Exit,
            counter.WrongWayIn, counter.WrongWayOut);
        var result = CounterCalculator.Calculate(prior, observation.Counters, !contiguous);
        var delta = result.Counters;
        db.ZoneCounterDeltas.Add(new()
        {
            AnalyticsSessionId = sessionId, CameraZoneId = zoneId, ReceivedMessageId = message.Id, EventTimeUtc = data.SourceTimestampUtc,
            VideoLoop = data.VideoLoop, FromSourceTimeMs = data.VideoLoop == counter?.LastVideoLoop ? counter?.LastSourceTimeMs : null,
            ToSourceTimeMs = data.SourceTimeMs, CounterQuality = result.Quality,
            ResetMask = result.ResetMask, HasObservationGap = result.HasGap, ZoneArrivalsDelta = delta.Arrivals,
            ZoneDeparturesDelta = delta.Departures, EntryDelta = delta.Entry, ExitDelta = delta.Exit,
            WrongWayInDelta = delta.WrongWayIn, WrongWayOutDelta = delta.WrongWayOut
        });
        if (counter is null)
        {
            counter = new() { AnalyticsSessionId = sessionId, CameraZoneId = zoneId };
            db.ZoneCounterStates.Add(counter);
        }
        else if (double.IsFinite(elapsed) && await db.ZoneSnapshots.SingleOrDefaultAsync(x => x.ReceivedMessageId == counter.LastReceivedMessageId
            && x.CameraZoneId == zoneId, ct) is { } previous)
        {
            var seconds = Math.Min(elapsed, contiguous ? Policy.MaximumSnapshotGapSeconds : Policy.ExpectedMessageIntervalSeconds);
            if (data.IsVideo)
                previous.ObservationEndSourceMs = previous.SourceTimeMs!.Value + (long)(seconds * 1000);
            else
                previous.ObservationEndUtc = previous.SourceTimestampUtc.AddSeconds(seconds);
            await AggregateQueue.Mark(db, zoneId, data.SourceType, previous.SourceTimestampUtc,
                previous.ObservationEndUtc ?? previous.SourceTimestampUtc, now, ct);
        }

        counter.LastReceivedMessageId = message.Id;
        counter.LastSequence = data.Sequence;
        counter.LastSourceTimeMs = data.SourceTimeMs;
        counter.LastVideoLoop = data.VideoLoop;
        counter.LastSourceTimestampUtc = data.SourceTimestampUtc;
        counter.ZoneArrivals = snapshot.ZoneArrivals;
        counter.ZoneDepartures = snapshot.ZoneDepartures;
        counter.Entry = snapshot.Entry;
        counter.Exit = snapshot.Exit;
        counter.WrongWayIn = snapshot.WrongWayIn;
        counter.WrongWayOut = snapshot.WrongWayOut;

        if (data.Status != "ended")
        {
            var visits = await OpenVisits(sessionId)
                .Where(x => x.CameraZoneId == zoneId && x.VideoLoop == data.VideoLoop).ToListAsync(ct);
            db.TrackedVisits.AddRange(VisitTracker.Observe(visits, observation.Visitors, cameraId, zoneId, sessionId,
                data, now, elapsed, contiguous, Policy.TrackMissingGraceSeconds));
        }
        await AggregateQueue.Mark(db, zoneId, data.SourceType, data.SourceTimestampUtc, data.SourceTimestampUtc, now, ct);

        var current = await db.ZoneCurrentStates.FindAsync([zoneId], ct);
        if (current is null)
        {
            current = new() { CameraZoneId = zoneId };
            db.ZoneCurrentStates.Add(current);
        }
        current.AnalyticsSessionId = sessionId;
        current.LastSequence = data.Sequence;
        current.StateVersion = stateVersion;
        current.Occupancy = snapshot.Occupancy;
        current.ZoneArrivals = snapshot.ZoneArrivals;
        current.ZoneDepartures = snapshot.ZoneDepartures;
        current.Entry = snapshot.Entry;
        current.Exit = snapshot.Exit;
        current.WrongWayIn = snapshot.WrongWayIn;
        current.WrongWayOut = snapshot.WrongWayOut;
        current.ReportedAverageDwellSeconds = snapshot.ReportedAverageDwellSeconds;
        current.ActiveVisitorsCount = snapshot.ActiveVisitorsCount;
        current.LongestActiveDwellSeconds = snapshot.LongestActiveDwellSeconds;
        current.LastUpdatedAtUtc = message.ReceivedAtUtc;
        return current;
    }

    private IQueryable<TrackedVisit> OpenVisits(Guid sessionId) => db.TrackedVisits.Where(x => x.AnalyticsSessionId == sessionId
        && (x.Status == VisitStatus.Active || x.Status == VisitStatus.TemporarilyMissing));

    private async Task CloseVisits(AnalyticsSession session, CameraObservation? data, DateTime now, string reason, CancellationToken ct)
    {
        var persisted = await OpenVisits(session.Id).ToListAsync(ct);
        var open = persisted.Concat(db.TrackedVisits.Local.Where(x => x.AnalyticsSessionId == session.Id))
            .Distinct().Where(x => x.Status is VisitStatus.Active or VisitStatus.TemporarilyMissing);
        foreach (var visit in open)
        {
            VisitTracker.Resolve(visit, data, now, VisitStatus.Uncertain, reason);
            await AggregateQueue.Mark(db, visit.CameraZoneId, session.SourceType, visit.ResolvedAtUtc!.Value,
                visit.ResolvedAtUtc.Value, now, ct);
        }
    }

    private async Task CloseCameraObservation(AnalyticsSession session, CameraObservation data, CancellationToken ct)
    {
        var prior = await db.CameraSnapshots.SingleOrDefaultAsync(x => x.AnalyticsSessionId == session.Id
            && x.Sequence == session.LastSequence && x.IsOrdered, ct);
        if (prior is null)
            return;
        var elapsed = ObservationRules.Elapsed(data, prior.SourceTimestampUtc, prior.SourceTimeMs, prior.VideoLoop);
        if (!double.IsFinite(elapsed))
            return;
        var cap = data.Sequence - prior.Sequence == 1 && elapsed <= Policy.MaximumSnapshotGapSeconds
            ? Policy.MaximumSnapshotGapSeconds : Policy.ExpectedMessageIntervalSeconds;
        var seconds = Math.Min(cap, elapsed);
        if (data.IsVideo)
            prior.ObservationEndSourceMs = prior.SourceTimeMs!.Value + (long)(seconds * 1000);
        else
            prior.ObservationEndUtc = prior.SourceTimestampUtc.AddSeconds(seconds);
    }

    private async Task<IngestResult> Reject(string topic, string hash, DateTime received, string reason, CancellationToken ct,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null)
    {
        db.IngestionRejections.Add(new() { Topic = topic[..Math.Min(topic.Length, 200)], PayloadHash = hash,
            ReceivedAtUtc = received, Reason = reason });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        logger.LogWarning("MQTT analytics rejected with reason {Reason}", reason);
        return IngestResult.Rejected;
    }
}
