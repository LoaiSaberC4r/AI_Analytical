namespace CameraAnalytics.Contracts.Mqtt;

// The MQTT reader enforces presence through required members, including nullable source time.
public sealed class CameraAnalyticsPayload
{
    public required string SchemaVersion { get; init; }
    public required string CameraId { get; init; }
    public required string SourceType { get; init; }
    public required string SourceName { get; init; }
    public required string SessionId { get; init; }
    public required long? SourceTimeMs { get; init; }
    public required double ProcessingFps { get; init; }
    public required ZoneAnalyticsPayload[] Zones { get; init; }
    public required int StaffCount { get; init; }
    public required string Status { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed class ZoneAnalyticsPayload
{
    public required string ZoneId { get; init; }
    public required int Occupancy { get; init; }
    public required long ZoneArrivals { get; init; }
    public required long ZoneDepartures { get; init; }
    public required long Entry { get; init; }
    public required long Exit { get; init; }
    public required long WrongWayIn { get; init; }
    public required long WrongWayOut { get; init; }
    public required double AverageDwellSeconds { get; init; }
    public required ActiveVisitorPayload[] ActiveVisitors { get; init; }
}

public sealed class ActiveVisitorPayload
{
    public required long TrackId { get; init; }
    public required double DwellSeconds { get; init; }
}
