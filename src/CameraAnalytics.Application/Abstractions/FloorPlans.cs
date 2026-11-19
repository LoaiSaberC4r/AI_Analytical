using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Abstractions;

public sealed record FloorData(Guid Id, Guid SiteId, string Name, string? Description, int DisplayOrder, bool IsActive, byte[] RowVersion);
public sealed record SaveFloor(string Name, string? Description = null, int DisplayOrder = 0, string? RowVersion = null);
public readonly record struct PolygonVertex(double X, double Y);
public sealed record SaveLayout(IReadOnlyList<PolygonVertex> Vertices, string? RowVersion = null);
public sealed record ZoneLayoutData(Guid FloorId, Guid ZoneId, Guid CameraId, string ZoneName, string CameraName,
    IReadOnlyList<PolygonVertex> Vertices, byte[] RowVersion);
public sealed record HeatmapZone(ZoneLayoutData Layout, double? Value, MetricQuality Quality);
public sealed record HeatmapVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period, FloorData Floor,
    IReadOnlyList<HeatmapZone> Zones) : VisualizationResponse(MetricKey, Title, Unit, Period);

public interface IFloorPlans
{
    Task<Page<FloorData>> List(Guid siteId, ListFilter filter, CancellationToken ct);
    Task<FloorData> Get(Guid floorId, CancellationToken ct);
    Task<FloorData> Save(Guid siteId, Guid? floorId, SaveFloor input, CancellationToken ct);
    Task Status(Guid floorId, SetStatus input, CancellationToken ct);
    Task<IReadOnlyList<ZoneLayoutData>> Layouts(Guid floorId, Guid? cameraId, CancellationToken ct);
    Task<ZoneLayoutData> SaveLayout(Guid floorId, Guid zoneId, SaveLayout input, CancellationToken ct);
    Task DeleteLayout(Guid floorId, Guid zoneId, string? rowVersion, CancellationToken ct);
    Task<HeatmapVisualization> Heatmap(FloorData floor, string metric, ReportFilter filter, CancellationToken ct);
}
