using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Mqtt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Persistence;

public sealed class MonitoringQueries(AnalyticsDb db, TimeProvider clock, IOptions<AnalyticsOptions> options,
    IOptions<MqttOptions> mqtt, MqttConnectionStatus connection) : IMonitoringQueries
{
    public async Task<LiveSite> Site(Guid id, CancellationToken ct)
    {
        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound("Site");
        return new(site.Id, site.Name, site.IsActive, await Cameras(db.Cameras.AsNoTracking().Where(x => x.SiteId == id), ct));
    }

    public async Task<LiveCamera> Camera(Guid id, CancellationToken ct) =>
        (await Cameras(db.Cameras.AsNoTracking().Where(x => x.Id == id), ct)).SingleOrDefault()
            ?? throw ApiError.NotFound("Camera");

    public async Task<LiveZone> Zone(Guid id, CancellationToken ct)
    {
        var zone = await db.CameraZones.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiError.NotFound("Zone");
        var camera = await Camera(zone.CameraId, ct);
        return camera.Zones.Single(x => x.ZoneId == id);
    }

    public async Task<MonitoringStatus> Status(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await (from camera in db.Cameras.AsNoTracking()
            join state in db.CameraCurrentStates.AsNoTracking() on camera.Id equals state.CameraId into states
            from state in states.DefaultIfEmpty()
            select new { Active = camera.IsActive && camera.Site.IsActive, State = state }).ToListAsync(ct);
        var active = rows.Where(x => x.Active).Select(x => x.State).ToArray();
        var grouped = active.GroupBy(x => ObservationRules.Freshness(now, x?.LastReceivedAtUtc, options.Value))
            .ToDictionary(x => x.Key, x => x.Count());
        return new(rows.Count, active.Length, grouped.GetValueOrDefault(DataFreshness.Fresh), grouped.GetValueOrDefault(DataFreshness.Stale),
            grouped.GetValueOrDefault(DataFreshness.NoRecentData), grouped.GetValueOrDefault(DataFreshness.NeverReceived),
            active.Count(x => x?.AnalyticsStatus == "ended"), mqtt.Value.Enabled, connection.Subscribed, now);
    }

    private async Task<LiveCamera[]> Cameras(IQueryable<Camera> query, CancellationToken ct)
    {
        await using var snapshot = await WriteTransaction.Read(db, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var cameras = await (from camera in query
            join state in db.CameraCurrentStates.AsNoTracking() on camera.Id equals state.CameraId into states
            from state in states.DefaultIfEmpty()
            orderby camera.Name, camera.Id
            select new { Record = camera, Active = camera.IsActive && camera.Site.IsActive, State = state }).ToListAsync(ct);
        var zones = await (from zone in db.CameraZones.AsNoTracking()
            where query.Any(x => x.Id == zone.CameraId)
            join state in db.ZoneCurrentStates.AsNoTracking() on zone.Id equals state.CameraZoneId into states
            from state in states.DefaultIfEmpty()
            orderby zone.Name, zone.Id
            select new { Record = zone, State = state }).ToListAsync(ct);
        // Both queries must reflect the same committed versions during ingestion.
        await snapshot.CommitAsync(ct);
        var byCamera = zones.ToLookup(x => x.Record.CameraId);
        return cameras.Select(x =>
        {
            var s = x.State;
            return new LiveCamera(x.Record.Id, x.Record.SiteId, x.Record.ExternalCameraId, x.Record.Name, x.Active,
                s?.AnalyticsSessionId, s?.LastSequence, s?.SourceType, s?.AnalyticsStatus,
                ObservationRules.Freshness(now, s?.LastReceivedAtUtc, options.Value).ToString(),
                s?.ProcessingFps, s?.StaffCount, s?.LastReceivedAtUtc, s?.StateVersion,
                byCamera[x.Record.Id].Select(z => Zone(z.Record, z.State, s?.AnalyticsSessionId, x.Active, now)).ToArray());
        }).ToArray();
    }

    private LiveZone Zone(CameraZone zone, ZoneCurrentState? state, Guid? sessionId, bool cameraActive, DateTime now)
    {
        var utilization = ObservationRules.Utilization(state?.Occupancy, zone.Capacity);
        var capacityStatus = utilization switch
        {
            null => null,
            _ when utilization >= zone.CriticalThresholdPercent => "Critical",
            _ when utilization >= zone.WarningThresholdPercent => "Warning",
            _ => "WithinCapacity"
        };
        return new(zone.Id, zone.CameraId, zone.ExternalZoneId, zone.Name, zone.IsActive && cameraActive,
            state?.AnalyticsSessionId, state?.LastSequence, state?.StateVersion,
            state is not null && state.AnalyticsSessionId != sessionId,
            ObservationRules.Freshness(now, state?.LastUpdatedAtUtc, options.Value).ToString(),
            state?.Occupancy, state?.ZoneArrivals, state?.ZoneDepartures, state?.Entry, state?.Exit,
            state?.WrongWayIn, state?.WrongWayOut, state?.ReportedAverageDwellSeconds, state?.LongestActiveDwellSeconds,
            state?.ActiveVisitorsCount, zone.Capacity, zone.WarningThresholdPercent, zone.CriticalThresholdPercent,
            utilization, capacityStatus, state?.LastUpdatedAtUtc);
    }
}
