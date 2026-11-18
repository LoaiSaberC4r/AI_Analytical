using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed class AlertStore(AnalyticsDb db, TimeProvider clock, IOptions<AlertOptions> options,
    IAlertNotifications notifications, ILogger<AlertStore> logger) : IAlerts
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Page<AlertRuleData>> Rules(Guid siteId, ListFilter filter, CancellationToken ct)
    {
        var skip = Input.Skip(filter);
        if (!await db.Sites.AnyAsync(x => x.Id == siteId, ct)) throw ApiError.NotFound("Site");
        var rows = db.AlertRules.AsNoTracking().Where(x => x.SiteId == siteId && (filter.Search == null || x.Name.Contains(filter.Search)));
        return new((await rows.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(skip).Take(filter.PageSize).ToListAsync(ct)).Select(Data).ToArray(),
            filter.Page, filter.PageSize, await rows.CountAsync(ct));
    }

    public async Task<AlertRuleData> Rule(Guid id, CancellationToken ct) => Data(await db.AlertRules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw ApiError.NotFound("Alert rule"));

    public async Task<AlertRuleData> SaveRule(Guid? id, SaveAlertRule input, CancellationToken ct)
    {
        var name = Input.Required(input.Name, "Name", 160);
        var cooldown = input.CooldownSeconds ?? options.Value.DefaultCooldownSeconds;
        var coverage = input.MinimumCoveragePercent ?? options.Value.MinimumCoveragePercent;
        var zoneScoped = input.Kind is "Congestion" or "LongStay" or "WrongWaySpike";
        if ((!zoneScoped && input.Kind is not ("CameraNoRecentData" or "LowFps")) || zoneScoped != input.ZoneId.HasValue
            || input.Severity is not ("Warning" or "Critical") || !double.IsFinite(input.Threshold) || input.Threshold <= 0
            || input.Threshold > 1_000_000_000 || (input.Kind == "CameraNoRecentData" && (input.AllowVideo || input.Threshold > 86_400)))
            throw ApiError.Invalid("Provide a supported alert kind, severity, positive threshold and matching camera or zone scope.");
        if (input.WindowSeconds is < 1 or > 3600 || input.MinimumObservedSeconds < 0 || input.MinimumObservedSeconds > input.WindowSeconds
            || input.RecoverySeconds is < 1 || input.RecoverySeconds > input.WindowSeconds || cooldown is < 0 or > 86_400
            || coverage is not (> 0 and <= 100))
            throw ApiError.Invalid("Check the alert window, observed duration, recovery, cooldown and coverage.");

        await using var transaction = await WriteTransaction.Begin(db, ct);
        var site = await db.Cameras.Where(x => x.Id == input.CameraId).Select(x => (Guid?)x.SiteId).SingleOrDefaultAsync(ct)
            ?? throw ApiError.NotFound("Camera");
        if (site != input.SiteId)
            throw ApiError.Invalid("Camera does not belong to the selected site.");
        if (input.ZoneId.HasValue)
        {
            var camera = await db.CameraZones.Where(x => x.Id == input.ZoneId).Select(x => (Guid?)x.CameraId).SingleOrDefaultAsync(ct)
                ?? throw ApiError.NotFound("Zone");
            if (camera != input.CameraId)
                throw ApiError.Invalid("Zone does not belong to the selected camera.");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        AlertRule rule;
        AnalyticsAlert? closed = null;
        if (id.HasValue)
        {
            rule = await FindRule(id.Value, ct);
            Match(rule, input.RowVersion);
            if (rule.SiteId != input.SiteId || rule.CameraId != input.CameraId || rule.CameraZoneId != input.ZoneId || rule.Kind != input.Kind)
                throw ApiError.Invalid("An alert rule's site, camera, zone and kind are immutable.");
            var unchanged = rule.Threshold == input.Threshold && rule.Severity == input.Severity && rule.MinimumObservedSeconds == input.MinimumObservedSeconds
                && rule.RecoverySeconds == input.RecoverySeconds && rule.WindowSeconds == input.WindowSeconds && rule.CooldownSeconds == cooldown
                && rule.MinimumCoveragePercent == coverage && rule.AllowVideo == input.AllowVideo;
            if (!unchanged)
            {
                closed = await Open(rule.Id, ct);
                if (closed is not null) Close(rule, closed, now, "rule_updated");
            }
        }
        else
        {
            if (await db.AlertRules.CountAsync(ct) >= options.Value.MaxRules)
                throw ApiError.Conflict("The configured alert rule limit has been reached.");
            rule = db.AlertRules.Add(new AlertRule { SiteId = input.SiteId, CameraId = input.CameraId, CameraZoneId = input.ZoneId,
                Kind = input.Kind, CreatedAtUtc = now }).Entity;
        }
        rule.Name = name;
        rule.Severity = input.Severity;
        rule.Threshold = input.Threshold;
        rule.MinimumObservedSeconds = input.MinimumObservedSeconds;
        rule.RecoverySeconds = input.RecoverySeconds;
        rule.WindowSeconds = input.WindowSeconds;
        rule.CooldownSeconds = cooldown;
        rule.MinimumCoveragePercent = coverage;
        rule.AllowVideo = input.AllowVideo;
        rule.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (closed is not null) await Notify(closed, ct);
        return Data(rule);
    }

    public async Task<AlertRuleData> SetRuleStatus(Guid id, SetStatus input, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var rule = await FindRule(id, ct);
        Match(rule, input.RowVersion);
        rule.IsActive = input.IsActive;
        rule.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        var alert = input.IsActive ? null : await Open(rule.Id, ct);
        if (alert is not null) Close(rule, alert, rule.UpdatedAtUtc, "rule_disabled");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (alert is not null) await Notify(alert, ct);
        return Data(rule);
    }

    public async Task<Page<AlertData>> List(Guid siteId, Guid? cameraId, string? status, ListFilter filter, CancellationToken ct)
    {
        var skip = Input.Skip(filter);
        if (status is not (null or "Active" or "Acknowledged" or "Resolved"))
            throw ApiError.Invalid("Status must be Active, Acknowledged or Resolved.");
        if (!await db.Sites.AnyAsync(x => x.Id == siteId, ct)) throw ApiError.NotFound("Site");
        if (cameraId.HasValue)
        {
            var site = await db.Cameras.Where(x => x.Id == cameraId).Select(x => (Guid?)x.SiteId).SingleOrDefaultAsync(ct)
                ?? throw ApiError.NotFound("Camera");
            if (site != siteId) throw ApiError.Invalid("Camera does not belong to the selected site.");
        }
        var query = db.AnalyticsAlerts.AsNoTracking().Where(x => x.SiteId == siteId && (!cameraId.HasValue || x.CameraId == cameraId)
            && (filter.Search == null || x.Kind.Contains(filter.Search)));
        query = status switch
        {
            "Active" => query.Where(x => x.ResolvedAtUtc == null && x.AcknowledgedAtUtc == null),
            "Acknowledged" => query.Where(x => x.ResolvedAtUtc == null && x.AcknowledgedAtUtc != null),
            "Resolved" => query.Where(x => x.ResolvedAtUtc != null),
            _ => query
        };
        var rows = await query.OrderByDescending(x => x.OpenedAtUtc).ThenByDescending(x => x.Id).Skip(skip).Take(filter.PageSize).ToListAsync(ct);
        return new(rows.Select(Data).ToArray(), filter.Page, filter.PageSize, await query.CountAsync(ct));
    }

    public async Task<AlertData> Get(Guid id, CancellationToken ct) => Data(await db.AnalyticsAlerts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw ApiError.NotFound("Alert"));

    public Task<AlertData> Acknowledge(Guid id, string? actor, CancellationToken ct) => Transition(id, actor, false, ct);
    public Task<AlertData> Resolve(Guid id, string? actor, CancellationToken ct) => Transition(id, actor, true, ct);

    private async Task<AlertData> Transition(Guid id, string? actor, bool resolve, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var alert = await Find(id, ct);
        if (alert.ResolvedAtUtc.HasValue || (!resolve && alert.AcknowledgedAtUtc.HasValue))
            return Data(alert);
        actor = actor?.Length > 256 ? actor[..256] : actor;
        var now = clock.GetUtcNow().UtcDateTime;
        if (resolve)
        {
            var rule = await FindRule(alert.AlertRuleId, ct);
            if (rule.IsActive && options.Value.Enabled)
                throw ApiError.Conflict("An enabled rule resolves its alerts when recovery is observed. Acknowledge it while the condition is active.");
            Close(rule, alert, now, "manual_resolution", actor);
        }
        else
        {
            alert.AcknowledgedAtUtc = now;
            alert.AcknowledgedBy = actor;
            alert.StateVersion++;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await Notify(alert, ct);
        return Data(alert);
    }

    internal async Task Notify(AnalyticsAlert alert, CancellationToken ct)
    {
        var data = Data(alert);
        AnalyticsTelemetry.AlertTransitions.Add(1, new KeyValuePair<string, object?>("status", data.Status));
        try { await notifications.Changed(data, ct); }
        catch (Exception e) { logger.LogWarning(e, "SignalR alert notification failed for {AlertId}", alert.Id); }
    }

    internal Task<AnalyticsAlert?> Open(Guid ruleId, CancellationToken ct) => db.AnalyticsAlerts
        .SingleOrDefaultAsync(x => x.AlertRuleId == ruleId && x.ResolvedAtUtc == null, ct);

    internal static void Close(AlertRule rule, AnalyticsAlert alert, DateTime now, string reason, string? actor = null)
    {
        alert.ResolvedAtUtc = rule.LastResolvedAtUtc = now;
        alert.ResolvedBy = actor;
        alert.ResolutionReason = reason;
        alert.StateVersion++;
    }

    private async Task<AlertRule> FindRule(Guid id, CancellationToken ct) => await db.AlertRules.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw ApiError.NotFound("Alert rule");

    private async Task<AnalyticsAlert> Find(Guid id, CancellationToken ct) => await db.AnalyticsAlerts.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw ApiError.NotFound("Alert");

    private static void Match(AlertRule rule, string? token)
    {
        if (!rule.RowVersion.AsSpan().SequenceEqual(Input.RowVersion(token)))
            throw ApiError.Concurrency();
    }

    private static AlertRuleData Data(AlertRule x) => new(x.Id, new(x.SiteId, x.CameraId, x.CameraZoneId), x.Name, x.Kind, x.Severity,
        x.Threshold, x.MinimumObservedSeconds, x.RecoverySeconds, x.WindowSeconds, x.CooldownSeconds, x.MinimumCoveragePercent,
        x.AllowVideo, x.IsActive, x.RowVersion);

    private static AlertData Data(AnalyticsAlert x) => new(x.Id, x.AlertRuleId, new(x.SiteId, x.CameraId, x.CameraZoneId), x.SessionId,
        x.VideoLoop, x.Kind, x.Severity, x.ResolvedAtUtc.HasValue ? "Resolved" : x.AcknowledgedAtUtc.HasValue ? "Acknowledged" : "Active",
        x.OpenedAtUtc, x.LastObservedAtUtc, x.AcknowledgedAtUtc, x.AcknowledgedBy, x.ResolvedAtUtc, x.ResolvedBy, x.ResolutionReason,
        x.StateVersion, JsonSerializer.Deserialize<AlertEvidence>(x.EvidenceJson, Json)!);
}
