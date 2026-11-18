using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CameraAnalytics.Api.Controllers;

[ApiController]
[Route("api/v1")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = Extensions.SiteAccess.Administration)]
public sealed class CatalogController(ICatalogQueries queries, ICatalogCommands commands) : ControllerBase
{
    [HttpGet("sites")]
    public Task<Page<SiteData>> Sites(CancellationToken ct, int page = 1, int pageSize = 20, string? search = null) =>
        queries.Sites(new(page, pageSize, search), ct);

    [HttpGet("sites/{id:guid}")]
    public Task<SiteData> Site(Guid id, CancellationToken ct) => queries.Site(id, ct);

    [HttpPost("sites")]
    [ProducesResponseType<SiteData>(201)]
    public async Task<ActionResult<SiteData>> CreateSite(SaveSite model, CancellationToken ct)
    {
        var created = await commands.Site(null, model, ct);
        return CreatedAtAction(nameof(Site), new { id = created.Id }, await queries.Site(created.Id, ct));
    }

    [HttpPut("sites/{id:guid}")]
    public async Task<SiteData> UpdateSite(Guid id, SaveSite model, CancellationToken ct)
    {
        await commands.Site(id, model, ct);
        return await queries.Site(id, ct);
    }

    [HttpPatch("sites/{id:guid}/status")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SiteStatus(Guid id, SetStatus model, CancellationToken ct)
    {
        await commands.Status(CatalogKind.Site, id, model, ct);
        return NoContent();
    }

    [HttpGet("cameras")]
    public Task<Page<CameraData>> Cameras(CancellationToken ct, int page = 1, int pageSize = 20,
        string? search = null, Guid? siteId = null) => queries.Cameras(new(page, pageSize, search), siteId, ct);

    [HttpGet("cameras/{id:guid}")]
    public Task<CameraData> Camera(Guid id, CancellationToken ct) => queries.Camera(id, ct);

    [HttpPost("cameras")]
    [ProducesResponseType<CameraData>(201)]
    public async Task<ActionResult<CameraData>> CreateCamera(SaveCamera model, CancellationToken ct)
    {
        var created = await commands.Camera(null, model, ct);
        return CreatedAtAction(nameof(Camera), new { id = created.Id }, await queries.Camera(created.Id, ct));
    }

    [HttpPut("cameras/{id:guid}")]
    public async Task<CameraData> UpdateCamera(Guid id, SaveCamera model, CancellationToken ct)
    {
        await commands.Camera(id, model, ct);
        return await queries.Camera(id, ct);
    }

    [HttpPatch("cameras/{id:guid}/status")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> CameraStatus(Guid id, SetStatus model, CancellationToken ct)
    {
        await commands.Status(CatalogKind.Camera, id, model, ct);
        return NoContent();
    }

    [HttpGet("cameras/{cameraId:guid}/zones")]
    public Task<Page<ZoneData>> Zones(Guid cameraId, CancellationToken ct, int page = 1, int pageSize = 20,
        string? search = null) => queries.Zones(cameraId, new(page, pageSize, search), ct);

    [HttpGet("zones/{id:guid}")]
    public Task<ZoneData> Zone(Guid id, CancellationToken ct) => queries.Zone(id, ct);

    [HttpPost("cameras/{cameraId:guid}/zones")]
    [ProducesResponseType<ZoneData>(201)]
    public async Task<ActionResult<ZoneData>> CreateZone(Guid cameraId, SaveZone model, CancellationToken ct)
    {
        var created = await commands.Zone(null, cameraId, model, ct);
        return CreatedAtAction(nameof(Zone), new { id = created.Id }, await queries.Zone(created.Id, ct));
    }

    [HttpPut("zones/{id:guid}")]
    public async Task<ZoneData> UpdateZone(Guid id, SaveZone model, CancellationToken ct)
    {
        await commands.Zone(id, null, model, ct);
        return await queries.Zone(id, ct);
    }

    [HttpPatch("zones/{id:guid}/status")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ZoneStatus(Guid id, SetStatus model, CancellationToken ct)
    {
        await commands.Status(CatalogKind.Zone, id, model, ct);
        return NoContent();
    }
}
