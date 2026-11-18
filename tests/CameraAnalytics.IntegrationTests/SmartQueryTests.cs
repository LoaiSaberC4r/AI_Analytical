using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace CameraAnalytics.IntegrationTests;

public sealed class SmartQueryTests
{
    [SqlFact]
    public async Task Retention_preserves_pending_minute_evidence_and_original_message_identity()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (_, _, zone) = await host.Register(client);
        var old = DateTime.UtcNow.AddDays(-3).Date.AddHours(1);
        var first = host.Payload("retained-identity", 1, timestamp: old, source: "live");
        var last = host.Payload("retained-identity", 2, count: 2, timestamp: old.AddSeconds(1), source: "live", status: "ended");
        await host.Ingest(first, receivedAt: old);
        await host.Ingest(last, receivedAt: old.AddSeconds(1));
        await host.InScope(async db =>
        {
            db.DirtyZoneMinutes.Add(new() { CameraZoneId = zone.Id, MinuteStartUtc = old, DirtyAtUtc = old });
            return await db.SaveChangesAsync();
        });
        await using var retentionHost = host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aggregation:Enabled"] = "true", ["Retention:Enabled"] = "true", ["Retention:RawSnapshotsDays"] = "1"
            })));
        using var retainedClient = retentionHost.CreateClient();
        await IntegrationHost.Eventually(() => host.InScope(db => db.ZoneHourlyAggregates.AnyAsync(x => x.CameraZoneId == zone.Id)));
        Assert.Equal(2, await host.InScope(db => db.ZoneSnapshots.CountAsync(x => x.CameraZoneId == zone.Id)));
        await host.InScope(db => db.DirtyZoneMinutes.Where(x => x.CameraZoneId == zone.Id).ExecuteDeleteAsync());
        await IntegrationHost.Eventually(() => host.InScope(async db => !await db.ZoneSnapshots.AnyAsync(x => x.CameraZoneId == zone.Id)));
        Assert.Equal(IngestResult.Duplicate, await host.Ingest(first));
        Assert.Equal(2, await host.InScope(db => db.ReceivedAnalyticsMessages.CountAsync()));
    }

    [SqlFact]
    public async Task Queue_batch_failure_rolls_back_ordered_receipt_snapshots_and_counter_facts()
    {
        await using var host = new IntegrationHost(minute: true);
        using var client = host.CreateClient();
        await host.Register(client);
        await host.InScope(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE DirtyCameraMinutes ADD CONSTRAINT CK_Test_DirtyCamera CHECK (1=0)"));
        var payload = host.Payload("queue-failure", 1, source: "live");
        await Assert.ThrowsAsync<SqlException>(() => host.Ingest(payload));
        Assert.True(await host.InScope(async db => !await db.ReceivedAnalyticsMessages.AnyAsync() && !await db.CameraSnapshots.AnyAsync()
            && !await db.ZoneCounterDeltas.AnyAsync() && !await db.DirtyZoneMinutes.AnyAsync() && !await db.DirtyZoneHours.AnyAsync()));
        await host.InScope(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE DirtyCameraMinutes DROP CONSTRAINT CK_Test_DirtyCamera"));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(payload));
    }

    [SqlFact]
    public async Task Backfill_discovers_retained_history_and_reuses_its_cursor_after_restart()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (_, _, zone) = await host.Register(client);
        var from = DateTime.UtcNow.AddDays(-1).AddMinutes(3);
        await host.Ingest(host.Payload("backfill", 1, count: 6, timestamp: from, source: "live"));
        await host.Ingest(host.Payload("backfill", 2, count: 8, timestamp: from.AddSeconds(1), source: "live"));
        Assert.False(await host.InScope(db => db.DirtyZoneMinutes.AnyAsync()));
        await using var restarted = host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinuteAggregation:Enabled"] = "true", ["MinuteAggregation:BackfillDays"] = "1"
            })));
        using var newClient = restarted.CreateClient();
        await IntegrationHost.Eventually(() => host.InScope(db => db.ZoneMinuteAggregates.AnyAsync(x => x.CameraZoneId == zone.Id && x.ArrivalsDelta == 2)));
        var cursor = await host.InScope(db => db.MinuteBackfills.AsNoTracking().SingleAsync());
        Assert.True(cursor.NextMinuteUtc > cursor.EndUtc.AddDays(-1));
    }

    [SqlFact]
    public async Task Forecast_uses_clean_persisted_hourly_rollups_and_excludes_dirty_history()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, _, zone) = await host.Register(client, "UTC");
        var now = DateTime.UtcNow;
        var target = now.Date.AddHours(now.Hour + 1);
        await host.InScope(async db =>
        {
            db.ZoneHourlyAggregates.AddRange(Enumerable.Range(1, 28).Select(day => new ZoneHourlyAggregate
            {
                CameraZoneId = zone.Id, SourceType = "live", HourStartUtc = target.AddDays(-day), AverageOccupancy = day,
                ObservationDurationSeconds = 3600, CoveragePercent = 100, SampleCount = 3600, DataQuality = "Derived", UpdatedAtUtc = now
            }));
            return await db.SaveChangesAsync();
        });
        var path = $"api/v1/analytics/forecast?siteId={site.Id}&zoneId={zone.Id}";
        var forecast = await client.GetFromJsonAsync<ForecastReport>(path);
        Assert.Equal(28, forecast!.QualifiedHistoryDays);
        Assert.Contains(forecast.Points, point => point.TargetStartUtc == target && point.Prediction == 17.5);
        await host.InScope(async db =>
        {
            db.DirtyZoneHours.Add(new() { CameraZoneId = zone.Id, SourceType = "live", HourStartUtc = target.AddDays(-28), DirtyAtUtc = now });
            return await db.SaveChangesAsync();
        });
        var incomplete = await client.GetFromJsonAsync<ForecastReport>(path);
        Assert.Equal("InsufficientData", incomplete!.Provenance);
        Assert.Equal(27, incomplete.QualifiedHistoryDays);
        Assert.All(incomplete.Points, point => Assert.Null(point.Prediction));
    }
    [SqlFact]
    public async Task Congestion_progress_survives_ingest_scopes_and_persists_one_recovered_episode()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, _, zone) = await host.Register(client);
        var start = DateTime.UtcNow.AddMinutes(-3);
        byte[] payload = [];
        for (var index = 0; index < 10; index++)
        {
            payload = host.Payload("congestion", index, index < 6 ? 25 : 9, timestamp: start.AddSeconds(index * 5), source: "live");
            Assert.Equal(IngestResult.Accepted, await host.Ingest(payload));
        }
        Assert.Equal(IngestResult.Duplicate, await host.Ingest(payload));
        var from = Uri.EscapeDataString(start.ToString("O"));
        var to = Uri.EscapeDataString(start.AddMinutes(1).ToString("O"));
        var history = await client.GetFromJsonAsync<CongestionHistory>(
            $"api/v1/analytics/congestion/history?siteId={site.Id}&zoneId={zone.Id}&from={from}&to={to}");
        var episode = Assert.Single(history!.Episodes.Items);
        Assert.Equal("observed_recovery", episode.ResolutionReason);
        Assert.Equal(30, episode.AboveCriticalSeconds);
        Assert.Equal(100, episode.Quality.CoveragePercent);
    }
    [SqlFact]
    public async Task Visualization_contract_enforces_metric_chart_whitelist_and_keeps_video_axis_separate()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, _, zone) = await host.Register(client);
        await host.Ingest(host.Payload("video", 1, videoLoop: 2));
        await host.Ingest(host.Payload("video", 2, count: 3, videoLoop: 2));
        var session = await host.InScope(db => db.AnalyticsSessions.Select(x => x.Id).SingleAsync());
        var path = $"api/v1/analytics/visualizations/arrivals?siteId={site.Id}&zoneId={zone.Id}&sessionId={session}&sourceType=video_file";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
        path += "&videoLoop=2&fromSourceTimeMs=0&toSourceTimeMs=3000";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "&visualizationType=histogram")).StatusCode);
        using var json = JsonDocument.Parse(await client.GetStringAsync(path + "&visualizationType=area"));
        Assert.Equal("area", json.RootElement.GetProperty("visualizationType").GetString());
        Assert.Equal("sourceTime", json.RootElement.GetProperty("timeBasis").GetString());
        var point = json.RootElement.GetProperty("series")[0].GetProperty("points")[0];
        Assert.Equal(2, point.GetProperty("value").GetInt32());
        Assert.Equal(JsonValueKind.Null, point.GetProperty("period").GetProperty("startUtc").ValueKind);
        using var swagger = JsonDocument.Parse(await client.GetStringAsync("swagger/v1/swagger.json"));
        Assert.True(swagger.RootElement.GetProperty("paths").TryGetProperty("/api/v1/analytics/visualizations/{metricKey}", out _));
    }

    [SqlFact]
    public async Task Late_ordered_sample_repairs_closed_minute_once_and_matches_hour_and_api()
    {
        await using var host = new IntegrationHost(aggregate: true, minute: true);
        using var client = host.CreateClient();
        var (site, camera, zone) = await host.Register(client);
        var hour = DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour - 2);
        var start = hour.AddMinutes(1);
        var first = host.Payload("live", 1, 8, 0, start.AddSeconds(58), "live");
        var second = host.Payload("live", 2, 10, 3, start.AddSeconds(60.5), "live");
        Assert.Equal(IngestResult.Accepted, await host.Ingest(first));
        await IntegrationHost.Eventually(() => host.InScope(db => db.ZoneMinuteAggregates.AnyAsync(x =>
            x.CameraZoneId == zone.Id && x.MinuteStartUtc == start && x.ObservationDurationSeconds == 0)));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(second));
        Assert.Equal(IngestResult.Duplicate, await host.Ingest(second));
        await IntegrationHost.Eventually(() => host.InScope(db => db.ZoneMinuteAggregates.AnyAsync(x =>
            x.CameraZoneId == zone.Id && x.MinuteStartUtc == start && x.ObservationDurationSeconds == 2)));
        await IntegrationHost.Eventually(() => host.InScope(db => db.CameraMinuteAggregates.AnyAsync(x =>
            x.CameraId == camera.Id && x.MinuteStartUtc == start.AddMinutes(1) && x.ObservationDurationSeconds == 0.5)));
        await IntegrationHost.Eventually(() => host.InScope(db => db.ZoneHourlyAggregates.AnyAsync(x =>
            x.CameraZoneId == zone.Id && x.HourStartUtc == hour && x.ArrivalsDelta == 3)));
        var sourceAndRollup = await host.InScope(async db => (
            await db.ZoneCounterDeltas.Where(x => x.CameraZoneId == zone.Id).SumAsync(x => x.ZoneArrivalsDelta),
            await db.ZoneMinuteAggregates.Where(x => x.CameraZoneId == zone.Id).SumAsync(x => x.ArrivalsDelta)));
        Assert.Equal(sourceAndRollup.Item1, sourceAndRollup.Item2);
        await using var scope = host.Services.CreateAsyncScope();
        var reports = scope.ServiceProvider.GetRequiredService<ISmartQueries>();
        var flow = Assert.Single((await reports.Flow(new(SiteId: site.Id, ZoneId: zone.Id,
            From: start, To: start.AddMinutes(2), Interval: "minute"), default)).Zones);
        Assert.Equal(3, flow.Total.ObservedCounts!.Arrivals);
        Assert.Equal(new[] { 2d, 0.5 }, flow.Buckets.Select(x => x.Quality.CoveredSeconds));
    }

    [SqlFact]
    public async Task Forward_migrations_preserve_catalog_rows_from_reviewed_schema()
    {
        await using var host = new IntegrationHost(migrate: false);
        using var client = host.CreateClient();
        await host.InScope(async db =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20261116172719_VideoLoopOrdering");
            return true;
        });
        var (_, _, zone) = await host.Register(client);
        Assert.True(await host.InScope(async db =>
        {
            await db.Database.MigrateAsync();
            return await db.CameraZones.AnyAsync(z => z.Id == zone.Id && z.Capacity == 20);
        }));
    }

    [SqlFact]
    public async Task Ordered_facts_preserve_capacity_and_camera_quality_across_catalog_edits()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, camera, zone) = await host.Register(client);
        var start = DateTime.UtcNow.AddMinutes(-2);
        await host.Ingest(host.Payload("live", 1, 20, 5, start, "live"));
        await host.Ingest(host.Payload("live", 2, 25, 6, start.AddSeconds(1), "live"));
        await host.Ingest(host.Payload("live", 4, 15, 8, start.AddSeconds(3), "live"));
        await host.Ingest(host.Payload("live", 3, 500, 50, start.AddSeconds(2), "live"));
        await host.InScope(async db =>
        {
            (await db.CameraZones.FindAsync(zone.Id))!.Capacity = 100;
            return await db.SaveChangesAsync();
        });
        await using var scope = host.Services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<ISmartQueries>();
        var filter = new ReportFilter(SiteId: site.Id, ZoneId: zone.Id, From: start, To: start.AddSeconds(5), Interval: "minute");
        var flow = Assert.Single((await queries.Flow(filter, default)).Zones);
        Assert.Equal(3, flow.Total.ObservedCounts!.Arrivals);
        Assert.Equal(25, flow.Total.PeakOccupancy);
        Assert.Null(flow.Total.EventsPerMinute);
        var congestion = Assert.Single((await queries.Congestion(filter, default)).Zones);
        Assert.Equal(125, congestion.PeakUtilizationPercent);
        Assert.Equal(2, congestion.AboveWarningSeconds);
        Assert.Equal(3, Assert.Single((await queries.Dwell(filter, default)).Zones).CompletedQuality.SampleCount);
        var quality = Assert.Single((await queries.Quality(filter with { ZoneId = null, CameraId = camera.Id }, default)).Cameras);
        Assert.Equal(1, quality.Buckets.Sum(x => x.MissingSequences));
        Assert.All(quality.Buckets.Where(x => x.AverageFps.HasValue), x => Assert.Equal(8, x.AverageFps));
    }
}
