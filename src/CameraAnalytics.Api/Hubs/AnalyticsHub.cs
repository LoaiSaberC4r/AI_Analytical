using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Contracts.Realtime;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Api.Hubs;

public interface IAnalyticsClient
{
    Task CameraSnapshotUpdated(CameraSnapshotUpdated message);
    Task ZoneMetricsUpdated(ZoneMetricsUpdated message);
    Task CameraStatusChanged(CameraStatusChanged message);
    Task AnalyticsSummaryUpdated(AnalyticsSummaryUpdated message);
}

public sealed class AnalyticsHub(AnalyticsDb db) : Hub<IAnalyticsClient>
{
    public async Task SubscribeToSite(Guid siteId)
    {
        if (!await db.Sites.AnyAsync(x => x.Id == siteId, Context.ConnectionAborted))
            throw new HubException("Site was not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, SiteGroup(siteId), Context.ConnectionAborted);
    }

    public Task UnsubscribeFromSite(Guid siteId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, SiteGroup(siteId), Context.ConnectionAborted);

    public async Task SubscribeToCamera(Guid cameraId)
    {
        if (!await db.Cameras.AnyAsync(x => x.Id == cameraId, Context.ConnectionAborted))
            throw new HubException("Camera was not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, CameraGroup(cameraId), Context.ConnectionAborted);
    }

    public Task UnsubscribeFromCamera(Guid cameraId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CameraGroup(cameraId), Context.ConnectionAborted);

    internal static string SiteGroup(Guid id) => $"site:{id:D}";
    internal static string CameraGroup(Guid id) => $"camera:{id:D}";
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
    }

    public Task FreshnessChanged(FreshnessChange message, CancellationToken ct) => Target(message.SiteId, message.CameraId)
        .CameraStatusChanged(new(message.CameraId, message.SiteId, message.SessionId, message.StateVersion,
            message.AnalyticsStatus, message.Freshness.ToString(), message.ReceivedAtUtc)).WaitAsync(ct);

    private IAnalyticsClient Target(Guid site, Guid camera) =>
        hub.Clients.Groups(AnalyticsHub.SiteGroup(site), AnalyticsHub.CameraGroup(camera));
}
