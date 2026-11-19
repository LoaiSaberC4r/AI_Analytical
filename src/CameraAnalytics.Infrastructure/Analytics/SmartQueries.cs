using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed partial class SmartQueries(AnalyticsDb db, ReportQueries reports, TimeProvider clock, IOptions<AnalyticsOptions> analytics,
    IOptions<VisualizationOptions> visualization, IOptions<CongestionOptions> congestion, IOptions<DwellAnalyticsOptions> dwell) : ISmartQueries
{
    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    public async Task<FlowReport> Flow(ReportFilter filter, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Read(db, ct);
        var scope = await Resolve(filter, ct);
        var timeline = scope.Timeline;
        var periods = Periods(timeline);
        var zones = await Zones(scope.Filter, ct);
        var result = new List<ZoneFlow>(zones.Count);
        foreach (var zone in zones)
        {
            var bins = new ReportBins(timeline, periods);
            await reports.ReadZone(timeline, bins, zone.Id, scope.Filter.SessionId, ct, useMinutes: true);
            var minimum = visualization.Value.MinimumRateCoveragePercent;
            var snapshots = new ReportData(db, analytics.Value.MaximumSnapshotGapSeconds).ZoneSnapshots(timeline, scope.Filter.SessionId, zone.Id);
            var metadata = timeline.IsVideo
                ? await snapshots.Where(x => x.SourceTimeMs >= timeline.Window.Start).OrderByDescending(x => x.SourceTimeMs)
                    .ThenByDescending(x => x.Sequence).Select(x => x.ReportedMetadataJson).FirstOrDefaultAsync(ct)
                : await snapshots.Where(x => x.SourceTimestampUtc >= timeline.Period.From).OrderByDescending(x => x.SourceTimestampUtc)
                    .ThenByDescending(x => x.Sequence).Select(x => x.ReportedMetadataJson).FirstOrDefaultAsync(ct);
            result.Add(new(zone.Scope, zone.Name, SmartMetricMath.Flow(timeline, timeline.Window, bins.Total, minimum),
                bins.Series.Select(x => SmartMetricMath.Flow(timeline, x.Period, x.Stats, minimum)).ToArray(), ParseMetadata(metadata)));
        }
        return new(timeline.Period with { SessionId = scope.Filter.SessionId }, result, ReadScope(scope.Filter));
    }

    public async Task<CongestionReport> Congestion(ReportFilter filter, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Read(db, ct);
        var scope = await Resolve(filter, ct);
        var timeline = scope.Timeline;
        var zones = await Zones(scope.Filter, ct);
        var result = new List<CongestionZone>(zones.Count);
        foreach (var zone in zones)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var freshness = ObservationRules.Freshness(now, zone.ReceivedAt, analytics.Value);
            var stale = !timeline.IsVideo && timeline.Period.To >= now.AddSeconds(-analytics.Value.StaleAfterSeconds)
                && freshness != DataFreshness.Fresh;
            var calculator = new CongestionCalculator(timeline.Window, congestion.Value, analytics.Value);
            await new ReportData(db, analytics.Value.MaximumSnapshotGapSeconds).Capacity(timeline, scope.Filter.SessionId, zone.Id, calculator, ct);
            var duration = timeline.Window.Seconds;
            var coverage = duration > 0 ? Math.Min(100, 100 * calculator.CoveredSeconds / duration) : 0;
            var quality = SmartMetricMath.Quality(new("Derived", calculator.Samples, coverage, calculator.CoveredSeconds, 0, 0,
                calculator.Gaps), duration, calculator.PeakUtilization.HasValue || calculator.CoveredSeconds > 0,
                stale: stale, warning: calculator.CoveredSeconds == 0 ? "no_capacity_coverage" : null);
            var episodes = calculator.Complete(stale && freshness == DataFreshness.NoRecentData).Select(episode =>
                new CongestionEpisodeData(timeline.Describe(episode.Window), episode.Critical ? "Critical" : "Warning",
                    episode.WarningSeconds, episode.CriticalSeconds, episode.Status, episode.ResolutionReason)).ToArray();
            result.Add(new(zone.Scope, zone.Name, calculator.AverageUtilization, calculator.PeakUtilization,
                calculator.CoveredSeconds > 0 ? calculator.AboveWarningSeconds : null,
                calculator.CoveredSeconds > 0 ? calculator.AboveCriticalSeconds : null, quality, episodes,
                calculator.LastObservedWarningThreshold, calculator.LastObservedCriticalThreshold));
        }
        return new(timeline.Period with { SessionId = scope.Filter.SessionId }, result, ReadScope(scope.Filter));
    }

    public async Task<DwellDistributionReport> Dwell(ReportFilter filter, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Read(db, ct);
        var scope = await Resolve(filter, ct);
        var timeline = scope.Timeline;
        var zones = await Zones(scope.Filter, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var result = new List<DwellZoneDistribution>(zones.Count);
        var bounds = dwell.Value.HistogramBounds;
        var threshold = dwell.Value.DefaultLongStaySeconds;
        foreach (var zone in zones)
        {
            var recent = zone.IsActive && ObservationRules.Freshness(now, zone.ReceivedAt, analytics.Value) == DataFreshness.Fresh;
            var state = await (from z in db.ZoneCurrentStates.AsNoTracking()
                join c in db.CameraCurrentStates.AsNoTracking() on zone.CameraId equals c.CameraId
                where z.CameraZoneId == zone.Id && z.AnalyticsSessionId == c.AnalyticsSessionId && c.AnalyticsStatus == "running"
                    && c.SourceType == timeline.Period.SourceType && (!scope.Filter.SessionId.HasValue || c.AnalyticsSessionId == scope.Filter.SessionId)
                    && (!timeline.IsVideo || (c.LastVideoLoop == timeline.Period.VideoLoop && db.ZoneSnapshots.Any(snapshot =>
                        snapshot.CameraZoneId == z.CameraZoneId && snapshot.AnalyticsSessionId == z.AnalyticsSessionId
                        && snapshot.Sequence == z.LastSequence && snapshot.VideoLoop == c.LastVideoLoop && snapshot.IsOrdered)))
                select new { z.AnalyticsSessionId, c.LastVideoLoop }).SingleOrDefaultAsync(ct);
            var active = recent && state is not null ? await db.TrackedVisits.AsNoTracking().Where(x => x.CameraZoneId == zone.Id
                    && x.AnalyticsSessionId == state.AnalyticsSessionId && x.VideoLoop == state.LastVideoLoop && x.Status == VisitStatus.Active)
                .Select(x => x.LastDwellSeconds).ToArrayAsync(ct) : null;
            var visits = db.TrackedVisits.AsNoTracking().Where(x => x.CameraZoneId == zone.Id
                && (!scope.Filter.SessionId.HasValue || x.AnalyticsSessionId == scope.Filter.SessionId)
                && x.Session.SourceType == timeline.Period.SourceType);
            visits = timeline.IsVideo ? visits.Where(x => x.VideoLoop == timeline.Period.VideoLoop
                    && x.ResolvedSourceTimeMs >= timeline.Window.Start && x.ResolvedSourceTimeMs < timeline.Window.End)
                : visits.Where(x => x.ResolvedAtUtc >= timeline.Period.From && x.ResolvedAtUtc < timeline.Period.To);
            var bins = new ReportBins(timeline, []);
            await reports.ReadZone(timeline, bins, zone.Id, scope.Filter.SessionId, ct);
            var statistics = bins.Total;
            var available = statistics.Samples > 0 || statistics.Duration > 0 || statistics.EstimatedVisits > 0 || statistics.UncertainVisits > 0;
            var completed = new DwellHistogram(bounds);
            await foreach (var seconds in visits.Where(x => x.Status == VisitStatus.EstimatedCompleted)
                .Select(x => x.LastDwellSeconds).AsAsyncEnumerable().WithCancellation(ct)) completed.Add(seconds);
            var total = completed.Sum;
            var finiteTotal = available && double.IsFinite(total) ? total : (double?)null;
            result.Add(new(zone.Scope, zone.Name, active?.Length, active?.Count(x => x >= threshold),
                active is { Length: > 0 } ? active.Max() : null, available ? completed.Count : null, finiteTotal,
                finiteTotal.HasValue ? VisitTracker.EstimatedAverage(finiteTotal.Value, completed.Count) : null,
                available ? statistics.UncertainVisits : null, active is null ? null : SmartMetricMath.Histogram(active, bounds),
                available ? completed.Bins() : null,
                SmartMetricMath.Quality(new("Derived", active is null ? 0 : 1, 0, 0, 0, 0, 0), 0, active is not null,
                    stale: !recent && zone.ReceivedAt.HasValue),
                SmartMetricMath.Quality(statistics.Quality(timeline.Window.Seconds), timeline.Window.Seconds, available, "Estimated"),
                !timeline.IsVideo && active is not null ? zone.ReceivedAt : null));
        }
        return new(timeline.Period with { SessionId = scope.Filter.SessionId }, threshold, now, result, ReadScope(scope.Filter));
    }

    private async Task<ReportQueries.ReportScope> Resolve(ReportFilter filter, CancellationToken ct)
    {
        if (filter.SourceType == "video_file" && (!filter.SessionId.HasValue || filter.VideoLoop is null
            || !filter.FromSourceTimeMs.HasValue || !filter.ToSourceTimeMs.HasValue))
            throw ApiError.Invalid("Video reports require sessionId, videoLoop, fromSourceTimeMs and toSourceTimeMs.");
        var scope = await reports.Resolve(filter, ct);
        if (!scope.Filter.SiteId.HasValue)
            throw ApiError.Invalid("Select a site, camera, zone or session.");
        if (scope.Timeline.IsVideo && (filter.VideoLoop is null || !filter.FromSourceTimeMs.HasValue || !filter.ToSourceTimeMs.HasValue))
            throw ApiError.Invalid("Video reports require videoLoop, fromSourceTimeMs and toSourceTimeMs.");
        return scope;
    }

    private NumericWindow[] Periods(ReportTimeline timeline)
    {
        var periods = timeline.Buckets().Take(visualization.Value.MaxPointsPerSeries + 1).ToArray();
        if (periods.Length > visualization.Value.MaxPointsPerSeries)
            throw ApiError.Invalid("The requested interval has too many buckets. Narrow the range or use a larger interval.");
        return periods;
    }

    private async Task<List<ZoneScope>> Zones(ReportFilter filter, CancellationToken ct)
    {
        var zones = await db.CameraZones.AsNoTracking().Where(x => x.Camera.SiteId == filter.SiteId
                && (!filter.CameraId.HasValue || x.CameraId == filter.CameraId) && (!filter.ZoneId.HasValue || x.Id == filter.ZoneId))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(visualization.Value.MaxSeries + 1)
            .Select(x => new ZoneScope(x.Id, x.CameraId, x.Camera.SiteId, x.Name, x.IsActive && x.Camera.IsActive && x.Camera.Site.IsActive,
                db.ZoneCurrentStates.Where(z => z.CameraZoneId == x.Id).Select(z => (DateTime?)z.LastUpdatedAtUtc).FirstOrDefault()))
            .ToListAsync(ct);
        if (zones.Count > visualization.Value.MaxSeries)
            throw ApiError.Invalid("The selection contains too many zone series. Select a camera or zone.");
        return zones;
    }

    private static ReportedAnalyticsMetadata? ParseMetadata(string? json) => json is null
        ? null : JsonSerializer.Deserialize<ReportedAnalyticsMetadata>(json, MetadataJson);

    private static MetricScope ReadScope(ReportFilter filter) => new(filter.SiteId!.Value, filter.CameraId, filter.ZoneId);

    private sealed record ZoneScope(Guid Id, Guid CameraId, Guid SiteId, string Name, bool IsActive, DateTime? ReceivedAt)
    {
        public MetricScope Scope => new(SiteId, CameraId, Id);
    }
}
