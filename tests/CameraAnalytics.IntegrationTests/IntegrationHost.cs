using System.Net.Http.Json;
using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CameraAnalytics.IntegrationTests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(IntegrationHost.SqlConnection))
            Skip = "Set CAMERA_ANALYTICS_TEST_SQL to a SQL Server connection with test database creation rights.";
    }
}

public sealed class BrokerFactAttribute : FactAttribute
{
    public BrokerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(IntegrationHost.SqlConnection) || string.IsNullOrWhiteSpace(IntegrationHost.BrokerHost))
            Skip = "Set CAMERA_ANALYTICS_TEST_SQL and CAMERA_ANALYTICS_TEST_MQTT_HOST for the real broker test.";
    }
}

internal sealed class IntegrationHost : WebApplicationFactory<Program>
{
    public static string? SqlConnection => Environment.GetEnvironmentVariable("CAMERA_ANALYTICS_TEST_SQL");
    public static string? BrokerHost => Environment.GetEnvironmentVariable("CAMERA_ANALYTICS_TEST_MQTT_HOST");
    public static int BrokerPort => int.TryParse(Environment.GetEnvironmentVariable("CAMERA_ANALYTICS_TEST_MQTT_PORT"), out var port) ? port : 1883;
    public string TestCamera { get; } = "test-" + Guid.NewGuid().ToString("N");
    public string Topic => $"cameras/{TestCamera}/analytics";
    public string ClientId { get; } = "ca-test-" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, string?> settings;
    private readonly string connection;

    public IntegrationHost(bool mqtt = false, bool aggregate = false, bool migrate = true, bool signalR = true)
    {
        connection = new SqlConnectionStringBuilder(SqlConnection!)
        {
            InitialCatalog = "CameraAnalytics_Test_" + Guid.NewGuid().ToString("N")
        }.ConnectionString;
        settings = new()
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Database:ApplyMigrationsOnStartup"] = migrate.ToString(),
            ["Mqtt:Enabled"] = mqtt.ToString(),
            ["Mqtt:ClientId"] = ClientId,
            ["Mqtt:TopicFilter"] = Topic,
            ["Mqtt:Host"] = BrokerHost ?? "127.0.0.1",
            ["Mqtt:Port"] = BrokerPort.ToString(),
            ["Mqtt:ReconnectDelaySeconds"] = "2",
            ["SignalR:Enabled"] = signalR.ToString(),
            ["Aggregation:Enabled"] = aggregate.ToString(),
            ["Aggregation:IntervalSeconds"] = "1",
            ["Analytics:StaleAfterSeconds"] = "3",
            ["Analytics:NoRecentDataAfterSeconds"] = "8",
            ["Analytics:TrackMissingGraceSeconds"] = "2"
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
    }

    public async Task<T> InScope<T>(Func<AnalyticsDb, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AnalyticsDb>());
    }

    public async Task<IngestResult> Ingest(byte[] json, string? topic = null)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAnalyticsIngestor>()
            .Process(topic ?? Topic, json, DateTime.UtcNow, default);
    }

    public async Task<(SiteData Site, CameraData Camera, ZoneData Zone)> Register(HttpClient client)
    {
        var site = await Post<SiteData>(client, "sites", new { name = "Main Mall", code = "MALL-001", timeZoneId = "Africa/Cairo" });
        var camera = await Post<CameraData>(client, "cameras", new { name = "Main Entrance Camera", siteId = site.Id, externalCameraId = TestCamera });
        var zone = await Post<ZoneData>(client, $"cameras/{camera.Id}/zones", new { name = "Reception", externalZoneId = "zone_01", capacity = 20 });
        return (site, camera, zone);
    }

    public static async Task<T> Post<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync("api/v1/" + path, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public byte[] Payload(string session, long seq, int occupancy = 2, long count = 1,
        DateTime? timestamp = null, string source = "video_file", string status = "running", string? zoneId = "zone_01",
        bool track = false, long? sourceMs = null, bool noPosition = false)
    {
        var zone = new
        {
            zone_id = zoneId, occupancy, zone_arrivals = count, zone_departures = 0, entry = count / 2, exit = 0,
            wrong_way_in = count / 3, wrong_way_out = count / 4, average_dwell_seconds = 13.3,
            active_visitors = track ? [new { track_id = 42, dwell_seconds = seq * 1d }] : Array.Empty<object>()
        };
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = "2.0", camera_id = TestCamera, source_type = source,
            source_name = source == "live" ? "entrance" : "1.mp4", session_id = session,
            source_time_ms = source == "video_file" && !noPosition ? sourceMs ?? seq * 1000 : (long?)null,
            processing_fps = 8, zones = zoneId is null ? [] : new[] { zone }, staff_count = occupancy / 2,
            status, sequence = seq, timestamp = timestamp ?? DateTime.UtcNow
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await using var db = new AnalyticsDb(new DbContextOptionsBuilder<AnalyticsDb>().UseSqlServer(connection).Options);
        await db.Database.EnsureDeletedAsync();
    }

    public static async Task Eventually(Func<Task<bool>> test, int seconds = 15)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!await test())
            await Task.Delay(100, timeout.Token);
    }
}
