using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Abstractions;

public sealed record SaveAlertRule(Guid SiteId, Guid CameraId, Guid? ZoneId, string Name, string Kind, string Severity, double Threshold,
    int MinimumObservedSeconds = 30, int RecoverySeconds = 15, int WindowSeconds = 60, int? CooldownSeconds = null,
    double? MinimumCoveragePercent = null, bool AllowVideo = false, string? RowVersion = null);
public sealed record AlertRuleData(Guid Id, MetricScope Scope, string Name, string Kind, string Severity, double Threshold,
    int MinimumObservedSeconds, int RecoverySeconds, int WindowSeconds, int CooldownSeconds, double MinimumCoveragePercent,
    bool AllowVideo, bool IsActive, byte[] RowVersion);
public sealed record AlertEvidence(ReportPeriod Period, PeriodPoint ConditionWindow, double Value, double Threshold,
    string Unit, double ObservedSeconds, MetricQuality Quality, int? ActiveVisitCount = null);
public sealed record AlertData(Guid Id, Guid RuleId, MetricScope Scope, Guid? SessionId, long? VideoLoop, string Kind, string Severity,
    string Status, DateTime OpenedAtUtc, DateTime LastObservedAtUtc, DateTime? AcknowledgedAtUtc, string? AcknowledgedBy,
    DateTime? ResolvedAtUtc, string? ResolvedBy, string? ResolutionReason, long StateVersion, AlertEvidence Evidence);

public interface IAlerts
{
    Task<Page<AlertRuleData>> Rules(Guid siteId, ListFilter filter, CancellationToken ct);
    Task<AlertRuleData> Rule(Guid id, CancellationToken ct);
    Task<AlertRuleData> SaveRule(Guid? id, SaveAlertRule input, CancellationToken ct);
    Task<AlertRuleData> SetRuleStatus(Guid id, SetStatus input, CancellationToken ct);
    Task<Page<AlertData>> List(Guid siteId, Guid? cameraId, string? status, ListFilter filter, CancellationToken ct);
    Task<AlertData> Get(Guid id, CancellationToken ct);
    Task<AlertData> Acknowledge(Guid id, string? actor, CancellationToken ct);
    Task<AlertData> Resolve(Guid id, string? actor, CancellationToken ct);
}

public interface IAlertNotifications
{
    Task Changed(AlertData alert, CancellationToken ct);
}
