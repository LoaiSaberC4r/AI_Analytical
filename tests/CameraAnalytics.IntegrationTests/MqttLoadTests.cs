using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.Mqtt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Xunit.Abstractions;

namespace CameraAnalytics.IntegrationTests;

public sealed class LoadFactAttribute : FactAttribute
{
    public LoadFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CAMERA_ANALYTICS_RUN_LOAD") != "true"
            || IntegrationHost.SqlConnection is null || IntegrationHost.BrokerHost is null)
            Skip = "Run samples/measure-mqtt.ps1 with a disposable SQL database target and an isolated test broker.";
    }
}

public sealed class MqttLoadTests(ITestOutputHelper output)
{
    [LoadFact]
    [Trait("Category", "Load")]
    public async Task Real_QoS1_synthetic_multicamera_workload_reconciles_sources_and_rollups()
    {
        var cameras = Number("CAMERA_ANALYTICS_LOAD_CAMERAS", 2, 100);
        var zonesPerCamera = Number("CAMERA_ANALYTICS_LOAD_ZONES", 3, 20);
        var frames = Number("CAMERA_ANALYTICS_LOAD_SECONDS", 12, 300);
        await using var host = new IntegrationHost(mqtt: true, minute: true, wildcard: true);
        using var http = host.CreateClient();
        var site = await IntegrationHost.Post<SiteData>(http, "sites",
            new { name = "Synthetic MQTT load", code = "load-test", timeZoneId = "UTC" });
        var cameraIds = new List<CameraData>();
        var zones = Enumerable.Range(0, zonesPerCamera).Select(i => $"load-zone-{i}").ToArray();
        for (var i = 0; i < cameras; i++)
        {
            var externalCameraId = $"{host.TestCamera}-{i}";
            var camera = await IntegrationHost.Post<CameraData>(http, "cameras",
                new { name = $"Synthetic camera {i}", siteId = site.Id, externalCameraId });
            cameraIds.Add(camera);
            foreach (var externalZoneId in zones)
                await IntegrationHost.Post<ZoneData>(http, $"cameras/{camera.Id}/zones", new { name = externalZoneId, externalZoneId, capacity = 20 });
        }

        var broker = host.Services.GetRequiredService<MqttConnectionStatus>();
        await IntegrationHost.Eventually(() => Task.FromResult(broker.Subscribed));
        using var publisher = new MqttClientFactory().CreateMqttClient();
        await publisher.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(IntegrationHost.BrokerHost!, IntegrationHost.BrokerPort)
            .WithClientId("synthetic-" + Guid.NewGuid().ToString("N")).WithProtocolVersion(MqttProtocolVersion.V311).Build());

