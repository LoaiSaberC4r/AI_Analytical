using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Persistence;

public sealed class CatalogQueries(AnalyticsDb db) : ICatalogQueries
{
    public Task<Page<SiteData>> Sites(ListFilter filter, CancellationToken ct) =>
        ReadPage(SiteRows(Filter(db.Sites.AsNoTracking(), filter)), filter, ct);

    public Task<Page<CameraData>> Cameras(ListFilter filter, Guid? siteId, CancellationToken ct) =>
        ReadPage(CameraRows(Filter(db.Cameras.AsNoTracking().Where(x => !siteId.HasValue || x.SiteId == siteId), filter)), filter, ct);

    public async Task<Page<ZoneData>> Zones(Guid cameraId, ListFilter filter, CancellationToken ct)
    {
        if (!await db.Cameras.AnyAsync(x => x.Id == cameraId, ct))
            throw ApiError.NotFound("Camera");
        return await ReadPage(ZoneRows(Filter(db.CameraZones.AsNoTracking().Where(x => x.CameraId == cameraId), filter)), filter, ct);
    }

    public async Task<SiteData> Site(Guid id, CancellationToken ct) =>
        await SiteRows(db.Sites.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw ApiError.NotFound("Site");

    public async Task<CameraData> Camera(Guid id, CancellationToken ct) =>
        await CameraRows(db.Cameras.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw ApiError.NotFound("Camera");

    public async Task<ZoneData> Zone(Guid id, CancellationToken ct) =>
        await ZoneRows(db.CameraZones.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw ApiError.NotFound("Zone");

    private static IOrderedQueryable<T> Filter<T>(IQueryable<T> rows, ListFilter filter) where T : CatalogRecord
    {
        var term = filter.Search?.Trim();
        return rows.Where(x => term == null || x.Name.Contains(term)).OrderBy(x => x.Name).ThenBy(x => x.Id);
    }

    private static IQueryable<SiteData> SiteRows(IQueryable<Site> rows) => rows.Select(x =>
        new SiteData(x.Id, x.Name, x.Code, x.Description, x.TimeZoneId, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, x.RowVersion));

    private IQueryable<CameraData> CameraRows(IQueryable<Camera> rows) => rows.Select(x =>
        new CameraData(x.Id, x.SiteId, x.Site.Name, x.ExternalCameraId, x.Name, x.Description, x.IsActive,
            db.CameraZones.Count(z => z.CameraId == x.Id), "cameras/" + x.ExternalCameraId + "/analytics",
            db.CameraCurrentStates.Where(s => s.CameraId == x.Id).Select(s => (DateTime?)s.LastReceivedAtUtc).FirstOrDefault(),
            x.CreatedAtUtc, x.UpdatedAtUtc, x.RowVersion));

    private static IQueryable<ZoneData> ZoneRows(IQueryable<CameraZone> rows) => rows.Select(x =>
        new ZoneData(x.Id, x.CameraId, x.ExternalZoneId, x.Name, x.Description, x.Capacity, x.WarningThresholdPercent,
            x.CriticalThresholdPercent, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, x.RowVersion));

    private static async Task<Page<T>> ReadPage<T>(IQueryable<T> rows, ListFilter filter, CancellationToken ct)
    {
        var skip = Input.Skip(filter);
        var count = await rows.CountAsync(ct);
        return new(await rows.Skip(skip).Take(filter.PageSize).ToListAsync(ct), filter.Page, filter.PageSize, count);
    }
}

public sealed class CatalogCommands(AnalyticsDb db, TimeProvider clock, IOptions<AnalyticsOptions> analytics) : ICatalogCommands
{
    public async Task<Saved> Site(Guid? id, SaveSite request, CancellationToken ct)
    {
        var name = Input.Required(request.Name, "Name", 160);
        var code = Input.Required(request.Code, "Code", 64).ToUpperInvariant();
        _ = Input.TimeZone(request.TimeZoneId);
        var site = await Edit<Site>(id, request.RowVersion, ct);
        site.Name = name;
        site.Code = code;
        site.Description = Input.Description(request.Description);
        site.TimeZoneId = request.TimeZoneId!.Trim();
        return await Persist(site.Id, ct);
    }

    public async Task<Saved> Camera(Guid? id, SaveCamera request, CancellationToken ct)
    {
        var name = Input.Required(request.Name, "Name", 160);
        await using var transaction = id.HasValue ? await WriteTransaction.Begin(db, ct) : null;
        if (!await db.Sites.AnyAsync(x => x.Id == request.SiteId, ct))
            throw ApiError.NotFound("Site");
        var camera = await Edit<Camera>(id, request.RowVersion, ct);
        var externalId = request.ExternalCameraId is null && id.HasValue
            ? camera.ExternalCameraId : Input.ExternalId(request.ExternalCameraId, "ExternalCameraId");
        if (id.HasValue && (camera.ExternalCameraId != externalId || camera.SiteId != request.SiteId)
            && await db.AnalyticsSessions.AnyAsync(x => x.CameraId == camera.Id, ct))
            throw ApiError.Conflict("The external ID and site are stable after ingestion begins.");
        if (id.HasValue && camera.SiteId != request.SiteId && (await db.ZoneSpatialLayouts.AnyAsync(x => x.Zone.CameraId == camera.Id, ct)
                || await db.AlertRules.AnyAsync(x => x.CameraId == camera.Id, ct)))
            throw ApiError.Conflict("A camera with existing floor layouts or alert rules must remain in its site.");
        camera.Name = name;
        camera.Description = Input.Description(request.Description);
        camera.SiteId = request.SiteId;
        camera.ExternalCameraId = externalId;
        var saved = await Persist(camera.Id, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task<Saved> Zone(Guid? id, Guid? cameraId, SaveZone request, CancellationToken ct)
    {
        var name = Input.Required(request.Name, "Name", 160);
        await using var transaction = id.HasValue ? await WriteTransaction.Begin(db, ct) : null;
        if (request.Capacity is <= 0)
            throw ApiError.Invalid("Capacity must be positive when supplied.");
        if (!id.HasValue && !await db.Cameras.AnyAsync(x => x.Id == cameraId, ct))
            throw ApiError.NotFound("Camera");
        var zone = await Edit<CameraZone>(id, request.RowVersion, ct);
        var warning = request.WarningThresholdPercent ?? (id.HasValue ? zone.WarningThresholdPercent : analytics.Value.DefaultWarningThresholdPercent);
        var critical = request.CriticalThresholdPercent ?? (id.HasValue ? zone.CriticalThresholdPercent : analytics.Value.DefaultCriticalThresholdPercent);
        if (!double.IsFinite(warning) || !double.IsFinite(critical) || warning < 0 || warning >= critical)
            throw ApiError.Invalid("The warning threshold must be nonnegative and lower than the critical threshold.");
        var externalId = request.ExternalZoneId is null && id.HasValue
            ? zone.ExternalZoneId : Input.ExternalId(request.ExternalZoneId, "ExternalZoneId");
        if (id.HasValue && zone.ExternalZoneId != externalId
            && (await db.ZoneCounterStates.AnyAsync(x => x.CameraZoneId == zone.Id, ct)
                || await db.ZoneSnapshots.AnyAsync(x => x.CameraZoneId == zone.Id, ct)))
            throw ApiError.Conflict("The external zone ID is stable after ingestion begins.");
        zone.Name = name;
        zone.Description = Input.Description(request.Description);
        zone.ExternalZoneId = externalId;
        zone.CameraId = cameraId ?? zone.CameraId;
        zone.Capacity = request.Capacity;
        zone.WarningThresholdPercent = warning;
        zone.CriticalThresholdPercent = critical;
        var saved = await Persist(zone.Id, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return saved;
    }

    public async Task Status(CatalogKind kind, Guid id, SetStatus request, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Begin(db, ct);
        CatalogRecord entity = kind switch
        {
            CatalogKind.Site => await Edit<Site>(id, request.RowVersion, ct),
            CatalogKind.Camera => await Edit<Camera>(id, request.RowVersion, ct),
            CatalogKind.Zone => await Edit<CameraZone>(id, request.RowVersion, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        entity.IsActive = request.IsActive;
        await Persist(id, ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<T> Edit<T>(Guid? id, string? rowVersion, CancellationToken ct) where T : CatalogRecord, new()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (id is null)
        {
            var created = new T { CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Add(created);
            return created;
        }

        var token = Input.RowVersion(rowVersion);
        var entity = await db.Set<T>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound(typeof(T).Name);
        if (!entity.RowVersion.AsSpan().SequenceEqual(token))
            throw ApiError.Concurrency();
        var entry = db.Entry(entity);
        entry.Property(x => x.RowVersion).OriginalValue = token;
        entry.Property(x => x.UpdatedAtUtc).CurrentValue = now;
        entry.Property(x => x.UpdatedAtUtc).IsModified = true;
        return entity;
    }

    private async Task<Saved> Persist(Guid id, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new(id);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiError.Concurrency();
        }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw ApiError.Conflict("The code or external ID already exists in its namespace.");
        }
    }
}
