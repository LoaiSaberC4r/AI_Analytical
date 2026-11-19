namespace CameraAnalytics.Application.Abstractions;

public sealed record ComparisonValue(double? Value, MetricQuality Quality);
public sealed record ComparisonRow(MetricScope Scope, string ZoneName, ComparisonValue Current, ComparisonValue? Previous,
    double? AbsoluteDelta, double? PercentageDelta, string? UnavailableReason);
public sealed record ComparisonReport(string MetricKey, string Unit, string Alignment, ReportPeriod CurrentPeriod, ReportPeriod? PreviousPeriod,
    IReadOnlyList<ComparisonRow> Zones);

public sealed record ForecastPoint(DateTime TargetStartUtc, DateTimeOffset TargetLocalStart, int QualifiedSampleCount,
    double? Prediction, double? LowerHistoricalQuantile, double? UpperHistoricalQuantile, string Provenance, string? UnavailableReason);
public sealed record ForecastReport(string ModelKey, string MetricKey, string Unit, MetricScope Scope, string ZoneName,
    string TimeZoneId, DateTime GeneratedAtUtc, DateTime? TrainedFrom, DateTime? TrainedTo, int QualifiedHistoryDays,
    int QualifiedHourCount, double HistoryCoveragePercent, string Provenance, string Methodology, IReadOnlyList<ForecastPoint> Points);
public interface IForecasts
{
    Task<ForecastReport> Occupancy(Guid zoneId, Guid? siteId, CancellationToken ct);
}