        var samples = new ConcurrentDictionary<string, ConcurrentQueue<double>>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == AnalyticsTelemetry.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(Key(instrument, tags), value));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Add(Key(instrument, tags), value));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(Key(instrument, tags), value));
        listener.Start();

        var started = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var session = "synthetic-load-" + Guid.NewGuid().ToString("N");
        var first = cameraIds[0];
        var read = $"/api/v1/analytics/traffic/flow-rate?siteId={site.Id}&cameraId={first.Id}&interval=minute";
        byte[]? lastFirstPayload = null;
        for (var sequence = 0; sequence < frames; sequence++)
        {
            var delay = TimeSpan.FromSeconds(sequence) - stopwatch.Elapsed;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
            var timestamp = DateTime.UtcNow;
            var i = sequence;
            await Task.WhenAll(cameraIds.Select(camera =>
            {
                var bytes = Payload(camera.ExternalCameraId, i, timestamp);
                if (camera.Id == first.Id) lastFirstPayload = bytes;
                return Publish(camera.MqttTopic, bytes);
            }));
            if (sequence % 3 == 0)
            {
                var before = Stopwatch.GetTimestamp();
                using var response = await http.GetAsync(read + $"&from={Uri.EscapeDataString(started.AddSeconds(-1).ToString("O"))}");
                response.EnsureSuccessStatusCode();
                Add("api.flow_ms", Stopwatch.GetElapsedTime(before).TotalMilliseconds);
            }
        }
        var expected = cameras * frames;
        await IntegrationHost.Eventually(() => Task.FromResult(Values("ca.mqtt.receipt_to_ack.Accepted").Length >= expected), 60);
        stopwatch.Stop();

        await Publish(first.MqttTopic, lastFirstPayload!);
        await Publish(first.MqttTopic, "invalid-json"u8.ToArray());
        await IntegrationHost.Eventually(() => Task.FromResult(Values("ca.mqtt.receipt_to_ack.Rejected").Length >= 1
            && Values("ca.mqtt.receipt_to_ack.Duplicate").Length >= 1));
        var expectedArrivals = (long)cameras * zonesPerCamera * (frames - 1);
        var persisted = await host.InScope(async db => (
            Accepted: await db.ReceivedAnalyticsMessages.CountAsync(x => x.ProcessingStatus == MessageStatus.Accepted),
            Deltas: await db.ZoneCounterDeltas.SumAsync(x => x.ZoneArrivalsDelta),
            Rejected: await db.IngestionRejections.CountAsync()));
        Assert.Equal(expected, persisted.Accepted);
        Assert.Equal(1, persisted.Rejected);
        Assert.Equal(expectedArrivals, persisted.Deltas);
        var sum = $"/api/v1/analytics/traffic?siteId={site.Id}&from={Uri.EscapeDataString(started.AddSeconds(-1).ToString("O"))}";
        var api = await http.GetFromJsonAsync<TrafficReport>(sum);
        Assert.Equal(expectedArrivals, api!.Totals.Arrivals);
        await IntegrationHost.Eventually(() => host.InScope(async db =>
            !await db.DirtyZoneMinutes.AnyAsync() && !await db.DirtyCameraMinutes.AnyAsync()
            && await db.ZoneMinuteAggregates.SumAsync(x => x.ArrivalsDelta) == expectedArrivals), 105);

        output.WriteLine("LOAD_RESULT " + JsonSerializer.Serialize(new
        {
            cameras, zonesPerCamera, frames, expected, elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
            acceptedPerSecondThroughDrain = expected / stopwatch.Elapsed.TotalSeconds,
            accepted = persisted.Accepted, rejected = persisted.Rejected, duplicate = Values("ca.mqtt.receipt_to_ack.Duplicate").Length,
            totalsReconciled = expectedArrivals, sourceToCommitMs = Quantiles("ca.ingest.source_to_commit"),
            receiptToCommitMs = Quantiles("ca.ingest.receipt_to_commit.Accepted"), receiptToAckMs = Quantiles("ca.mqtt.receipt_to_ack.Accepted"),
            minuteLagSeconds = Quantiles("ca.rollup.minute_lag"), maxSampledDirtyMinutes = Values("ca.rollup.dirty_minutes").DefaultIfEmpty().Max(),
            flowReadMs = Quantiles("api.flow_ms"), summarySignals = Values("ca.realtime.signals").Sum(),
        }));

        byte[] Payload(string cameraId, int seq, DateTime timestamp) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = "2.0", camera_id = cameraId, source_type = "live", source_name = "synthetic-mqtt-load",
            session_id = session, source_time_ms = (long?)null, processing_fps = 8, staff_count = 1, status = "running", sequence = seq, timestamp,
            zones = zones.Select(zone_id => new { zone_id, occupancy = seq % 12, zone_arrivals = seq, zone_departures = seq / 2,
                entry = seq, exit = seq / 2, wrong_way_in = seq / 10, wrong_way_out = 0, average_dwell_seconds = 0, active_visitors = Array.Empty<object>() }),
        });

        async Task Publish(string topic, byte[] bytes)
        {
            var result = await publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(bytes)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
            Assert.True(result.IsSuccess);
        }

        void Add(string key, double value) => samples.GetOrAdd(key, _ => new()).Enqueue(value);
        double[] Values(string key) => samples.TryGetValue(key, out var series) ? series.ToArray() : [];
        object Quantiles(string key)
        {
            var values = Values(key).Order().ToArray();
            double? At(double quantile) => values.Length == 0 ? null : values[(int)Math.Ceiling(values.Length * quantile) - 1];
            return new { count = values.Length, p50 = At(0.5), p95 = At(0.95), p99 = At(0.99) };
        }
    }

    private static string Key(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
            if (tag.Key == "outcome") return instrument.Name + "." + tag.Value;
        return instrument.Name;
    }

    private static int Number(string name, int fallback, int limit) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
        && value >= 2 && value <= limit ? value : fallback;
}
