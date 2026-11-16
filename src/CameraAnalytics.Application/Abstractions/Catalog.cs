using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Abstractions;

public sealed record SiteData(Guid Id, string Name, string Code, string? Description, string TimeZoneId,
    bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);

public sealed record CameraData(Guid Id, Guid SiteId, string SiteName, string ExternalCameraId, string Name,
    string? Description, bool IsActive, int ZonesCount, string MqttTopic, DateTime? LastReceivedAtUtc,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);

public sealed record ZoneData(Guid Id, Guid CameraId, string ExternalZoneId, string Name, string? Description,
    int? Capacity, double WarningThresholdPercent, double CriticalThresholdPercent,
    bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);

public sealed record SaveSite(string? Name, string? Code, string? Description, string? TimeZoneId, string? RowVersion);
public sealed record SaveCamera(Guid SiteId, string? ExternalCameraId, string? Name, string? Description, string? RowVersion);
public sealed record SaveZone(string? ExternalZoneId, string? Name, string? Description, int? Capacity,
    double? WarningThresholdPercent, double? CriticalThresholdPercent, string? RowVersion);
public sealed record SetStatus([property: System.Text.Json.Serialization.JsonRequired] bool IsActive, string? RowVersion);
public enum CatalogKind { Site, Camera, Zone }

public interface ICatalogQueries
{
    Task<Page<SiteData>> Sites(ListFilter filter, CancellationToken ct);
    Task<Page<CameraData>> Cameras(ListFilter filter, Guid? siteId, CancellationToken ct);
    Task<Page<ZoneData>> Zones(Guid cameraId, ListFilter filter, CancellationToken ct);
    Task<SiteData> Site(Guid id, CancellationToken ct);
    Task<CameraData> Camera(Guid id, CancellationToken ct);
    Task<ZoneData> Zone(Guid id, CancellationToken ct);
}

public interface ICatalogCommands
{
    Task<Saved> Site(Guid? id, SaveSite request, CancellationToken ct);
    Task<Saved> Camera(Guid? id, SaveCamera request, CancellationToken ct);
    Task<Saved> Zone(Guid? id, Guid? cameraId, SaveZone request, CancellationToken ct);
    Task Status(CatalogKind kind, Guid id, SetStatus request, CancellationToken ct);
}
