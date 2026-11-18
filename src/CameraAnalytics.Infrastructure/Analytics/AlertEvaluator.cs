using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed class AlertEvaluator(AnalyticsDb db, AlertStore store, IOptions<AnalyticsOptions> settings)
{
    public async Task Evaluate(Guid ruleId, DateTime now, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var rule = await db.AlertRules.SingleAsync(x => x.Id == ruleId, ct);
        var alert = await store.Open(ruleId, ct);
        var camera = await db.Cameras.AsNoTracking().Where(x => x.Id == rule.CameraId)
            .Select(x => new { Active = x.IsActive && x.Site.IsActive, x.Site.TimeZoneId }).SingleAsync(ct);
        var state = await db.CameraCurrentStates.AsNoTracking().SingleOrDefaultAsync(x => x.CameraId == rule.CameraId, ct);
        if (state is null)
            return;
        var zone = rule.CameraZoneId.HasValue ? await (from z in db.CameraZones.AsNoTracking()
            join current in db.ZoneCurrentStates.AsNoTracking() on z.Id equals current.CameraZoneId
            where z.Id == rule.CameraZoneId
            select new { z.IsActive, current.LastUpdatedAtUtc, current.AnalyticsSessionId }).SingleOrDefaultAsync(ct) : null;
        var video = state.SourceType == "video_file";
        var freshness = ObservationRules.Freshness(now, state.LastReceivedAtUtc, settings.Value);
        string? reason = null;
        if (!rule.IsActive || !camera.Active || zone is { IsActive: false })
            reason = "scope_disabled";
        else if (state.AnalyticsStatus == "ended")
            reason = "session_ended";
        else if (alert is not null && (alert.SessionId != state.AnalyticsSessionId || alert.VideoLoop != state.LastVideoLoop))
            reason = alert.SessionId != state.AnalyticsSessionId ? "session_changed" : "video_loop_changed";
        else if (state.SourceType != "live" && !(rule.AllowVideo && video))
            reason = "source_not_enabled";
        else if (rule.Kind != "CameraNoRecentData" && (freshness == DataFreshness.NoRecentData
            || zone is not null && ObservationRules.Freshness(now, zone.LastUpdatedAtUtc, settings.Value) == DataFreshness.NoRecentData))
            reason = "observation_unavailable";
        if (reason is not null)
        {
            if (alert is null) return;
            AlertStore.Close(rule, alert, now, reason);
            await Commit(alert);
            return;
        }
        if (rule.CameraZoneId.HasValue && (zone is null || zone.AnalyticsSessionId != state.AnalyticsSessionId))
            return;
        if (rule.Kind != "CameraNoRecentData" && (freshness != DataFreshness.Fresh
            || (zone is not null && ObservationRules.Freshness(now, zone.LastUpdatedAtUtc, settings.Value) != DataFreshness.Fresh)))
            return;
        var timeline = await Timeline(rule, state, now, camera.TimeZoneId, ct);
        if (timeline is null)
            return;
        var evaluation = await Measure(rule, state, timeline, now, freshness, ct);
        if (evaluation is null)
            return;
        var (satisfied, recovered, evidence) = evaluation;
        if (alert is not null && recovered)
        {
            AlertStore.Close(rule, alert, now, "observed_recovery");
            await Commit(alert);
        }
        else if (satisfied)
        {
            var json = JsonSerializer.Serialize(evidence, AlertStore.Json);
            if (alert is not null)
            {
                if (alert.EvidenceJson == json || rule.Kind == "CameraNoRecentData") return;
                alert.EvidenceJson = json;
                alert.LastObservedAtUtc = now;
                alert.StateVersion++;
                await Commit(null);
                return;
            }
            if (rule.LastResolvedAtUtc.HasValue && now - rule.LastResolvedAtUtc.Value < TimeSpan.FromSeconds(rule.CooldownSeconds))
                return;
            alert = db.AnalyticsAlerts.Add(new()
            {
                AlertRuleId = rule.Id, SiteId = rule.SiteId, CameraId = rule.CameraId, CameraZoneId = rule.CameraZoneId,
                SessionId = state.AnalyticsSessionId, VideoLoop = state.LastVideoLoop, Kind = rule.Kind, Severity = rule.Severity,
                OpenedAtUtc = now, LastObservedAtUtc = now, StateVersion = 1, EvidenceJson = json
            }).Entity;
            await Commit(alert);
        }

        async Task Commit(AnalyticsAlert? notification)
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (notification is not null)
                await store.Notify(notification, ct);
        }
    }

    private async Task<ReportTimeline?> Timeline(AlertRule rule, CameraCurrentState state, DateTime now, string zoneId, CancellationToken ct)
    {
        if (state.SourceType == "live")
        {
            var from = now.AddSeconds(-rule.WindowSeconds);
            return new(new("live", "eventTime", "minute", zoneId, from, now, null, null),
                new(ReportTimeline.Ms(from), ReportTimeline.Ms(now)), Input.TimeZone(zoneId));
        }
        var position = await db.AnalyticsSessions.Where(x => x.Id == state.AnalyticsSessionId)
            .Select(x => x.LastSourceTimeMs).SingleAsync(ct);
        if (position is null or long.MaxValue)
            return null;
        var end = position.Value + 1;
        var start = Math.Max(0, end - rule.WindowSeconds * 1000L);
        return new(new("video_file", "sourceTime", "minute", zoneId, null, null, start, end, state.LastVideoLoop),
            new(start, end), Input.TimeZone(zoneId));
    }

    private async Task<Evaluation?> Measure(AlertRule rule, CameraCurrentState state, ReportTimeline timeline, DateTime now,
        DataFreshness freshness, CancellationToken ct)
    {
        var window = timeline.Window;
        if (rule.Kind == "CameraNoRecentData")
        {
            var seconds = Math.Max(0, (now - state.LastReceivedAtUtc).TotalSeconds);
            var measured = new AlertEvidence(timeline.Period, timeline.Describe(window), seconds, rule.Threshold,
                "seconds since receipt", 0, SmartMetricMath.Quality(new("Reported", 1, 0, 0, 0, 0, 0), 0, true, "Reported",
                    freshness != DataFreshness.Fresh));
            return new(freshness == DataFreshness.NoRecentData && seconds >= Math.Max(rule.Threshold, rule.MinimumObservedSeconds),
                freshness == DataFreshness.Fresh, measured);
        }
        var data = new ReportData(db, settings.Value.MaximumSnapshotGapSeconds);
        if (rule.Kind == "WrongWaySpike")
        {
            var stats = new ReportBins(timeline, []);
            await data.Zone(timeline, window, rule.CameraZoneId!.Value, state.AnalyticsSessionId, stats, ct);
            var total = stats.Total;
            if (total.Coverage(window.Seconds) < rule.MinimumCoveragePercent || total.Gaps > 0 || total.Resets > 0
                || total.Baselines > 0 || total.Samples == 0)
                return null;
            var count = (double)total.Traffic.WrongWayEvents;
            return new(count >= rule.Threshold && total.Duration >= rule.MinimumObservedSeconds,
                count < rule.Threshold && total.Duration >= rule.RecoverySeconds, new(timeline.Period,
                    timeline.Describe(window), count, rule.Threshold, "events", total.Duration,
                    SmartMetricMath.Quality(total.Quality(window.Seconds), window.Seconds, true)));
        }

        var samples = new List<ConditionSample>();
        var identifier = rule.CameraZoneId;
        int? activeCount = null;
        if (identifier.HasValue)
        {
            var source = data.ZoneSnapshots(timeline, state.AnalyticsSessionId, identifier.Value);
            source = timeline.IsVideo ? source.OrderBy(x => x.SourceTimeMs).ThenBy(x => x.Sequence)
                : source.OrderBy(x => x.SourceTimestampUtc).ThenBy(x => x.Sequence);
            var projected = source.Select(z => new
            {
                z.SourceTimeMs, z.SourceTimestampUtc, z.ObservationEndSourceMs, z.ObservationEndUtc, z.Occupancy,
                z.CapacityAtObservation, z.LongestActiveDwellSeconds,
                Gap = db.ZoneCounterDeltas.Any(d => d.ReceivedMessageId == z.ReceivedMessageId && d.CameraZoneId == identifier && d.HasObservationGap)
            });
            await foreach (var row in projected.AsAsyncEnumerable().WithCancellation(ct))
            {
                var value = rule.Kind == "LongStay" ? row.LongestActiveDwellSeconds ?? 0
                    : ObservationRules.Utilization(row.Occupancy, row.CapacityAtObservation);
                samples.Add(new(timeline.IsVideo ? row.SourceTimeMs!.Value : ReportTimeline.Ms(row.SourceTimestampUtc),
                    timeline.IsVideo ? row.ObservationEndSourceMs : ReportTimeline.Ms(row.ObservationEndUtc), value, row.Gap));
            }
            if (rule.Kind == "LongStay")
                activeCount = await db.TrackedVisits.CountAsync(x => x.CameraZoneId == identifier && x.AnalyticsSessionId == state.AnalyticsSessionId
                    && x.VideoLoop == state.LastVideoLoop && x.Status == VisitStatus.Active && x.LastDwellSeconds >= rule.Threshold, ct);
        }
        else
        {
            await foreach (var sample in data.CameraSamples(timeline, state.CameraId, state.AnalyticsSessionId, ct))
                samples.Add(new(sample.At, sample.Until, sample.Fps, sample.MissingSequences > 0));
        }

        var measurement = AlertCondition.Measure(samples, window, value => rule.Kind == "LowFps"
            ? value < rule.Threshold : value >= rule.Threshold, settings.Value.MaximumSnapshotGapSeconds);
        var coverage = window.Seconds > 0 ? Math.Min(100, measurement.CoveredSeconds / window.Seconds * 100) : 0;
        if (measurement.LatestValue is null || measurement.Satisfied is null || coverage < rule.MinimumCoveragePercent)
            return null;
        var quality = SmartMetricMath.Quality(new("Derived", measurement.SampleCount, coverage, measurement.CoveredSeconds,
            0, 0, measurement.Gaps), window.Seconds, true);
        var unit = rule.Kind switch { "Congestion" => "percent", "LowFps" => "frames/s", _ => "seconds dwell" };
        var evidence = new AlertEvidence(timeline.Period, timeline.Describe(new(measurement.StartedAt ?? window.End, window.End)),
            measurement.LatestValue.Value, rule.Threshold, unit, measurement.SustainedSeconds, quality, activeCount);
        return new(measurement.Satisfied == true && measurement.SustainedSeconds >= rule.MinimumObservedSeconds && activeCount is not 0,
            measurement.Satisfied == false && measurement.SustainedSeconds >= rule.RecoverySeconds, evidence);
    }

    private sealed record Evaluation(bool Satisfied, bool Recovered, AlertEvidence Evidence);
}
