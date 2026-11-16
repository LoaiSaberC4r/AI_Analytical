using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Abstractions;

public sealed record ReportFilter(Guid? SiteId = null, Guid? CameraId = null, Guid? ZoneId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? Interval = null, string? SourceType = null,
    Guid? SessionId = null, long? FromSourceTimeMs = null, long? ToSourceTimeMs = null);

public sealed record ReportPeriod(string SourceType, string TimeBasis, string Interval, string TimeZoneId,
    DateTime? From, DateTime? To, long? FromSourceTimeMs, long? ToSourceTimeMs);
public sealed record PeriodPoint(DateTime? StartUtc, DateTime? EndUtc, DateTimeOffset? LocalStart,
    long? StartSourceTimeMs, long? EndSourceTimeMs);
public sealed record DataQuality(string Kind, int SampleCount, double CoveragePercent, double ObservationDurationSeconds,
    int CounterResetCount, int BaselineCount, int GapCount);
public sealed record TrafficTotals(decimal Arrivals, decimal Departures, decimal Entries, decimal Exits, decimal WrongWayIn, decimal WrongWayOut)
{
    public decimal WrongWayEvents => WrongWayIn + WrongWayOut;
}
public sealed record ZonePeriod(PeriodPoint Period, double? AverageOccupancy, int? PeakOccupancy, DateTime? PeakAtUtc,
    long? PeakAtSourceTimeMs, TrafficTotals Traffic, DataQuality DataQuality);
public sealed record ZoneResult(Guid ZoneId, Guid CameraId, string ZoneName, int? Capacity, double? AverageOccupancy,
    int? PeakOccupancy, DateTime? PeakAtUtc, long? PeakAtSourceTimeMs, double? PeakUtilizationPercent,
    double? AverageUtilizationPercent, double? ReportedAverageDwellSeconds, double? LongestActiveDwellSeconds,
    int EstimatedCompletedVisits, double? EstimatedTotalDwellSeconds, double? EstimatedHistoricalAverageDwellSeconds,
    int UncertainVisits, TrafficTotals Traffic, DataQuality DataQuality, IReadOnlyList<ZonePeriod> Series);
public sealed record ReportResult(ReportPeriod Period, IReadOnlyList<ZoneResult> Zones);

public sealed record RankingRow(int Rank, Guid ZoneId, string ZoneName, double Value, DataQuality DataQuality);
public sealed record RankingReport(ReportPeriod Period, string Metric, string Unit, IReadOnlyList<RankingRow> Items);
public sealed record PeakHourRow(Guid ZoneId, string ZoneName, ZonePeriod? Occupancy, ZonePeriod? Arrivals,
    ZonePeriod? Entries, ZonePeriod? Departures, ZonePeriod? WrongWayEvents);
public sealed record PeakHoursReport(ReportPeriod Period, IReadOnlyList<PeakHourRow> Zones);
public sealed record OverviewReport(ReportPeriod Period, RankingRow? MostVisitedZone, RankingRow? TopEntryZone,
    RankingRow? MostDepartedZone, RankingRow? TopExitZone, RankingRow? MostCrowdedZone, RankingRow? PeakOccupancy,
    RankingRow? LongestActiveStay, RankingRow? EstimatedHistoricalDwell, RankingRow? HighestWrongWayZone,
    double? ObservedZoneAverageOccupancy, TrafficTotals Traffic, DataQuality DataQuality);
public sealed record TrafficZone(Guid ZoneId, string ZoneName, TrafficTotals Traffic, DataQuality DataQuality,
    IReadOnlyList<ZonePeriod> Series);
public sealed record TrafficReport(ReportPeriod Period, TrafficTotals Totals, IReadOnlyList<TrafficZone> Zones);
public sealed record DwellZone(Guid ZoneId, string ZoneName, double? ReportedAverageDwellSeconds, double? LongestActiveDwellSeconds,
    int EstimatedCompletedVisits, double? EstimatedTotalDwellSeconds, double? EstimatedHistoricalAverageDwellSeconds,
    int UncertainVisits, DataQuality DataQuality);
public sealed record DwellReport(ReportPeriod Period, IReadOnlyList<DwellZone> Zones);
public sealed record UtilizationZone(Guid ZoneId, string ZoneName, int? Capacity, double? AverageUtilizationPercent,
    double? PeakUtilizationPercent, DataQuality DataQuality);
public sealed record UtilizationReport(ReportPeriod Period, IReadOnlyList<UtilizationZone> Zones);

public sealed record StaffPoint(PeriodPoint Period, double? AverageStaffCount, int? PeakStaffCount, DataQuality DataQuality);
public sealed record StaffCamera(Guid CameraId, string CameraName, int? LatestStaffCount, DateTime? LastReceivedAtUtc,
    double? AverageStaffCount, int? PeakStaffCount, DataQuality DataQuality, IReadOnlyList<StaffPoint> Series);
public sealed record StaffReport(ReportPeriod Period, IReadOnlyList<StaffCamera> Cameras);

public sealed record SessionData(Guid Id, Guid CameraId, string CameraName, string ExternalSessionId, string SourceType,
    string SourceName, string Status, long FirstSequence, long LastSequence, long? FirstSourceTimeMs, long? LastSourceTimeMs,
    DateTime FirstSourceTimestampUtc, DateTime LastSourceTimestampUtc, DateTime StartedAtUtc, DateTime LastReceivedAtUtc,
    DateTime? EndedAtUtc, DateTime CreatedAtUtc);
public sealed record SessionReport(SessionData Session, ReportResult Analytics, StaffReport Staff, int UntimedVideoObservations);

public interface IReportQueries
{
    Task<ReportResult> Zones(ReportFilter filter, bool series, CancellationToken ct);
    Task<StaffReport> Staff(ReportFilter filter, CancellationToken ct);
    Task<Page<SessionData>> Sessions(ListFilter filter, Guid? siteId, Guid? cameraId, string? sourceType, CancellationToken ct);
    Task<SessionData> Session(Guid id, CancellationToken ct);
    Task<SessionReport> SessionReport(Guid id, ReportFilter filter, CancellationToken ct);
}
