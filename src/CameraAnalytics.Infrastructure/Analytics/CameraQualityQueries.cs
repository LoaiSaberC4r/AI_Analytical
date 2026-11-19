using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed partial class SmartQueries
{
    public async Task<QualityReport> Quality(ReportFilter filter, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Read(db, ct);
        var scope = await Resolve(filter, ct);
        var timeline = scope.Timeline;
        var periods = Periods(timeline);
        var cameras = await db.Cameras.AsNoTracking().Where(x => x.SiteId == scope.Filter.SiteId
                && (!scope.Filter.CameraId.HasValue || x.Id == scope.Filter.CameraId))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(visualization.Value.MaxSeries + 1)
            .Select(x => new { x.Id, x.SiteId, x.Name, x.ExternalCameraId }).ToListAsync(ct);
        if (cameras.Count > visualization.Value.MaxSeries)
            throw ApiError.Invalid("The selection contains too many camera series. Select a camera.");
        var result = new List<CameraDiagnostics>(cameras.Count);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var camera in cameras)
        {
            var staff = periods.Select(_ => new CameraSampleStatistics()).ToArray();
            var ends = periods.Select(p => p.End).ToArray();
            string? metadata = null;
            var data = new ReportData(db, analytics.Value.MaximumSnapshotGapSeconds);
            await foreach (var sample in data.CameraSamples(timeline, camera.Id, scope.Filter.SessionId, ct))
            {
                var index = Array.BinarySearch(ends, sample.At);
                if (timeline.Window.Contains(sample.At)) metadata = sample.Metadata;
                for (var i = index >= 0 ? index + 1 : ~index; i < periods.Length && periods[i].Start <= (sample.Until ?? sample.At); i++)
                    staff[i].Observe(periods[i], sample.At, sample.Until, sample.Staff, sample.Fps, sample.MissingSequences, sample.LatencyMs);
            }
            var resets = new int[periods.Length];
            var resetRows = db.ZoneCounterDeltas.AsNoTracking().Where(x => x.Session.CameraId == camera.Id
                && x.Session.SourceType == timeline.Period.SourceType && x.CounterQuality == CounterQuality.Reset
                && (!scope.Filter.ZoneId.HasValue || x.CameraZoneId == scope.Filter.ZoneId)
                && (!scope.Filter.SessionId.HasValue || x.AnalyticsSessionId == scope.Filter.SessionId));
            if (timeline.IsVideo)
            {
                await foreach (var time in resetRows.Where(x => x.VideoLoop == timeline.Period.VideoLoop
                    && x.ToSourceTimeMs >= timeline.Window.Start && x.ToSourceTimeMs < timeline.Window.End)
                    .Select(x => x.ToSourceTimeMs!.Value).AsAsyncEnumerable().WithCancellation(ct)) CountReset(time);
            }
            else
            {
                await foreach (var time in resetRows.Where(x => x.EventTimeUtc >= timeline.Period.From && x.EventTimeUtc < timeline.Period.To)
                    .Select(x => x.EventTimeUtc).AsAsyncEnumerable().WithCancellation(ct)) CountReset(ReportTimeline.Ms(time));
            }
            void CountReset(long time)
            {
                var index = Array.BinarySearch(ends, time);
                var bucket = index >= 0 ? index + 1 : ~index;
                if (bucket < resets.Length) resets[bucket]++;
            }
            var current = await db.CameraCurrentStates.AsNoTracking().Where(x => x.CameraId == camera.Id
                    && x.SourceType == timeline.Period.SourceType && (!scope.Filter.SessionId.HasValue || x.AnalyticsSessionId == scope.Filter.SessionId)
                    && (!timeline.IsVideo || x.LastVideoLoop == timeline.Period.VideoLoop))
                .Select(x => new { x.LastReceivedAtUtc, x.ProcessingFps }).SingleOrDefaultAsync(ct);
            var freshness = ObservationRules.Freshness(now, current?.LastReceivedAtUtc, analytics.Value);
            result.Add(new(new(camera.SiteId, camera.Id), camera.Name, freshness.ToString(), current?.LastReceivedAtUtc,
                freshness == DataFreshness.Fresh ? current?.ProcessingFps : null, staff.Select((x, i) =>
                {
                    var period = periods[i];
                    var quality = x.Staff.Quality(period.Seconds) with { GapCount = x.GapCount, CounterResetCount = resets[i] };
                    return new CameraQualityBucket(timeline.Describe(period), x.Staff.Average, x.Staff.Peak, x.AverageFps, x.MinimumFps,
                        x.MaximumFps, x.MissingSequences, x.AverageLatency, x.LatencySampleCount,
                        SmartMetricMath.Quality(quality, period.Seconds, x.Staff.Duration > 0 || x.Staff.Samples > 0,
                            warning: x.KnownSequenceSamples == 0 ? "sequence_coverage_unknown"
                                : x.KnownSequenceSamples < x.Staff.Samples ? "sequence_coverage_partial" : null));
                }).ToArray(), ParseMetadata(metadata)));
        }

        List<RejectionCount>? rejections = null;
        if (!timeline.IsVideo && !scope.Filter.SessionId.HasValue && !scope.Filter.ZoneId.HasValue)
        {
            var topics = cameras.Select(x => $"cameras/{x.ExternalCameraId}/analytics").ToArray();
            var groups = await db.IngestionRejections.AsNoTracking().Where(x => topics.Contains(x.Topic)
                    && x.ReceivedAtUtc >= timeline.Period.From && x.ReceivedAtUtc < timeline.Period.To)
                .GroupBy(x => x.Reason).Select(g => new { Reason = g.Key, Total = g.Count() }).OrderBy(x => x.Reason).ToListAsync(ct);
            rejections = groups.Select(x => new RejectionCount(x.Reason, x.Total)).ToList();
        }
        return new(timeline.Period with { SessionId = scope.Filter.SessionId }, result, rejections, ReadScope(scope.Filter));
    }
}
