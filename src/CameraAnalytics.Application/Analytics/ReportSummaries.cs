using CameraAnalytics.Application.Abstractions;

namespace CameraAnalytics.Application.Analytics;

public static class ReportSummaries
{
    public static OverviewReport Overview(ReportResult report)
    {
        var quality = report.Zones.Select(x => x.DataQuality).ToArray();
        var duration = quality.Sum(x => x.ObservationDurationSeconds);
        var weightedAverage = duration == 0 ? (double?)null
            : report.Zones.Sum(x => (x.AverageOccupancy ?? 0) * x.DataQuality.ObservationDurationSeconds) / duration;
        return new(report.Period, Best("arrivals"), Best("entries"), Best("departures"), Best("exits"),
            Best("averageOccupancy"), Best("peakOccupancy"), Best("longestActiveDwell"), Best("estimatedAverageDwell"),
            Best("wrongWayEvents"), weightedAverage, Traffic(report),
            new(quality.Any(x => x.Kind == "Derived") ? "Derived" : "InsufficientData",
                quality.Sum(x => x.SampleCount), quality.Length == 0 ? 0 : quality.Average(x => x.CoveragePercent),
                duration, quality.Sum(x => x.CounterResetCount), quality.Sum(x => x.BaselineCount), quality.Sum(x => x.GapCount)));

        RankingRow? Best(string metric) => ReportRanking.Rank(report, metric, 0).Items.FirstOrDefault();
    }

    public static TrafficTotals Traffic(ReportResult report) => report.Zones.Select(x => x.Traffic)
        .Aggregate(new TrafficTotals(0, 0, 0, 0, 0, 0), ReportStatistics.Add);
}
