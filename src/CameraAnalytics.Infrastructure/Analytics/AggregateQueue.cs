using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Persistence;

namespace CameraAnalytics.Infrastructure.Analytics;

internal static class AggregateQueue
{
    public static void Fill(ZoneAggregateValues row, ReportStatistics stats, int seconds, DateTime now)
    {
        row.ObservationDurationSeconds = stats.Duration;
        row.AverageOccupancy = stats.Average;
        row.PeakOccupancy = stats.Peak;
        row.PeakAtUtc = stats.PeakAt.HasValue ? ReportTimeline.Utc(stats.PeakAt.Value) : null;
        row.ArrivalsDelta = stats.Traffic.Arrivals;
        row.DeparturesDelta = stats.Traffic.Departures;
        row.EntryDelta = stats.Traffic.Entries;
        row.ExitDelta = stats.Traffic.Exits;
        row.WrongWayInDelta = stats.Traffic.WrongWayIn;
        row.WrongWayOutDelta = stats.Traffic.WrongWayOut;
        row.LatestReportedDwellSeconds = stats.LatestReportedDwell;
        row.LatestReportedAtUtc = stats.LatestReportedAt.HasValue ? ReportTimeline.Utc(stats.LatestReportedAt.Value) : null;
        row.LongestActiveDwellSeconds = stats.LongestActiveDwell;
        row.CounterBaselineCount = stats.Baselines;
        row.CounterResetCount = stats.Resets;
        row.GapCount = stats.Gaps;
        row.SampleCount = stats.Samples;
        row.UncertainVisits = stats.UncertainVisits;
        row.EstimatedCompletedVisits = stats.EstimatedVisits;
        row.EstimatedTotalDwellSeconds = double.IsFinite(stats.EstimatedTotalDwell) ? stats.EstimatedTotalDwell : null;
        row.CoveragePercent = stats.Coverage(seconds);
        row.DataQuality = stats.Quality(seconds).Kind;
        row.UpdatedAtUtc = now;
    }

    public static DateTime FloorHour(DateTime time) => new(time.Ticks - time.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);

    public static async Task Mark(AnalyticsDb db, Guid zoneId, string sourceType, DateTime from, DateTime to, DateTime now,
        bool minuteEnabled, CancellationToken ct)
    {
        if (sourceType != "live")
            return;
        if (minuteEnabled)
            await MinuteQueue.MarkZone(db, zoneId, from, to, now, ct);

        var end = FloorHour(to);
        for (var hour = FloorHour(from); hour <= end; hour = hour.AddHours(1))
        {
            var job = await db.DirtyZoneHours.FindAsync([zoneId, sourceType, hour], ct);
            if (job is null)
                db.DirtyZoneHours.Add(new() { CameraZoneId = zoneId, SourceType = sourceType, HourStartUtc = hour, DirtyAtUtc = now });
        }
    }
}
