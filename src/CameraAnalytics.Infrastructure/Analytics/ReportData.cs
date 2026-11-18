using System.Runtime.CompilerServices;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Infrastructure.Analytics;

internal sealed class ReportData(AnalyticsDb db, int maximumGapSeconds)
{
    internal IQueryable<ZoneSnapshot> ZoneSnapshots(ReportTimeline timeline, Guid? sessionId, Guid zoneId)
    {
        var snapshots = db.ZoneSnapshots.AsNoTracking().Where(x => x.CameraZoneId == zoneId && x.IsOrdered
            && (!sessionId.HasValue || x.AnalyticsSessionId == sessionId) && x.Session.SourceType == timeline.Period.SourceType);
        return timeline.IsVideo
            ? snapshots.Where(x => x.VideoLoop == timeline.Period.VideoLoop && x.SourceTimeMs >= Math.Max(0, timeline.Window.Start - maximumGapSeconds * 1000L)
                && x.SourceTimeMs < timeline.Window.End)
            : snapshots.Where(x => x.SourceTimestampUtc >= timeline.Period.From!.Value.AddSeconds(-maximumGapSeconds) && x.SourceTimestampUtc < timeline.Period.To);
    }

    internal async Task Capacity(ReportTimeline timeline, Guid? sessionId, Guid zoneId, CongestionCalculator calculator, CancellationToken ct)
    {
        var snapshots = ZoneSnapshots(timeline, sessionId, zoneId);
        var ordered = timeline.IsVideo ? snapshots.OrderBy(x => x.SourceTimeMs).ThenBy(x => x.Sequence)
            : snapshots.OrderBy(x => x.SourceTimestampUtc).ThenBy(x => x.Sequence);
        var samples = ordered.Select(x => new
        {
            x.AnalyticsSessionId, x.SourceTimeMs, x.SourceTimestampUtc, x.VideoLoop, x.ObservationEndSourceMs, x.ObservationEndUtc,
            x.Occupancy, x.CapacityAtObservation, x.WarningThresholdAtObservation, x.CriticalThresholdAtObservation,
            Ended = db.CameraSnapshots.Any(c => c.ReceivedMessageId == x.ReceivedMessageId && c.AnalyticsStatus == "ended"),
            Gap = db.ZoneCounterDeltas.Any(d => d.ReceivedMessageId == x.ReceivedMessageId && d.CameraZoneId == zoneId && d.HasObservationGap)
        });
        await foreach (var x in samples.AsAsyncEnumerable().WithCancellation(ct))
            calculator.Observe(new(x.AnalyticsSessionId, x.VideoLoop,
                timeline.IsVideo ? x.SourceTimeMs!.Value : ReportTimeline.Ms(x.SourceTimestampUtc),
                timeline.IsVideo ? x.ObservationEndSourceMs : ReportTimeline.Ms(x.ObservationEndUtc), x.Occupancy,
                x.CapacityAtObservation, x.WarningThresholdAtObservation, x.CriticalThresholdAtObservation, x.Ended, x.Gap));
    }

    public async Task Zone(ReportTimeline timeline, NumericWindow range, Guid zoneId, Guid? sessionId, ReportBins bins, CancellationToken ct)
    {
        var snapshots = db.ZoneSnapshots.AsNoTracking().Where(x => x.CameraZoneId == zoneId && x.IsOrdered
            && (!sessionId.HasValue || x.AnalyticsSessionId == sessionId) && x.Session.SourceType == timeline.Period.SourceType);
        var deltas = db.ZoneCounterDeltas.AsNoTracking().Where(x => x.CameraZoneId == zoneId
            && (!sessionId.HasValue || x.AnalyticsSessionId == sessionId) && x.Session.SourceType == timeline.Period.SourceType);
        var visits = db.TrackedVisits.AsNoTracking().Where(x => x.CameraZoneId == zoneId
            && (!sessionId.HasValue || x.AnalyticsSessionId == sessionId) && x.Session.SourceType == timeline.Period.SourceType
            && (x.Status == VisitStatus.EstimatedCompleted || x.Status == VisitStatus.Uncertain));
        var left = Math.Max(0, range.Start - maximumGapSeconds * 1000L);
        if (timeline.IsVideo)
        {
            var loop = timeline.Period.VideoLoop;
            var observations = snapshots.Where(x => x.VideoLoop == loop && x.SourceTimeMs >= left && x.SourceTimeMs < range.End)
                .OrderBy(x => x.SourceTimeMs).ThenBy(x => x.Sequence)
                .Select(x => new { x.SourceTimeMs, x.ObservationEndSourceMs, x.Occupancy, x.ReportedAverageDwellSeconds, x.LongestActiveDwellSeconds });
            await foreach (var x in observations.AsAsyncEnumerable().WithCancellation(ct))
                bins.Observe(range, new(x.SourceTimeMs!.Value, x.ObservationEndSourceMs, x.Occupancy, x.ReportedAverageDwellSeconds, x.LongestActiveDwellSeconds));

            await foreach (var x in deltas.Where(x => x.VideoLoop == loop && x.ToSourceTimeMs >= range.Start
                && x.ToSourceTimeMs < range.End).AsAsyncEnumerable().WithCancellation(ct))
                bins.Traffic(x.ToSourceTimeMs!.Value, x);
            await foreach (var x in visits.Where(x => x.VideoLoop == loop && x.ResolvedSourceTimeMs >= range.Start
                && x.ResolvedSourceTimeMs < range.End).AsAsyncEnumerable().WithCancellation(ct))
                bins.Visit(x.ResolvedSourceTimeMs!.Value, x);
        }
        else
        {
            var from = ReportTimeline.Utc(range.Start);
            var to = ReportTimeline.Utc(range.End);
            var earliest = from.AddSeconds(-maximumGapSeconds);
            var observations = snapshots.Where(x => x.SourceTimestampUtc >= earliest && x.SourceTimestampUtc < to)
                .OrderBy(x => x.SourceTimestampUtc).ThenBy(x => x.Sequence)
                .Select(x => new { x.SourceTimestampUtc, x.ObservationEndUtc, x.Occupancy, x.ReportedAverageDwellSeconds, x.LongestActiveDwellSeconds });
            await foreach (var x in observations.AsAsyncEnumerable().WithCancellation(ct))
                bins.Observe(range, new(ReportTimeline.Ms(x.SourceTimestampUtc), ReportTimeline.Ms(x.ObservationEndUtc),
                    x.Occupancy, x.ReportedAverageDwellSeconds, x.LongestActiveDwellSeconds));

            await foreach (var x in deltas.Where(x => x.EventTimeUtc >= from && x.EventTimeUtc < to).AsAsyncEnumerable().WithCancellation(ct))
                bins.Traffic(ReportTimeline.Ms(x.EventTimeUtc), x);
            await foreach (var x in visits.Where(x => x.ResolvedAtUtc >= from && x.ResolvedAtUtc < to).AsAsyncEnumerable().WithCancellation(ct))
                bins.Visit(ReportTimeline.Ms(x.ResolvedAtUtc!.Value), x);
        }
    }

