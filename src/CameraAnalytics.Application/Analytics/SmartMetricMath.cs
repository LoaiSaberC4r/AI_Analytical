using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Application.Analytics;

public static class SmartMetricMath
{
    public static FlowBucket Flow(ReportTimeline timeline, NumericWindow period, ReportStatistics stats, double minimumCoverage)
    {
        var counts = stats.Samples > stats.Baselines || stats.Traffic != new TrafficTotals(0, 0, 0, 0, 0, 0)
            ? stats.Traffic : null;
        FlowRates? rates = null;
        if (counts is not null && stats.Coverage(period.Seconds) >= minimumCoverage && stats.Gaps == 0
            && stats.Resets == 0 && stats.Baselines == 0 && period.Seconds > 0)
        {
            var divisor = period.Seconds / 60;
            rates = new((double)counts.Arrivals / divisor, (double)counts.Departures / divisor,
                (double)counts.Entries / divisor, (double)counts.Exits / divisor,
                (double)counts.WrongWayIn / divisor, (double)counts.WrongWayOut / divisor);
        }
        var quality = Quality(stats.Quality(period.Seconds), period.Seconds,
            counts is not null || stats.Peak.HasValue || stats.Average.HasValue, warning: rates is null ? "rate_unavailable" : null);
        return new(timeline.Describe(period), counts, rates, counts?.Entries - counts?.Exits, stats.Average,
            stats.Peak, timeline.Time(stats.PeakAt), timeline.Position(stats.PeakAt), quality);
    }

    public static MetricQuality Quality(DataQuality quality, double windowSeconds, bool available,
        string provenance = "Derived", bool stale = false, string? warning = null)
    {
        var warnings = new List<string>();
        if (!available) warnings.Add("insufficient_data");
        if (windowSeconds > 0 && quality.CoveragePercent < 100) warnings.Add("partial_coverage");
        if (quality.GapCount > 0) warnings.Add("observation_gap");
        if (quality.CounterResetCount > 0) warnings.Add("counter_reset");
        if (quality.BaselineCount > 0) warnings.Add("counter_baseline");
        if (stale) warnings.Add("stale");
        if (warning is not null) warnings.Add(warning);
        return new(available ? provenance : "InsufficientData", quality.SampleCount, quality.ObservationDurationSeconds,
            quality.CoveragePercent, quality.GapCount, quality.CounterResetCount, quality.BaselineCount,
            windowSeconds > 0 && quality.CoveragePercent < 100, stale, warnings);
    }

    public static IReadOnlyList<DwellBin> Histogram(IEnumerable<double> values, double[] bounds)
    {
        var histogram = new DwellHistogram(bounds);
        foreach (var value in values) histogram.Add(value);
        return histogram.Bins();
    }
}

public sealed class DwellHistogram(double[] bounds)
{
    private readonly int[] counts = new int[bounds.Length + 1];
    public double Sum { get; private set; }
    public int Count { get; private set; }

    public void Add(double seconds)
    {
        var index = Array.BinarySearch(bounds, seconds);
        counts[index >= 0 ? index + 1 : ~index]++;
        Sum += seconds;
        Count++;
    }

    public IReadOnlyList<DwellBin> Bins() => counts.Select((count, i) => new DwellBin(
        i == 0 ? 0 : bounds[i - 1], i == bounds.Length ? null : bounds[i], count)).ToArray();
}

public sealed class CameraSampleStatistics
{
    public ReportStatistics Staff { get; } = new();
    public double? MinimumFps { get; private set; }
    public double? MaximumFps { get; private set; }
    public double FpsSum { get; private set; }
    public decimal? MissingSequences { get; private set; }
    public int KnownSequenceSamples { get; private set; }
    public int GapCount { get; private set; }
    public int LatencySampleCount { get; private set; }
    private double latencySum;

    public void Observe(NumericWindow period, long at, long? end, int staff, double fps, long? missing, double? latency = null)
    {
        Staff.Observe(period, at, end, staff);
        if (!period.Contains(at))
            return;
        MinimumFps = MinimumFps.HasValue ? Math.Min(fps, MinimumFps.Value) : fps;
        MaximumFps = MaximumFps.HasValue ? Math.Max(fps, MaximumFps.Value) : fps;
        FpsSum += fps;
        if (missing > 0) GapCount++;
        if (missing.HasValue)
        {
            KnownSequenceSamples++;
            MissingSequences = (MissingSequences ?? 0) + missing;
        }
        if (latency is >= 0)
        {
            LatencySampleCount++;
            latencySum += latency.Value;
        }
    }

    public double? AverageFps => Staff.Samples == 0 || !double.IsFinite(FpsSum) ? null : FpsSum / Staff.Samples;
    public double? AverageLatency => LatencySampleCount == 0 ? null : latencySum / LatencySampleCount;
}
