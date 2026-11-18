using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed partial class FloorPlans(AnalyticsDb db, TimeProvider clock, ReportQueries reports, IOptions<FloorPlanOptions> limits,
    IOptions<AnalyticsOptions> analytics, IOptions<MinuteAggregationOptions> minute,
    IOptions<VisualizationOptions> visuals, IOptions<CongestionOptions> congestion) : IFloorPlans
{
    private static readonly JsonSerializerOptions GeometryJson = new(JsonSerializerDefaults.Web);

    public async Task<Page<FloorData>> List(Guid siteId, ListFilter filter, CancellationToken ct)
    {
        var skip = Input.Skip(filter);
        if (!await db.Sites.AnyAsync(x => x.Id == siteId, ct))
            throw ApiError.NotFound("Site");
        var query = db.SiteFloors.AsNoTracking().Where(x => x.SiteId == siteId
            && (filter.Search == null || x.Name.Contains(filter.Search)));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Skip(skip).Take(filter.PageSize).ToListAsync(ct);
        return new(rows.Select(Data).ToArray(), filter.Page, filter.PageSize, count);
    }

    public async Task<FloorData> Get(Guid floorId, CancellationToken ct) => Data(await Floor(floorId, ct));

    public async Task<FloorData> Save(Guid siteId, Guid? floorId, SaveFloor input, CancellationToken ct)
    {
        var name = Input.Required(input.Name, "Name", 160);
        if (input.DisplayOrder is < 0 or > 10_000)
            throw ApiError.Invalid("Display order must be between 0 and 10000.");
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        SiteFloor floor;
        if (floorId.HasValue)
        {
            floor = await Floor(floorId.Value, ct);
            if (siteId != floor.SiteId)
                throw ApiError.Invalid("Floor does not belong to the specified site.");
            VerifyVersion(floor.RowVersion, input.RowVersion);
        }
        else
        {
            if (!await db.Sites.AnyAsync(x => x.Id == siteId, ct))
                throw ApiError.NotFound("Site");
            floor = db.SiteFloors.Add(new SiteFloor { SiteId = siteId, CreatedAtUtc = now }).Entity;
        }
        floor.Name = name;
        floor.Description = Input.Description(input.Description);
        floor.DisplayOrder = input.DisplayOrder;
        floor.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Data(floor);
    }

    public async Task Status(Guid floorId, SetStatus input, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var floor = await Floor(floorId, ct);
        VerifyVersion(floor.RowVersion, input.RowVersion);
        floor.IsActive = input.IsActive;
        floor.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ZoneLayoutData>> Layouts(Guid floorId, Guid? cameraId, CancellationToken ct)
    {
        var floor = await Floor(floorId, ct);
        if (cameraId.HasValue)
        {
            var siteId = await db.Cameras.Where(x => x.Id == cameraId).Select(x => (Guid?)x.SiteId).SingleOrDefaultAsync(ct)
                ?? throw ApiError.NotFound("Camera");
            if (siteId != floor.SiteId)
                throw ApiError.Invalid("Camera does not belong to the floor's site.");
        }
        var layouts = await LayoutRows(floorId).Where(x => !cameraId.HasValue || x.Zone.CameraId == cameraId)
            .OrderBy(x => x.Zone.CameraId).ThenBy(x => x.Zone.Name).ThenBy(x => x.CameraZoneId).ToListAsync(ct);
        return layouts.Select(Layout).ToArray();
    }

    public async Task<ZoneLayoutData> SaveLayout(Guid floorId, Guid zoneId, SaveLayout input, CancellationToken ct)
    {
        PolygonGeometry.Validate(input.Vertices, limits.Value.MaxPolygonVertices);
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var floor = await Floor(floorId, ct);
        var zone = await db.CameraZones.Include(x => x.Camera).SingleOrDefaultAsync(x => x.Id == zoneId, ct) ?? throw ApiError.NotFound("Zone");
        if (zone.Camera.SiteId != floor.SiteId)
            throw ApiError.Invalid("A layout must link to a zone in the same site.");
        var layout = await db.ZoneSpatialLayouts.FindAsync([floorId, zoneId], ct);
        if (layout is null)
        {
            if (input.RowVersion is not null)
                throw ApiError.Concurrency();
            if (await db.ZoneSpatialLayouts.CountAsync(x => x.SiteFloorId == floorId, ct) >= limits.Value.MaxLayoutsPerFloor)
                throw ApiError.Conflict("The floor has reached the configured layout limit.");
            layout = db.ZoneSpatialLayouts.Add(new() { SiteFloorId = floorId, CameraZoneId = zoneId, Zone = zone }).Entity;
        }
        else if (input.RowVersion is null)
            throw ApiError.Concurrency();
        else
            VerifyVersion(layout.RowVersion, input.RowVersion);
        layout.VerticesJson = JsonSerializer.Serialize(input.Vertices, GeometryJson);
        layout.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Layout(layout);
    }

    public async Task DeleteLayout(Guid floorId, Guid zoneId, string? rowVersion, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var layout = await db.ZoneSpatialLayouts.FindAsync([floorId, zoneId], ct) ?? throw ApiError.NotFound("Layout");
        VerifyVersion(layout.RowVersion, rowVersion);
        db.ZoneSpatialLayouts.Remove(layout);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private IQueryable<ZoneSpatialLayout> LayoutRows(Guid floorId) => db.ZoneSpatialLayouts.AsNoTracking()
        .Where(x => x.SiteFloorId == floorId).Include(x => x.Zone).ThenInclude(x => x.Camera);

    private async Task<SiteFloor> Floor(Guid floorId, CancellationToken ct) =>
        await db.SiteFloors.SingleOrDefaultAsync(x => x.Id == floorId, ct) ?? throw ApiError.NotFound("Floor");

    private static void VerifyVersion(byte[] current, string? version)
    {
        if (!current.AsSpan().SequenceEqual(Input.RowVersion(version)))
            throw ApiError.Concurrency();
    }

    private static FloorData Data(SiteFloor x) => new(x.Id, x.SiteId, x.Name, x.Description, x.DisplayOrder, x.IsActive, x.RowVersion);
    private static ZoneLayoutData Layout(ZoneSpatialLayout x) => new(x.SiteFloorId, x.CameraZoneId, x.Zone.CameraId,
        x.Zone.Name, x.Zone.Camera.Name, JsonSerializer.Deserialize<PolygonVertex[]>(x.VerticesJson, GeometryJson)!, x.RowVersion);
}
