namespace CameraAnalytics.Application.Abstractions;

public sealed record LiveZone(Guid ZoneId, Guid CameraId, string ExternalZoneId, string ZoneName, bool IsActive,
    Guid? SessionId, long? Sequence, long? StateVersion, bool PreviousSession, string DataFreshness,
    int? Occupancy, long? ZoneArrivals, long? ZoneDepartures, long? Entry, long? Exit, long? WrongWayIn,
    long? WrongWayOut, double? ReportedAverageDwellSeconds, double? LongestActiveDwellSeconds, int? ActiveVisitorsCount,
    int? Capacity, double WarningThresholdPercent, double CriticalThresholdPercent,
    double? UtilizationPercent, string? CapacityStatus, DateTime? LastUpdatedAtUtc);

public sealed record LiveCamera(Guid CameraId, Guid SiteId, string ExternalCameraId, string CameraName, bool IsActive,
    Guid? SessionId, long? LastSequence, string? SourceType, string? AnalyticsStatus, string DataFreshness,
    double? ProcessingFps, int? StaffCount, DateTime? LastReceivedAtUtc, long? StateVersion, IReadOnlyList<LiveZone> Zones);

public sealed record LiveSite(Guid SiteId, string SiteName, bool IsActive, IReadOnlyList<LiveCamera> Cameras);
public sealed record MonitoringStatus(int RegisteredCameras, int ActiveCameras, int Fresh, int Stale, int NoRecentData,
    int NeverReceived, int Ended, bool MqttEnabled, bool MqttSubscribed, DateTime CheckedAtUtc);

public interface IMonitoringQueries
{
    Task<LiveSite> Site(Guid id, CancellationToken ct);
    Task<LiveCamera> Camera(Guid id, CancellationToken ct);
    Task<LiveZone> Zone(Guid id, CancellationToken ct);
    Task<MonitoringStatus> Status(CancellationToken ct);
}
