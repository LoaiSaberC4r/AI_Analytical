using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Contracts.Realtime;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Api.Hubs;

public interface IAnalyticsClient
{
    Task CameraSnapshotUpdated(CameraSnapshotUpdated message);
    Task ZoneMetricsUpdated(ZoneMetricsUpdated message);
    Task CameraStatusChanged(CameraStatusChanged message);
    Task AnalyticsSummaryUpdated(AnalyticsSummaryUpdated message);
    Task AnalyticsAlertChanged(AnalyticsAlertChanged message);
}

public sealed class AnalyticsHub(AnalyticsDb db, SiteAccess access) : Hub<IAnalyticsClient>
{
    public async Task SubscribeToSite(Guid siteId)
    {
        if (!access.Allows(Context.User, siteId))
            throw new HubException("Site access denied.");
        if (!await db.Sites.AnyAsync(x => x.Id == siteId, Context.ConnectionAborted))
            throw new HubException("Site was not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, SiteGroup(siteId), Context.ConnectionAborted);
    }

    public Task UnsubscribeFromSite(Guid siteId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, SiteGroup(siteId), Context.ConnectionAborted);

    public async Task SubscribeToCamera(Guid cameraId)
    {
        var site = await db.Cameras.Where(x => x.Id == cameraId).Select(x => (Guid?)x.SiteId)
            .SingleOrDefaultAsync(Context.ConnectionAborted);
        if (!site.HasValue || !access.Allows(Context.User, site.Value))
            throw new HubException("Camera is unavailable.");
        await Groups.AddToGroupAsync(Context.ConnectionId, CameraGroup(site.Value, cameraId), Context.ConnectionAborted);
    }

    public async Task UnsubscribeFromCamera(Guid cameraId)
    {
        var site = await db.Cameras.Where(x => x.Id == cameraId).Select(x => (Guid?)x.SiteId)
            .SingleOrDefaultAsync(Context.ConnectionAborted);
        if (site.HasValue)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, CameraGroup(site.Value, cameraId), Context.ConnectionAborted);
    }

    internal static string SiteGroup(Guid id) => $"site:{id:D}";
    internal static string CameraGroup(Guid site, Guid id) => $"camera:{site:D}:{id:D}";
}

public sealed class HubNotifications(IHubContext<AnalyticsHub, IAnalyticsClient> hub) : IAnalyticsNotifications
{
    public async Task AnalyticsCommitted(CommittedAnalytics message, CancellationToken ct)
    {
        var s = message.Camera;
        var target = Target(message.SiteId, s.CameraId);
        await target.CameraSnapshotUpdated(new(s.CameraId, message.SiteId, s.AnalyticsSessionId, s.LastSequence, s.StateVersion,
            s.SourceType, s.AnalyticsStatus, s.DataFreshness.ToString(), s.ProcessingFps, s.StaffCount, s.LastReceivedAtUtc)).WaitAsync(ct);

        foreach (var z in message.Zones)
            await target.ZoneMetricsUpdated(new(s.CameraId, z.CameraZoneId, s.AnalyticsSessionId, z.LastSequence, z.StateVersion,
                z.Occupancy, z.ZoneArrivals, z.ZoneDepartures, z.Entry, z.Exit, z.WrongWayIn, z.WrongWayOut,
                z.ReportedAverageDwellSeconds, z.ActiveVisitorsCount, z.LongestActiveDwellSeconds, z.LastUpdatedAtUtc)).WaitAsync(ct);

        if (message.StatusChanged)
            await FreshnessChanged(new(message.SiteId, s.CameraId, s.AnalyticsSessionId, s.StateVersion, s.AnalyticsStatus,
                s.DataFreshness, s.LastReceivedAtUtc), ct);
        await target.AnalyticsSummaryUpdated(new(s.CameraId, message.SiteId, s.AnalyticsSessionId,
            s.StateVersion, s.LastReceivedAtUtc)).WaitAsync(ct);
        AnalyticsTelemetry.RealtimeSignals.Add(1, new KeyValuePair<string, object?>("event", "summary"));
    }

    public Task FreshnessChanged(FreshnessChange message, CancellationToken ct) => Target(message.SiteId, message.CameraId)
        .CameraStatusChanged(new(message.CameraId, message.SiteId, message.SessionId, message.StateVersion,
            message.AnalyticsStatus, message.Freshness.ToString(), message.ReceivedAtUtc)).WaitAsync(ct);

    private IAnalyticsClient Target(Guid site, Guid camera) =>
        hub.Clients.Groups(AnalyticsHub.SiteGroup(site), AnalyticsHub.CameraGroup(site, camera));
}

public sealed class HubAlertNotifications(IHubContext<AnalyticsHub, IAnalyticsClient> hub, IOptions<SignalRSettings> settings) : IAlertNotifications
{
    public Task Changed(AlertData alert, CancellationToken ct) => !settings.Value.Enabled ? Task.CompletedTask
        : hub.Clients.Groups(AnalyticsHub.SiteGroup(alert.Scope.SiteId), AnalyticsHub.CameraGroup(alert.Scope.SiteId, alert.Scope.CameraId!.Value))
            .AnalyticsAlertChanged(new(alert.Id, alert.Scope.SiteId, alert.Scope.CameraId.Value, alert.StateVersion, alert.Status)).WaitAsync(ct);
}
