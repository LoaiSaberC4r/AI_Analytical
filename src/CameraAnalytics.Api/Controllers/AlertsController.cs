using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CameraAnalytics.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class AlertsController(IAlerts alerts, SiteAccess access) : ControllerBase
{
    [HttpGet("alerts")]
    public Task<Page<AlertData>> List(Guid siteId, CancellationToken ct, Guid? cameraId = null, string? status = null,
        int page = 1, int pageSize = 20, string? search = null)
    {
        access.Demand(User, siteId);
        return alerts.List(siteId, cameraId, status, new(page, pageSize, search), ct);
    }

    [HttpGet("alerts/{id:guid}")]
    public async Task<AlertData> Get(Guid id, CancellationToken ct)
    {
        var alert = await alerts.Get(id, ct);
        access.Demand(User, alert.Scope.SiteId);
        return alert;
    }

    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<AlertData> Acknowledge(Guid id, CancellationToken ct)
    {
        access.Demand(User, (await alerts.Get(id, ct)).Scope.SiteId, write: true);
        return await alerts.Acknowledge(id, User.FindFirst("sub")?.Value, ct);
    }

    [HttpPost("alerts/{id:guid}/resolve")]
    public async Task<AlertData> Resolve(Guid id, CancellationToken ct)
    {
        access.Demand(User, (await alerts.Get(id, ct)).Scope.SiteId, write: true);
        return await alerts.Resolve(id, User.FindFirst("sub")?.Value, ct);
    }

    [HttpGet("alert-rules")]
    public Task<Page<AlertRuleData>> Rules(Guid siteId, CancellationToken ct, int page = 1, int pageSize = 20, string? search = null)
    {
        access.Demand(User, siteId);
        return alerts.Rules(siteId, new(page, pageSize, search), ct);
    }

    [HttpPost("alert-rules")]
    public async Task<ActionResult<AlertRuleData>> Create(SaveAlertRule rule, CancellationToken ct)
    {
        access.Demand(User, rule.SiteId, write: true);
        var saved = await alerts.SaveRule(null, rule, ct);
        return CreatedAtAction(nameof(Rule), new { id = saved.Id }, saved);
    }

    [HttpGet("alert-rules/{id:guid}")]
    public async Task<AlertRuleData> Rule(Guid id, CancellationToken ct)
    {
        var rule = await alerts.Rule(id, ct);
        access.Demand(User, rule.Scope.SiteId);
        return rule;
    }

    [HttpPut("alert-rules/{id:guid}")]
    public async Task<AlertRuleData> Update(Guid id, SaveAlertRule rule, CancellationToken ct)
    {
        access.Demand(User, (await alerts.Rule(id, ct)).Scope.SiteId, write: true);
        return await alerts.SaveRule(id, rule, ct);
    }

    [HttpPatch("alert-rules/{id:guid}/status")]
    public async Task<AlertRuleData> Status(Guid id, SetStatus input, CancellationToken ct)
    {
        access.Demand(User, (await alerts.Rule(id, ct)).Scope.SiteId, write: true);
        return await alerts.SetRuleStatus(id, input, ct);
    }
}
