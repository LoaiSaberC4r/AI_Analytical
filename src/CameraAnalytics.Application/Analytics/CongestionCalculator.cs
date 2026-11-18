using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Analytics;

public readonly record struct CapacitySample(Guid SessionId, long? VideoLoop, long At, long? Until, int Occupancy,
    int? Capacity, double? Warning, double? Critical, bool Ended, bool Discontinuous = false);

public sealed record MeasuredEpisode(NumericWindow Window, bool Critical, double WarningSeconds, double CriticalSeconds,
    string Status, string? ResolutionReason);

public sealed class CongestionCalculator(NumericWindow window, CongestionOptions options, AnalyticsOptions defaults)
{
    private CapacitySample? previous;
    private long cursor = window.Start;
    private long? onset;
    private long? recoveryStart;
    private double above;
    private double critical;
    private double recovering;
    private double weightedUtilization;
    private readonly List<MeasuredEpisode> episodes = [];

    public int Samples { get; private set; }
    public int Gaps { get; private set; }
    public double CoveredSeconds { get; private set; }
    public double AboveWarningSeconds { get; private set; }
    public double AboveCriticalSeconds { get; private set; }
    public double? PeakUtilization { get; private set; }
    public double? LastObservedWarningThreshold { get; private set; }
    public double? LastObservedCriticalThreshold { get; private set; }
    public double? AverageUtilization => CoveredSeconds > 0 ? weightedUtilization / CoveredSeconds : null;

    public void Observe(CapacitySample sample)
    {
        var start = Math.Max(window.Start, sample.At);
        var end = Math.Min(window.End, sample.Until ?? sample.At);
        if (previous is { } prior)
        {
            if (prior.SessionId != sample.SessionId || prior.VideoLoop != sample.VideoLoop)
                Close(cursor, "Resolved", prior.SessionId == sample.SessionId ? "video_loop_changed" : "session_changed");
            else if (sample.Discontinuous || start - cursor > options.MaxObservationGapSeconds * 1000L)
            {
                Gaps++;
                Close(cursor, "Uncertain", "observation_gap");
            }
            else if (prior.Capacity != sample.Capacity || prior.Warning != sample.Warning || prior.Critical != sample.Critical)
                Close(cursor, "Resolved", "configuration_changed");
        }
        previous = sample;
        var utilization = ObservationRules.Utilization(sample.Occupancy, sample.Capacity);
        if (sample.Ended)
            Close(Math.Max(cursor, start), "Resolved", "session_ended");
        if (!utilization.HasValue)
        {
            Close(cursor, "Uncertain", "capacity_unknown");
            return;
        }
        if (window.Contains(sample.At))
        {
            Samples++;
            PeakUtilization = PeakUtilization.HasValue ? Math.Max(PeakUtilization.Value, utilization.Value) : utilization;
            LastObservedWarningThreshold = sample.Warning ?? defaults.DefaultWarningThresholdPercent;
            LastObservedCriticalThreshold = sample.Critical ?? defaults.DefaultCriticalThresholdPercent;
        }
        start = Math.Max(start, cursor);
        if (end <= start || sample.Ended)
            return;
        cursor = end;
        var seconds = new NumericWindow(start, end).Seconds;
        CoveredSeconds += seconds;
        weightedUtilization += utilization.Value * seconds;
        if (utilization >= (sample.Warning ?? defaults.DefaultWarningThresholdPercent))
        {
            onset ??= start;
            recovering = 0;
            recoveryStart = null;
            above += seconds;
            AboveWarningSeconds += seconds;
            if (utilization >= (sample.Critical ?? defaults.DefaultCriticalThresholdPercent))
            {
                critical += seconds;
                AboveCriticalSeconds += seconds;
            }
        }
        else if (onset.HasValue)
        {
            recoveryStart ??= start;
            recovering += seconds;
            if (recovering >= options.RecoveryObservedSeconds)
                Close(recoveryStart.Value, "Resolved", "observed_recovery");
        }
    }

    public IReadOnlyList<MeasuredEpisode> Complete(bool noRecentData = false)
    {
        if (noRecentData || window.End - cursor > options.MaxObservationGapSeconds * 1000L)
            Close(cursor, "Uncertain", noRecentData ? "no_recent_data" : "observation_gap");
        else
            Close(cursor, "Observed", null);
        return episodes;
    }

    private void Close(long end, string status, string? reason)
    {
        if (onset.HasValue && above >= options.MinObservedDurationSeconds)
            episodes.Add(new(new(onset.Value, end), critical > 0, above, critical, status, reason));
        onset = recoveryStart = null;
        above = critical = recovering = 0;
    }
}