    public async Task Staff(ReportTimeline timeline, Guid cameraId, Guid? sessionId, ReportBins bins, CancellationToken ct)
    {
        var range = timeline.Window;
        var snapshots = db.CameraSnapshots.AsNoTracking().Where(x => x.CameraId == cameraId && x.IsOrdered
            && (!sessionId.HasValue || x.AnalyticsSessionId == sessionId) && x.Session.SourceType == timeline.Period.SourceType);
        if (timeline.IsVideo)
        {
            var earliest = Math.Max(0, range.Start - maximumGapSeconds * 1000L);
            var loop = timeline.Period.VideoLoop;
            var projected = snapshots.Where(x => x.VideoLoop == loop && x.SourceTimeMs >= earliest && x.SourceTimeMs < range.End)
                .Select(x => new { x.SourceTimeMs, x.ObservationEndSourceMs, x.StaffCount });
            await foreach (var x in projected.AsAsyncEnumerable().WithCancellation(ct))
                bins.Observe(range, new(x.SourceTimeMs!.Value, x.ObservationEndSourceMs, x.StaffCount));
        }
        else
        {
            var earliest = ReportTimeline.Utc(range.Start).AddSeconds(-maximumGapSeconds);
            var until = ReportTimeline.Utc(range.End);
            var projected = snapshots.Where(x => x.SourceTimestampUtc >= earliest && x.SourceTimestampUtc < until)
                .Select(x => new { x.SourceTimestampUtc, x.ObservationEndUtc, x.StaffCount });
            await foreach (var x in projected.AsAsyncEnumerable().WithCancellation(ct))
                bins.Observe(range, new(ReportTimeline.Ms(x.SourceTimestampUtc), ReportTimeline.Ms(x.ObservationEndUtc), x.StaffCount));
        }
    }

    public async IAsyncEnumerable<CameraSample> CameraSamples(ReportTimeline timeline, Guid cameraId, Guid? sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var range = timeline.Window;
        var snapshots = db.CameraSnapshots.AsNoTracking().Where(x => x.CameraId == cameraId && x.IsOrdered
            && (!sessionId.HasValue || x.AnalyticsSessionId == sessionId) && x.Session.SourceType == timeline.Period.SourceType);
        if (timeline.IsVideo)
        {
            var earliest = Math.Max(0, range.Start - maximumGapSeconds * 1000L);
            var projected = snapshots.Where(x => x.VideoLoop == timeline.Period.VideoLoop && x.SourceTimeMs >= earliest && x.SourceTimeMs < range.End)
                .OrderBy(x => x.SourceTimeMs).ThenBy(x => x.Sequence).Select(x => new CameraSample(x.SourceTimeMs!.Value,
                    x.ObservationEndSourceMs, x.StaffCount, x.ProcessingFps, x.MissingSequences, x.ReportedMetadataJson, null));
            await foreach (var row in projected.AsAsyncEnumerable().WithCancellation(ct)) yield return row;
        }
        else
        {
            var earliest = ReportTimeline.Utc(range.Start).AddSeconds(-maximumGapSeconds);
            var until = ReportTimeline.Utc(range.End);
            var projected = snapshots.Where(x => x.SourceTimestampUtc >= earliest && x.SourceTimestampUtc < until)
                .OrderBy(x => x.SourceTimestampUtc).ThenBy(x => x.Sequence).Select(x => new { x.SourceTimestampUtc, x.ObservationEndUtc,
                    x.StaffCount, x.ProcessingFps, x.MissingSequences, x.ReportedMetadataJson, x.ReceivedAtUtc });
            await foreach (var x in projected.AsAsyncEnumerable().WithCancellation(ct))
                yield return new(ReportTimeline.Ms(x.SourceTimestampUtc), ReportTimeline.Ms(x.ObservationEndUtc), x.StaffCount,
                    x.ProcessingFps, x.MissingSequences, x.ReportedMetadataJson, (x.ReceivedAtUtc - x.SourceTimestampUtc).TotalMilliseconds);
        }
    }
}

internal sealed record CameraSample(long At, long? Until, int Staff, double Fps, long? MissingSequences, string? Metadata, double? LatencyMs);
