namespace CameraAnalytics.Domain.Entities;

public sealed class AlertRule : CatalogRecord
{
    public Guid SiteId { get; set; }
    public Guid CameraId { get; set; }
    public Guid? CameraZoneId { get; set; }
    public string Kind { get; set; } = "";
    public string Severity { get; set; } = "";
    public double Threshold { get; set; }
    public int MinimumObservedSeconds { get; set; }
    public int RecoverySeconds { get; set; }
    public int WindowSeconds { get; set; }
    public int CooldownSeconds { get; set; }
    public double MinimumCoveragePercent { get; set; }
    public bool AllowVideo { get; set; }
    public DateTime? LastResolvedAtUtc { get; set; }
}

public sealed class AnalyticsAlert
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AlertRuleId { get; set; }
    public Guid SiteId { get; set; }
    public Guid CameraId { get; set; }
    public Guid? CameraZoneId { get; set; }
    public Guid? SessionId { get; set; }
    public long? VideoLoop { get; set; }
    public string Kind { get; set; } = "";
    public string Severity { get; set; } = "";
    public DateTime OpenedAtUtc { get; set; }
    public DateTime LastObservedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionReason { get; set; }
    public long StateVersion { get; set; }
    public string EvidenceJson { get; set; } = "";
}
