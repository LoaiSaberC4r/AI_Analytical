namespace CameraAnalytics.Domain.Entities;

public sealed class CongestionEpisode
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CameraZoneId { get; set; }
    public CameraZone Zone { get; set; } = null!;
    public Guid AnalyticsSessionId { get; set; }
    public AnalyticsSession Session { get; set; } = null!;
    public long? VideoLoop { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public long? StartSourceTimeMs { get; set; }
    public long? EndSourceTimeMs { get; set; }
    public int SampleCount { get; set; }
    public double ObservedAboveWarningSeconds { get; set; }
    public double ObservedAboveCriticalSeconds { get; set; }
    public double WarningThresholdPercent { get; set; }
    public double CriticalThresholdPercent { get; set; }
    public int Capacity { get; set; }
    public int MinimumObservedSeconds { get; set; }
    public string Status { get; set; } = "";
    public string ResolutionReason { get; set; } = "";
}
