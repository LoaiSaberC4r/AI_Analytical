using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;

namespace CameraAnalytics.Application.Analytics;

public sealed record CongestionProgress(Guid SessionId, long? VideoLoop, DateTime StartedAtUtc, long? StartSourceTimeMs,
    DateTime LastObservedAtUtc, long? LastObservedSourceTimeMs, int Capacity, double WarningThresholdPercent,
    double CriticalThresholdPercent, int MinimumObservedSeconds, int RecoveryRequiredSeconds, int SampleCount = 0,
    double AboveWarningSeconds = 0, double AboveCriticalSeconds = 0, double RecoverySeconds = 0)
{
    public static CongestionProgress? Advance(CongestionProgress? progress, ZoneSnapshot prior, ZoneSnapshot next,
        bool video, bool contiguous, CongestionOptions options, Action<CongestionEpisode> save)
    {
        var utilization = ObservationRules.Utilization(prior.Occupancy, prior.CapacityAtObservation);
        var warning = prior.WarningThresholdAtObservation;
        var critical = prior.CriticalThresholdAtObservation;
        if (!utilization.HasValue || !warning.HasValue || !critical.HasValue)
        {
            progress?.Close(prior.CameraZoneId, "Uncertain", "capacity_unknown", save);
            return null;
        }
        if (progress is not null && (progress.SessionId != prior.AnalyticsSessionId || progress.VideoLoop != prior.VideoLoop))
        {
            progress.Close(prior.CameraZoneId, "Resolved", "source_changed", save);
            progress = null;
        }
        var endPosition = video ? prior.ObservationEndSourceMs : null;
        var endUtc = video ? next.SourceTimestampUtc : prior.ObservationEndUtc ?? prior.SourceTimestampUtc;
        var seconds = video && endPosition.HasValue && prior.SourceTimeMs.HasValue
            ? (endPosition.Value - prior.SourceTimeMs.Value) / 1000d
            : video ? 0 : (endUtc - prior.SourceTimestampUtc).TotalSeconds;
        if (seconds > 0 && utilization.Value >= warning.Value)
        {
            progress ??= new(prior.AnalyticsSessionId, prior.VideoLoop, prior.SourceTimestampUtc, video ? prior.SourceTimeMs : null,
                endUtc, endPosition, prior.CapacityAtObservation!.Value, warning.Value, critical.Value,
                options.MinObservedDurationSeconds, options.RecoveryObservedSeconds);
            progress = progress with
            {
                LastObservedAtUtc = endUtc, LastObservedSourceTimeMs = endPosition,
                SampleCount = progress.SampleCount + 1, AboveWarningSeconds = progress.AboveWarningSeconds + seconds,
                AboveCriticalSeconds = progress.AboveCriticalSeconds + (utilization.Value >= critical.Value ? seconds : 0), RecoverySeconds = 0
            };
        }
        else if (progress is not null && seconds > 0)
        {
            progress = progress with { RecoverySeconds = progress.RecoverySeconds + seconds };
            if (progress.RecoverySeconds >= progress.RecoveryRequiredSeconds)
            {
                progress.Close(prior.CameraZoneId, "Resolved", "observed_recovery", save);
                return null;
            }
        }
        if (progress is null)
            return null;
        var gap = video && endPosition.HasValue && next.SourceTimeMs.HasValue
            ? (next.SourceTimeMs.Value - endPosition.Value) / 1000d : (next.SourceTimestampUtc - endUtc).TotalSeconds;
        if (!contiguous || gap > options.MaxObservationGapSeconds)
        {
            progress.Close(prior.CameraZoneId, "Uncertain", "observation_gap", save);
            return null;
        }
        if (prior.CapacityAtObservation != next.CapacityAtObservation || warning != next.WarningThresholdAtObservation
            || critical != next.CriticalThresholdAtObservation)
        {
            progress.Close(prior.CameraZoneId, "Resolved", "configuration_changed", save);
            return null;
        }
        return progress;
    }

    public void Close(Guid zoneId, string status, string reason, Action<CongestionEpisode> save)
    {
        if (AboveWarningSeconds < MinimumObservedSeconds) return;
        save(new CongestionEpisode
        {
            CameraZoneId = zoneId, AnalyticsSessionId = SessionId, VideoLoop = VideoLoop, StartedAtUtc = StartedAtUtc,
            EndedAtUtc = LastObservedAtUtc, StartSourceTimeMs = StartSourceTimeMs, EndSourceTimeMs = LastObservedSourceTimeMs,
            SampleCount = SampleCount, ObservedAboveWarningSeconds = AboveWarningSeconds, ObservedAboveCriticalSeconds = AboveCriticalSeconds,
            WarningThresholdPercent = WarningThresholdPercent, CriticalThresholdPercent = CriticalThresholdPercent, Capacity = Capacity,
            MinimumObservedSeconds = MinimumObservedSeconds, Status = status, ResolutionReason = reason
        });
    }
}
