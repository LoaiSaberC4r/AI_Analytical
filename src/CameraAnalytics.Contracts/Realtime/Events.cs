namespace CameraAnalytics.Contracts.Realtime;

public sealed record AnalyticsAlertChanged(Guid AlertId, Guid SiteId, Guid CameraId, long StateVersion, string Status);

public sealed record CameraSnapshotUpdated(Guid CameraId, Guid SiteId, Guid SessionId, long Sequence,
    long StateVersion, string SourceType, string AnalyticsStatus, string DataFreshness, double ProcessingFps, int StaffCount,
    DateTime ReceivedAtUtc);
public sealed record ZoneMetricsUpdated(Guid CameraId, Guid ZoneId, Guid SessionId, long Sequence, long StateVersion,
    int Occupancy, long Arrivals, long Departures, long Entry, long Exit, long WrongWayIn, long WrongWayOut,
    double ReportedAverageDwellSeconds, int ActiveVisitorsCount, double? LongestActiveDwellSeconds, DateTime ReceivedAtUtc);
public sealed record CameraStatusChanged(Guid CameraId, Guid SiteId, Guid SessionId, long StateVersion,
    string AnalyticsStatus, string DataFreshness, DateTime ReceivedAtUtc);
public sealed record AnalyticsSummaryUpdated(Guid CameraId, Guid SiteId, Guid SessionId, long StateVersion, DateTime ReceivedAtUtc);
