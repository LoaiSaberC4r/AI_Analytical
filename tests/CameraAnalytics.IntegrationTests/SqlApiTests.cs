using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.IntegrationTests;

public sealed class SqlApiTests
{
    [SqlFact]
    public async Task Migrations_catalog_uniqueness_row_version_cors_and_OpenAPI()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, camera, zone) = await host.Register(client);
        Assert.Equal(host.Topic, camera.MqttTopic);
        Assert.Equal(8, site.RowVersion.Length);
        Assert.Equal(80, zone.WarningThresholdPercent);
        using var duplicateCamera = await client.PostAsJsonAsync("api/v1/cameras",
            new { name = "Repeat", siteId = site.Id, externalCameraId = host.TestCamera });
        Assert.Equal(HttpStatusCode.Conflict, duplicateCamera.StatusCode);
        using var duplicateZone = await client.PostAsJsonAsync($"api/v1/cameras/{camera.Id}/zones",
            new { name = "Repeat", externalZoneId = zone.ExternalZoneId });
        Assert.Equal(HttpStatusCode.Conflict, duplicateZone.StatusCode);
        var second = await IntegrationHost.Post<CameraData>(client, "cameras",
            new { name = "Second Camera", siteId = site.Id, externalCameraId = host.TestCamera + "-other" });
        _ = await IntegrationHost.Post<ZoneData>(client, $"cameras/{second.Id}/zones",
            new { name = "Same Zone Key", externalZoneId = zone.ExternalZoneId });
        using var update = await client.PatchAsJsonAsync($"api/v1/sites/{site.Id}/status",
            new { isActive = false, rowVersion = site.RowVersion });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var stale = await client.PutAsJsonAsync($"api/v1/sites/{site.Id}",
            new { name = "New name", site.Code, site.TimeZoneId, rowVersion = site.RowVersion });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var never = await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{camera.Id}/live");
        Assert.False(never!.IsActive);
        Assert.Null(never.Zones[0].Occupancy);

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "hubs/analytics/negotiate");
        preflight.Headers.Add("Origin", "http://localhost:4200");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        using var cors = await client.SendAsync(preflight);
        Assert.Equal("http://localhost:4200", Assert.Single(cors.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/v1/health/ready")).StatusCode);
        var openApi = await client.GetFromJsonAsync<JsonElement>("swagger/v1/swagger.json");
        Assert.Equal(37, openApi.GetProperty("paths").EnumerateObject().Sum(path =>
            path.Value.EnumerateObject().Count(verb => verb.Name is "get" or "post" or "put" or "patch")));
    }

    [SqlFact]
    public async Task No_startup_migration_and_disabled_hub_keep_readiness_and_liveness_distinct()
    {
        await using var host = new IntegrationHost(migrate: false, signalR: false);
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/v1/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("api/v1/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("hubs/analytics/negotiate", null)).StatusCode);
    }

    [SqlFact]
    public async Task Ordered_ingestion_deduplication_resets_omissions_and_archived_sessions()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, camera, zone) = await host.Register(client);
        var at = DateTime.UtcNow.AddMinutes(-2);
        var first = host.Payload("one", 1, occupancy: 2, count: 10, timestamp: at, track: true);
        Assert.Equal(IngestResult.Accepted, await host.Ingest(first));
        Assert.Equal(IngestResult.Duplicate, await host.Ingest(first));
        Assert.Equal(IngestResult.Rejected, await host.Ingest(host.Payload("one", 1, timestamp: at)));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("one", 2, occupancy: 6, count: 15, timestamp: at.AddSeconds(1))));
        Assert.Equal(IngestResult.HistoricalOnly, await host.Ingest(host.Payload("one", 0, count: 0, timestamp: at.AddSeconds(-1))));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("one", 3, occupancy: 4, count: 2, timestamp: at.AddSeconds(2))));
        Assert.Equal(IngestResult.Rejected, await host.Ingest(host.Payload("one", 4, zoneId: "unregistered", timestamp: at.AddSeconds(3))));
        Assert.Equal(IngestResult.Rejected, await host.Ingest("bad json"u8.ToArray()));
        var live = await client.GetFromJsonAsync<LiveSite>($"api/v1/sites/{site.Id}/live");
        Assert.Equal(3, live!.Cameras.Single().LastSequence);
        Assert.Equal(4, live.Cameras.Single().Zones.Single().Occupancy);
        Assert.Equal(20, live.Cameras.Single().Zones.Single().UtilizationPercent);
        var video = await client.GetFromJsonAsync<SessionReport>($"api/v1/analytics/sessions/{live.Cameras[0].SessionId}/report");
        Assert.Equal("sourceTime", video!.Analytics.Period.TimeBasis);
        var metrics = Assert.Single(video.Analytics.Zones);
        Assert.Equal(5, metrics.Traffic.Arrivals);
        Assert.Equal(4, metrics.AverageOccupancy);
        Assert.Equal(6, metrics.PeakOccupancy);
        Assert.Equal(2000, metrics.PeakAtSourceTimeMs);
        Assert.Null(metrics.PeakAtUtc);
        Assert.Equal(1, metrics.DataQuality.CounterResetCount);
        Assert.Equal(2, metrics.DataQuality.ObservationDurationSeconds);
        Assert.Equal(3, metrics.DataQuality.SampleCount);

        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("two", 1, zoneId: null, timestamp: at.AddSeconds(10))));
        Assert.Equal(IngestResult.HistoricalOnly, await host.Ingest(host.Payload("one", 4, timestamp: at.AddSeconds(11))));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("two", 2, timestamp: at.AddSeconds(12), count: 40)));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("two", 3, timestamp: at.AddSeconds(13), status: "ended", count: 41)));
        Assert.Equal(IngestResult.HistoricalOnly, await host.Ingest(host.Payload("two", 4, timestamp: at.AddSeconds(14))));
        var final = await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{camera.Id}/live");
        Assert.Equal("ended", final!.AnalyticsStatus);
        Assert.Equal(3, final.LastSequence);
        Assert.Equal(5, await host.InScope(db => db.ZoneCounterDeltas.CountAsync()));
        Assert.Equal(9, await host.InScope(db => db.CameraSnapshots.CountAsync()));
        Assert.Equal(8, await host.InScope(db => db.ZoneSnapshots.CountAsync()));
        Assert.Equal(VisitStatus.Uncertain, await host.InScope(db => db.TrackedVisits.Select(x => x.Status).SingleAsync()));
        Assert.Equal(3, await host.InScope(db => db.IngestionRejections.CountAsync()));

        var unknown = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(host.Payload("foo", 1)).Replace(host.TestCamera, "no-camera"));
        Assert.Equal(IngestResult.Rejected, await host.Ingest(unknown, "cameras/no-camera/analytics"));
        Assert.Contains("unknown_camera", await host.InScope(db => db.IngestionRejections.Select(x => x.Reason).ToListAsync()));
        using var disable = await client.PatchAsJsonAsync($"api/v1/cameras/{camera.Id}/status", new { isActive = false, camera.RowVersion });
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        Assert.Equal(IngestResult.Rejected, await host.Ingest(host.Payload("three", 1)));
        Assert.Contains("inactive_camera_or_site", await host.InScope(db => db.IngestionRejections.Select(x => x.Reason).ToListAsync()));
    }

    [SqlFact]
    public async Task Failed_snapshot_write_rolls_back_session_marker_state_and_history()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        await host.Register(client);
        await host.InScope(db => db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE [ZoneSnapshots] ADD CONSTRAINT [CK_Test_Occupancy] CHECK ([Occupancy] <> 99);"));
        var payload = host.Payload("rollback", 1, occupancy: 99);
        await Assert.ThrowsAsync<DbUpdateException>(() => host.Ingest(payload));
        Assert.Equal(0, await host.InScope(db => db.AnalyticsSessions.CountAsync()));
        Assert.Equal(0, await host.InScope(db => db.ReceivedAnalyticsMessages.CountAsync()));
        Assert.Equal(0, await host.InScope(db => db.CameraSnapshots.CountAsync()));
        Assert.Equal(0, await host.InScope(db => db.CameraCurrentStates.CountAsync()));

        await host.InScope(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE [ZoneSnapshots] DROP CONSTRAINT [CK_Test_Occupancy];"));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(payload));
        Assert.Equal(IngestResult.Duplicate, await host.Ingest(payload));
    }

    [SqlFact]
    public async Task Closed_live_hours_are_rebuilt_without_double_counting()
    {
        await using var host = new IntegrationHost(aggregate: true);
        using var client = host.CreateClient();
        var (site, _, zone) = await host.Register(client);
        var hour = DateTime.UtcNow.Date.AddHours(Math.Max(0, DateTime.UtcNow.Hour - 2)).AddDays(-1);
        var first = host.Payload("live-one", 1, occupancy: 3, count: 6, timestamp: hour, source: "live");
        await host.Ingest(first);
        await IntegrationHost.Eventually(() => host.InScope(db => db.ZoneHourlyAggregates.AnyAsync(x => x.CameraZoneId == zone.Id)));
        await host.Ingest(host.Payload("live-one", 2, occupancy: 5, count: 9, timestamp: hour.AddSeconds(1), source: "live", status: "ended"));
        await IntegrationHost.Eventually(() => host.InScope(db => db.ZoneHourlyAggregates.AnyAsync(x => x.ArrivalsDelta == 3)));
        Assert.Equal(IngestResult.Duplicate, await host.Ingest(first));
        var q = $"siteId={site.Id}&from={Uri.EscapeDataString(hour.ToString("O"))}&to={Uri.EscapeDataString(hour.AddHours(1).ToString("O"))}";
        var report = await client.GetFromJsonAsync<ReportResult>($"api/v1/analytics/zones/{zone.Id}/timeseries?{q}");
        var values = Assert.Single(report!.Zones);
        Assert.Equal(3, values.Traffic.Arrivals);
        Assert.Equal(3, values.AverageOccupancy);
        Assert.Equal(5, values.PeakOccupancy);
        Assert.Equal(1, values.DataQuality.ObservationDurationSeconds);
        Assert.Equal(1, await host.InScope(db => db.ZoneHourlyAggregates.CountAsync()));
    }

    [SqlFact]
    public async Task Video_without_source_positions_is_persisted_and_timeline_keeps_the_gap()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (_, camera, _) = await host.Register(client);
        var received = DateTime.UtcNow.AddMinutes(-1);
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("untimed", 1, noPosition: true, timestamp: received)));
        var current = await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{camera.Id}/live");
        Assert.Equal(1, current!.LastSequence);
        var path = $"api/v1/analytics/sessions/{current.SessionId}/report";
        var untimed = await client.GetFromJsonAsync<SessionReport>(path);
        Assert.Equal(1, untimed!.UntimedVideoObservations);
        Assert.Null(untimed.Analytics.Period.FromSourceTimeMs);
        Assert.Equal("InsufficientData", untimed.Analytics.Zones.Single().DataQuality.Kind);
        await host.Ingest(host.Payload("untimed", 2, timestamp: received.AddSeconds(1), sourceMs: 5000));
        await host.Ingest(host.Payload("untimed", 3, timestamp: received.AddSeconds(2), noPosition: true));
        await host.Ingest(host.Payload("untimed", 4, timestamp: received.AddSeconds(3), sourceMs: 7000));
        var partial = await client.GetFromJsonAsync<SessionReport>(path);
        Assert.Equal(2, partial!.UntimedVideoObservations);
        Assert.Equal(5000, partial.Session.FirstSourceTimeMs);
        Assert.Equal(7000, partial.Session.LastSourceTimeMs);
        Assert.Equal(2, partial.Analytics.Zones.Single().DataQuality.SampleCount);
        Assert.Equal(0, partial.Analytics.Zones.Single().DataQuality.ObservationDurationSeconds);
    }

    [SqlFact]
    public async Task Concurrent_live_reads_keep_camera_and_zone_observation_versions_consistent()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (_, camera, _) = await host.Register(client);
        var time = DateTime.UtcNow;

        var writes = Publish();
        var reads = Read();
        await Task.WhenAll(writes, reads);

        async Task Publish()
        {
            for (var sequence = 1; sequence <= 10; sequence++)
                Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("concurrent", sequence,
                    timestamp: time.AddMilliseconds(sequence))));
        }

        async Task Read()
        {
            for (var round = 0; round < 10; round++)
            {
                var latest = await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{camera.Id}/live");
                Assert.Equal(latest!.LastSequence, latest.Zones.Single().Sequence);
            }
        }
    }
}
