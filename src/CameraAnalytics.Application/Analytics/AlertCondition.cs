namespace CameraAnalytics.Application.Analytics;

public readonly record struct ConditionSample(long At, long? End, double? Value, bool Discontinuous);
public sealed record ConditionMeasurement(bool? Satisfied, double? LatestValue, double CoveredSeconds, double SustainedSeconds,
    long? StartedAt, int SampleCount, int Gaps);

public static class AlertCondition
{
    public static ConditionMeasurement Measure(IEnumerable<ConditionSample> samples, NumericWindow window,
        Func<double, bool> test, int maximumGapSeconds)
    {
        double covered = 0, sustained = 0;
        int count = 0, gaps = 0;
        long? start = null;
        bool? previous = null;
        double? latest = null;
        var cursor = window.Start;
        foreach (var sample in samples)
        {
            var from = Math.Max(cursor, sample.At);
            var end = Math.Min(window.End, sample.End ?? sample.At);
            var next = sample.Value.HasValue ? test(sample.Value.Value) : (bool?)null;
            if (sample.Discontinuous || (count > 0 && from - cursor > maximumGapSeconds * 1000L))
            {
                gaps++;
                sustained = 0;
                start = null;
            }
            if (next != previous || next is null)
            {
                sustained = 0;
                start = null;
            }
            if (end > from && next.HasValue)
            {
                var seconds = (end - from) / 1000d;
                covered += seconds;
                sustained += seconds;
                start ??= from;
                cursor = end;
            }
            if (window.Contains(sample.At) && sample.Value.HasValue) count++;
            previous = next;
            latest = sample.Value;
        }
        if (window.End - cursor > maximumGapSeconds * 1000L)
            return new(null, latest, covered, 0, null, count, gaps + 1);
        return new(previous, latest, covered, sustained, start, count, gaps);
    }
}
