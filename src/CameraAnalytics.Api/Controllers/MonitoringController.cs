using CameraAnalytics.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace CameraAnalytics.Api.Controllers;

[ApiController]
[Route("api/v1")]
[ProducesResponseType<ProblemDetails>(404)]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = Extensions.SiteAccess.Administration)]
public sealed class MonitoringController(IMonitoringQueries monitoring) : ControllerBase
{
    [HttpGet("sites/{siteId:guid}/live")]
    public Task<LiveSite> Site(Guid siteId, CancellationToken ct) => monitoring.Site(siteId, ct);

    [HttpGet("cameras/{cameraId:guid}/live")]
    public Task<LiveCamera> Camera(Guid cameraId, CancellationToken ct) => monitoring.Camera(cameraId, ct);

    [HttpGet("zones/{zoneId:guid}/live")]
    public Task<LiveZone> Zone(Guid zoneId, CancellationToken ct) => monitoring.Zone(zoneId, ct);

    [HttpGet("monitoring/status")]
    public Task<MonitoringStatus> Status(CancellationToken ct) => monitoring.Status(ct);
}
