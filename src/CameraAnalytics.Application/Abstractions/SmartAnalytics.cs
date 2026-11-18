namespace CameraAnalytics.Application.Abstractions;

public sealed record MetricScope(Guid SiteId, Guid? CameraId = null, Guid? ZoneId = null);

public sealed record MetricQuality(string Provenance, int SampleCount, double CoveredSeconds, double CoveragePercent,
    int GapCount, int ResetCount, int BaselineCount, bool IsPartial, bool IsStale, IReadOnlyList<string> WarningCodes);

public sealed record FlowRates(double Arrivals, double Departures, double Entries, double Exits, double WrongWayIn, double WrongWayOut);

public sealed record FlowBucket(PeriodPoint Period, TrafficTotals? ObservedCounts, FlowRates? EventsPerMinute,
    decimal? TrafficBalance, double? AverageOccupancy, int? PeakOccupancy, DateTime? PeakAtUtc, long? PeakAtSourceTimeMs,
    MetricQuality Quality);

public sealed record ZoneFlow(MetricScope Scope, string ZoneName, FlowBucket Total, IReadOnlyList<FlowBucket> Buckets,
    ReportedAnalyticsMetadata? LatestReportedMetadata);
public sealed record FlowReport(ReportPeriod Period, IReadOnlyList<ZoneFlow> Zones, MetricScope? Scope = null);

public sealed record CongestionEpisodeData(PeriodPoint Period, string Severity, double AboveWarningSeconds,
    double AboveCriticalSeconds, string Status, string? ResolutionReason);

public sealed record CongestionZone(MetricScope Scope, string ZoneName, double? AverageUtilizationPercent,
    double? PeakUtilizationPercent, double? AboveWarningSeconds, double? AboveCriticalSeconds,
    MetricQuality Quality, IReadOnlyList<CongestionEpisodeData> Episodes, double? LastObservedWarningThreshold = null,
    double? LastObservedCriticalThreshold = null);

public sealed record CongestionReport(ReportPeriod Period, IReadOnlyList<CongestionZone> Zones, MetricScope? Scope = null);
public sealed record CongestionHistoryItem(Guid Id, MetricScope Scope, Guid SessionId, long? VideoLoop, PeriodPoint Period,
    string Severity, string Status, string ResolutionReason, string RuleVersion, int Capacity, double WarningThresholdPercent,
    double CriticalThresholdPercent, int MinimumObservedSeconds, double AboveWarningSeconds, double AboveCriticalSeconds, MetricQuality Quality);
public sealed record CongestionHistory(ReportPeriod Period, Common.Page<CongestionHistoryItem> Episodes);

public sealed record DwellBin(double LowerSeconds, double? UpperSeconds, int Count);
public sealed record DwellZoneDistribution(MetricScope Scope, string ZoneName, int? ActiveCount, int? LongStayActiveCount,
    double? LongestActiveDwellSeconds, int? EstimatedCompletedCount, double? EstimatedCompletedTotalDwellSeconds,
    double? EstimatedCompletedAverageDwellSeconds, int? UncertainCount, IReadOnlyList<DwellBin>? ActiveBins,
    IReadOnlyList<DwellBin>? EstimatedCompletedBins, MetricQuality ActiveQuality, MetricQuality CompletedQuality,
    DateTime? ActiveObservedAtUtc = null);
public sealed record DwellDistributionReport(ReportPeriod Period, double LongStayThresholdSeconds, DateTime EvaluatedAtUtc,
    IReadOnlyList<DwellZoneDistribution> Zones, MetricScope? Scope = null);

public sealed record CameraQualityBucket(PeriodPoint Period, double? AverageStaffCount, int? PeakStaffCount,
    double? AverageFps, double? MinimumFps, double? MaximumFps, decimal? MissingSequences, double? AverageSourceToReceiptMs,
    int LatencySampleCount, MetricQuality Quality);
public sealed record CameraDiagnostics(MetricScope Scope, string CameraName, string Freshness, DateTime? LastReceivedAtUtc,
    double? LatestProcessingFps, IReadOnlyList<CameraQualityBucket> Buckets, ReportedAnalyticsMetadata? ReportedMetadata);
public sealed record RejectionCount(string Reason, int Count);
public sealed record QualityReport(ReportPeriod Period, IReadOnlyList<CameraDiagnostics> Cameras, IReadOnlyList<RejectionCount>? Rejections,
    MetricScope? Scope = null);

public interface ISmartQueries
{
    Task<FlowReport> Flow(ReportFilter filter, CancellationToken ct);
    Task<CongestionReport> Congestion(ReportFilter filter, CancellationToken ct);
    Task<CongestionHistory> CongestionHistory(ReportFilter filter, Common.ListFilter page, CancellationToken ct);
    Task<DwellDistributionReport> Dwell(ReportFilter filter, CancellationToken ct);
    Task<QualityReport> Quality(ReportFilter filter, CancellationToken ct);
}
