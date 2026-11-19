using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed partial class FloorPlans
{
    public async Task<HeatmapVisualization> Heatmap(FloorData floor, string metric, ReportFilter filter, CancellationToken ct)
    {
        var (title, unit) = metric switch
        {
            "arrivals" => ("Observed arrivals", "events"),
            "averageOccupancy" => ("Weighted occupancy", "people per zone"),
            "freshOccupancy" => ("Fresh occupancy", "people per zone"),
            "utilization" => ("Fresh utilization", "%"),
            "estimatedAverageDwell" => ("Estimated completed dwell", "seconds"),
            "wrongWay" => ("Wrong-way movements", "events"),
            "congestion" => ("Observed critical congestion", "seconds"),
            _ => throw ApiError.Invalid("The heatmap metric is not supported.")
        };
        if (filter.SiteId.HasValue && filter.SiteId != floor.SiteId)
            throw ApiError.Invalid("The floor does not belong to the requested site.");
        var live = metric is "freshOccupancy" or "utilization";
        if (live && (filter.From.HasValue || filter.To.HasValue || filter.SessionId.HasValue
                || filter.SourceType is not (null or "live") || filter.VideoLoop is not null
                || filter.FromSourceTimeMs.HasValue || filter.ToSourceTimeMs.HasValue))
            throw ApiError.Invalid("Fresh metrics use the current live snapshot.");
        await using var transaction = await WriteTransaction.Read(db, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var layouts = await Layouts(floor.Id, filter.CameraId, ct);
        if (filter.ZoneId.HasValue)
            layouts = layouts.Where(x => x.ZoneId == filter.ZoneId).ToArray();
        var zoneIds = layouts.Select(x => x.ZoneId).ToArray();
        var eligibleEnd = MinuteQueue.Floor(now.AddSeconds(-Math.Max(minute.Value.FinalizationDelaySeconds, analytics.Value.MaximumSnapshotGapSeconds)));
        var defaultMinute = !live && filter is { From: null, To: null, SessionId: null, SourceType: null or "live" };
        if (defaultMinute)
        {
            var latest = await db.ZoneMinuteAggregates.AsNoTracking().Where(x => zoneIds.Contains(x.CameraZoneId)
                    && x.MinuteStartUtc < eligibleEnd && x.MinuteStartUtc >= eligibleEnd.AddMinutes(-2)
                    && !db.DirtyZoneMinutes.Any(d => d.CameraZoneId == x.CameraZoneId && d.MinuteStartUtc == x.MinuteStartUtc))
                .MaxAsync(x => (DateTime?)x.MinuteStartUtc, ct);
            var from = latest ?? eligibleEnd.AddMinutes(-1);
            filter = filter with { From = from, To = from.AddMinutes(1), Interval = "minute" };
        }
        if (live)
            filter = filter with { From = now.AddSeconds(-analytics.Value.StaleAfterSeconds), To = now, Interval = "minute" };
        var scope = await reports.Resolve(filter with { SiteId = floor.SiteId }, ct);
        var timeline = scope.Timeline;
        if (scope.Filter.CameraId.HasValue)
            layouts = layouts.Where(x => x.CameraId == scope.Filter.CameraId).ToArray();
        var zones = new List<HeatmapZone>(layouts.Count);
        var data = new ReportData(db, analytics.Value.MaximumSnapshotGapSeconds);
        foreach (var layout in layouts)
        {
            if (!floor.IsActive)
            {
                zones.Add(new(layout, null, SmartMetricMath.Quality(new("Derived", 0, 0, 0, 0, 0, 0), 0, false, warning: "floor_inactive")));
                continue;
            }
            if (live)
            {
                var current = await (from z in db.ZoneCurrentStates.AsNoTracking()
                    join c in db.CameraCurrentStates.AsNoTracking() on layout.CameraId equals c.CameraId
                    join zone in db.CameraZones.AsNoTracking() on z.CameraZoneId equals zone.Id
                    where z.CameraZoneId == layout.ZoneId && z.AnalyticsSessionId == c.AnalyticsSessionId
                        && c.SourceType == "live" && c.AnalyticsStatus == "running"
                        && zone.IsActive && zone.Camera.IsActive && zone.Camera.Site.IsActive
                    select new { z.Occupancy, z.LastUpdatedAtUtc, Capacity = db.ZoneSnapshots
                        .Where(s => s.CameraZoneId == layout.ZoneId && s.AnalyticsSessionId == z.AnalyticsSessionId && s.Sequence == z.LastSequence)
                        .Select(s => s.CapacityAtObservation).FirstOrDefault() }).SingleOrDefaultAsync(ct);
                var fresh = ObservationRules.Freshness(now, current?.LastUpdatedAtUtc, analytics.Value) == DataFreshness.Fresh;
                double? value = !fresh || current is null ? null : metric == "freshOccupancy" ? current.Occupancy
                    : ObservationRules.Utilization(current.Occupancy, current.Capacity);
                zones.Add(new(layout, value, SmartMetricMath.Quality(new("Derived", current is null ? 0 : 1, 0, 0, 0, 0, 0),
                    0, value.HasValue, metric == "freshOccupancy" ? "Reported" : "Derived", current is not null && !fresh)));
                continue;
            }
            if (defaultMinute && !await db.ZoneMinuteAggregates.AnyAsync(x => x.CameraZoneId == layout.ZoneId && x.MinuteStartUtc == timeline.Period.From
                    && !db.DirtyZoneMinutes.Any(d => d.CameraZoneId == x.CameraZoneId && d.MinuteStartUtc == x.MinuteStartUtc), ct))
            {
                zones.Add(new(layout, null, SmartMetricMath.Quality(new("Derived", 0, 0, 0, 0, 0, 0), 60, false, warning: "minute_unavailable")));
                continue;
            }
            var stats = new ReportBins(timeline, []);
            await reports.ReadZone(timeline, stats, layout.ZoneId, scope.Filter.SessionId, ct, useMinutes: true);
            var flow = SmartMetricMath.Flow(timeline, timeline.Window, stats.Total, visuals.Value.MinimumRateCoveragePercent);
            double? measured = metric switch
            {
                "arrivals" => (double?)flow.ObservedCounts?.Arrivals,
                "averageOccupancy" => flow.AverageOccupancy,
                "estimatedAverageDwell" when double.IsFinite(stats.Total.EstimatedTotalDwell) =>
                    VisitTracker.EstimatedAverage(stats.Total.EstimatedTotalDwell, stats.Total.EstimatedVisits),
                "wrongWay" => (double?)flow.ObservedCounts?.WrongWayEvents,
                _ => null
            };
            var quality = flow.Quality with { Provenance = measured.HasValue ? metric == "estimatedAverageDwell" ? "Estimated" : "Derived" : "InsufficientData" };
            if (metric == "congestion")
            {
                var calculator = new CongestionCalculator(timeline.Window, congestion.Value, analytics.Value);
                await data.Capacity(timeline, scope.Filter.SessionId, layout.ZoneId, calculator, ct);
                measured = calculator.CoveredSeconds > 0 ? calculator.AboveCriticalSeconds : null;
                quality = SmartMetricMath.Quality(new("Derived", calculator.Samples,
                    timeline.Window.Seconds > 0 ? Math.Min(100, calculator.CoveredSeconds / timeline.Window.Seconds * 100) : 0,
                    calculator.CoveredSeconds, 0, 0, calculator.Gaps), timeline.Window.Seconds, measured.HasValue);
            }
            zones.Add(new(layout, measured, quality));
        }
        return new(metric, title, unit, timeline.Period with { SessionId = scope.Filter.SessionId }, floor, zones)
        {
            Quality = VisualizationQueries.Summary(zones.Select(x => x.Quality)),
            Scope = new(floor.SiteId, scope.Filter.CameraId, scope.Filter.ZoneId),
            RefreshSeconds = live ? visuals.Value.DefaultRefreshSeconds : 60
        };
    }

}
