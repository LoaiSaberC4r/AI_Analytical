using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Application.Analytics;

public readonly record struct NumericWindow(long Start, long End)
{
    public double Seconds => (End - Start) / 1000d;
    public bool Contains(long point) => point >= Start && point < End;
}

public sealed class ReportStatistics
{
    public double WeightedSum { get; private set; }
    public double Duration { get; private set; }
    public int? Peak { get; private set; }
    public long? PeakAt { get; private set; }
    public int Samples { get; private set; }
    public double? LatestReportedDwell { get; private set; }
    public long? LatestReportedAt { get; private set; }
    public double? LongestActiveDwell { get; private set; }
    public int EstimatedVisits { get; private set; }
    public int UncertainVisits { get; private set; }
    public double EstimatedTotalDwell { get; private set; }
    public int Resets { get; private set; }
    public int Baselines { get; private set; }
    public int Gaps { get; private set; }
    public TrafficTotals Traffic { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public double? Average => Duration > 0 ? WeightedSum / Duration : null;

    public void Observe(NumericWindow range, long at, long? until, int value, double? reportedDwell = null, double? activeDwell = null)
    {
        var seconds = Math.Max(0, Math.Min(range.End, until ?? at) - Math.Max(range.Start, at)) / 1000d;
        Duration += seconds;
        WeightedSum += value * seconds;
        if (!range.Contains(at))
            return;

        Samples++;
        SetPeak(value, at);
        SetDwell(reportedDwell, at, activeDwell);
    }

    public void CountTraffic(TrafficTotals increment, CounterQuality quality, bool gap)
    {
        Traffic = Add(Traffic, increment);
        if (quality == CounterQuality.Baseline) Baselines++;
        if (quality == CounterQuality.Reset) Resets++;
        if (gap) Gaps++;
    }

    public void CountVisit(VisitStatus status, double dwell)
    {
        if (status == VisitStatus.Uncertain)
            UncertainVisits++;
        else
        {
            EstimatedVisits++;
            EstimatedTotalDwell += dwell;
        }
    }

    public void Merge(double duration, double? average, int? peak, long? peakAt, int samples,
        double? reportedDwell, long? reportAt, double? activeDwell, TrafficTotals traffic, int resets, int baselines,
        int gaps, int visits, int uncertain, double totalDwell)
    {
        WeightedSum += (average ?? 0) * duration;
        Duration += duration;
        Samples += samples;
        if (peak.HasValue && peakAt.HasValue)
            SetPeak(peak.Value, peakAt.Value);
        SetDwell(reportedDwell, reportAt, activeDwell);
        Traffic = Add(Traffic, traffic);
        Resets += resets;
        Baselines += baselines;
        Gaps += gaps;
        EstimatedVisits += visits;
        UncertainVisits += uncertain;
        EstimatedTotalDwell += totalDwell;
    }

    public DataQuality Quality(double totalSeconds, string kind = "Derived") =>
        new(Samples == 0 && Duration == 0 && EstimatedVisits == 0 && UncertainVisits == 0 && Baselines == 0 && Traffic == new TrafficTotals(0, 0, 0, 0, 0, 0)
                ? "InsufficientData" : kind, Samples, Coverage(totalSeconds), Duration, Resets, Baselines, Gaps);

    public double Coverage(double totalSeconds) => totalSeconds <= 0 ? 0 : Math.Clamp(Duration / totalSeconds * 100, 0, 100);

    private void SetPeak(int peak, long at)
    {
        if (Peak is null || peak > Peak || (peak == Peak && at < PeakAt))
        {
            Peak = peak;
            PeakAt = at;
        }
    }

    private void SetDwell(double? reported, long? at, double? active)
    {
        if (reported.HasValue && (LatestReportedAt is null || at >= LatestReportedAt))
        {
            LatestReportedAt = at;
            LatestReportedDwell = reported;
        }
        if (active.HasValue && (!LongestActiveDwell.HasValue || active > LongestActiveDwell))
            LongestActiveDwell = active;
    }

    public static TrafficTotals Add(TrafficTotals x, TrafficTotals y) => new(x.Arrivals + y.Arrivals,
        x.Departures + y.Departures, x.Entries + y.Entries, x.Exits + y.Exits, x.WrongWayIn + y.WrongWayIn, x.WrongWayOut + y.WrongWayOut);
}

public static class ReportRanking
{
    public static RankingReport Rank(ReportResult report, string metric, double minimumCoverage)
    {
        (Func<ZoneResult, double?> Value, string Unit, string Quality, bool RequiresCoverage) definition = metric switch
        {
            "arrivals" => (x => Traffic(x, x.Traffic.Arrivals), "events", "Derived", false),
            "entries" => (x => Traffic(x, x.Traffic.Entries), "events", "Derived", false),
            "departures" => (x => Traffic(x, x.Traffic.Departures), "events", "Derived", false),
            "exits" => (x => Traffic(x, x.Traffic.Exits), "events", "Derived", false),
            "averageOccupancy" => (x => x.AverageOccupancy, "people per zone", "Derived", true),
            "peakOccupancy" => (x => x.PeakOccupancy, "people per zone", "Derived", false),
            "utilization" => (x => x.AverageUtilizationPercent, "percent", "Derived", true),
            "reportedAverageDwell" => (x => x.ReportedAverageDwellSeconds, "seconds", "Reported", false),
            "estimatedAverageDwell" => (x => x.EstimatedHistoricalAverageDwellSeconds, "seconds", "Estimated", false),
            "longestActiveDwell" => (x => x.LongestActiveDwellSeconds, "seconds", "Derived", false),
            "wrongWayEvents" => (x => Traffic(x, x.Traffic.WrongWayEvents), "events", "Derived", false),
            _ => throw Common.ApiError.Invalid("The ranking metric is not supported.")
        };
        var ordered = report.Zones.Select(x => (Zone: x, Value: definition.Value(x)))
            .Where(x => x.Value.HasValue && (!definition.RequiresCoverage || x.Zone.DataQuality.CoveragePercent >= minimumCoverage))
            .OrderByDescending(x => x.Value).ThenBy(x => x.Zone.ZoneName, StringComparer.Ordinal).ThenBy(x => x.Zone.ZoneId);
        return new(report.Period, metric, definition.Unit, ordered.Select((x, i) =>
            new RankingRow(i + 1, x.Zone.ZoneId, x.Zone.ZoneName, x.Value!.Value, x.Zone.DataQuality with { Kind = definition.Quality })).ToArray());
    }

    public static PeakHoursReport PeakHours(ReportResult report, double minimumCoverage) =>
        new(report.Period, report.Zones.Select(zone =>
        {
            var covered = zone.Series.Where(x => x.DataQuality.CoveragePercent >= minimumCoverage).ToArray();
            return new PeakHourRow(zone.ZoneId, zone.ZoneName,
                covered.Where(x => x.AverageOccupancy.HasValue).OrderByDescending(x => x.AverageOccupancy).FirstOrDefault(),
                Best(x => x.Traffic.Arrivals), Best(x => x.Traffic.Entries), Best(x => x.Traffic.Departures), Best(x => x.Traffic.WrongWayEvents));

            ZonePeriod? Best(Func<ZonePeriod, decimal> metric) => covered.Where(x => metric(x) > 0)
                .OrderByDescending(metric).FirstOrDefault();
        }).ToArray());

    private static double? Traffic(ZoneResult zone, decimal value) => zone.DataQuality.SampleCount > zone.DataQuality.BaselineCount
        || value > 0 ? (double)value : null;
}
