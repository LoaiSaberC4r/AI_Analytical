using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CameraAnalytics.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class FloorsController(IFloorPlans floors, SiteAccess access) : ControllerBase
{
    [HttpGet("sites/{siteId:guid}/floors")]
    public Task<Page<FloorData>> List(Guid siteId, CancellationToken ct, int page = 1, int pageSize = 20, string? search = null)
    {
        access.Demand(User, siteId);
        return floors.List(siteId, new(page, pageSize, search), ct);
    }

    [HttpPost("sites/{siteId:guid}/floors")]
    public async Task<ActionResult<FloorData>> Create(Guid siteId, SaveFloor input, CancellationToken ct)
    {
        access.Demand(User, siteId, write: true);
        var floor = await floors.Save(siteId, null, input, ct);
        return CreatedAtAction(nameof(Get), new { id = floor.Id }, floor);
    }

    [HttpGet("floors/{id:guid}")]
    public Task<FloorData> Get(Guid id, CancellationToken ct) => Authorize(id, false, ct);

    [HttpPut("floors/{id:guid}")]
    public async Task<FloorData> Update(Guid id, SaveFloor input, CancellationToken ct) =>
        await floors.Save((await Authorize(id, true, ct)).SiteId, id, input, ct);

    [HttpPatch("floors/{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, SetStatus input, CancellationToken ct)
    {
        await Authorize(id, true, ct);
        await floors.Status(id, input, ct);
        return NoContent();
    }

    [HttpGet("floors/{id:guid}/layouts")]
    public async Task<IReadOnlyList<ZoneLayoutData>> Layouts(Guid id, [FromQuery] Guid? cameraId, CancellationToken ct)
    {
        await Authorize(id, false, ct);
        return await floors.Layouts(id, cameraId, ct);
    }

    [HttpPut("floors/{id:guid}/zones/{zoneId:guid}/layout")]
    public async Task<ZoneLayoutData> SaveLayout(Guid id, Guid zoneId, SaveLayout input, CancellationToken ct)
    {
        await Authorize(id, true, ct);
        return await floors.SaveLayout(id, zoneId, input, ct);
    }

    [HttpDelete("floors/{id:guid}/zones/{zoneId:guid}/layout")]
    public async Task<IActionResult> DeleteLayout(Guid id, Guid zoneId, [FromQuery] string? rowVersion, CancellationToken ct)
    {
        await Authorize(id, true, ct);
        await floors.DeleteLayout(id, zoneId, rowVersion, ct);
        return NoContent();
    }

    [HttpGet("analytics/heatmap")]
    public async Task<VisualizationResponse> Heatmap([FromQuery] Guid floorId, [FromQuery] ReportFilter filter, CancellationToken ct,
        string metricKey = "arrivals") => await floors.Heatmap(await Authorize(floorId, false, ct), metricKey, filter, ct);

    private async Task<FloorData> Authorize(Guid floorId, bool write, CancellationToken ct)
    {
        var floor = await floors.Get(floorId, ct);
        access.Demand(User, floor.SiteId, write);
        return floor;
    }
}
