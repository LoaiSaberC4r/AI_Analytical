using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Infrastructure.Analytics;

internal sealed class ReportBins(ReportTimeline timeline, NumericWindow[] periods)
{
    private readonly ReportStatistics[] bins = periods.Select(_ => new ReportStatistics()).ToArray();
    public ReportStatistics Total { get; } = new();

    public IEnumerable<(NumericWindow Period, ReportStatistics Stats)> Series =>
        periods.Zip(bins).Select(x => (x.First, x.Second));

    public bool CanUseHour(NumericWindow hour)
    {
        if (hour.Start < timeline.Window.Start || hour.End > timeline.Window.End)
            return false;
        var index = Find(hour.Start);
        return periods.Length == 0 || (index < periods.Length && hour.End <= periods[index].End);
    }

    public void Observe(NumericWindow clip, ObservationRow row)
    {
        Total.Observe(clip, row.At, row.Until, row.Value, row.ReportedDwell, row.ActiveDwell);
        for (var i = Find(Math.Max(clip.Start, row.At)); i < periods.Length && periods[i].Start < clip.End; i++)
        {
            if (periods[i].Start > Math.Max(row.At, row.Until ?? row.At))
                break;
            bins[i].Observe(new(Math.Max(clip.Start, periods[i].Start), Math.Min(clip.End, periods[i].End)),
                row.At, row.Until, row.Value, row.ReportedDwell, row.ActiveDwell);
        }
    }

    public void Traffic(long at, ZoneCounterDelta delta)
    {
        var traffic = new TrafficTotals(delta.ZoneArrivalsDelta, delta.ZoneDeparturesDelta, delta.EntryDelta,
            delta.ExitDelta, delta.WrongWayInDelta, delta.WrongWayOutDelta);
        Total.CountTraffic(traffic, delta.CounterQuality, delta.HasObservationGap);
        var i = Find(at);
        if (i < periods.Length)
            bins[i].CountTraffic(traffic, delta.CounterQuality, delta.HasObservationGap);
    }

    public void Visit(long at, TrackedVisit visit)
    {
        Total.CountVisit(visit.Status, visit.LastDwellSeconds);
        var i = Find(at);
        if (i < periods.Length)
            bins[i].CountVisit(visit.Status, visit.LastDwellSeconds);
    }

    public void Rollup(NumericWindow hour, ZoneAggregateValues row)
    {
        Merge(Total);
        var index = Find(hour.Start);
        if (index < periods.Length)
            Merge(bins[index]);

        void Merge(ReportStatistics target) => target.Merge(row.ObservationDurationSeconds, row.AverageOccupancy,
            row.PeakOccupancy, ReportTimeline.Ms(row.PeakAtUtc), row.SampleCount,
            row.LatestReportedDwellSeconds, ReportTimeline.Ms(row.LatestReportedAtUtc), row.LongestActiveDwellSeconds,
            new(row.ArrivalsDelta, row.DeparturesDelta, row.EntryDelta, row.ExitDelta, row.WrongWayInDelta, row.WrongWayOutDelta),
            row.CounterResetCount, row.CounterBaselineCount, row.GapCount, row.EstimatedCompletedVisits, row.UncertainVisits,
            row.EstimatedTotalDwellSeconds ?? double.PositiveInfinity);
    }

    private int Find(long point)
    {
        var left = 0;
        var right = periods.Length;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (periods[middle].End <= point) left = middle + 1;
            else right = middle;
        }
        return left;
    }
}

internal sealed record ObservationRow(long At, long? Until, int Value, double? ReportedDwell = null, double? ActiveDwell = null);
