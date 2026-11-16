using System.Net.Http.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Contracts.Realtime;
using CameraAnalytics.Infrastructure.Mqtt;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace CameraAnalytics.IntegrationTests;

public sealed class RealtimeTests
{
    [SqlFact]
    public async Task SignalR_groups_receive_committed_versions_and_rejoin_after_disconnect()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, camera, zone) = await host.Register(client);
        await using var siteClient = Connection(host);
        await using var cameraClient = Connection(host);
        await using var outsider = Connection(host);
        await siteClient.StartAsync();
        await cameraClient.StartAsync();
        await outsider.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => outsider.InvokeAsync("SubscribeToCamera", Guid.NewGuid()));
        await siteClient.InvokeAsync("SubscribeToSite", site.Id);
        await cameraClient.InvokeAsync("SubscribeToCamera", camera.Id);
        var snapshot = NewCompletion<CameraSnapshotUpdated>();
        var metrics = NewCompletion<ZoneMetricsUpdated>();
        var status = NewCompletion<CameraStatusChanged>();
        var summary = NewCompletion<AnalyticsSummaryUpdated>();
        var leaking = NewCompletion<CameraSnapshotUpdated>();
        using var c1 = siteClient.On<CameraSnapshotUpdated>("CameraSnapshotUpdated", async item =>
        {
            var committed = await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{item.CameraId}/live");
            if (committed!.StateVersion >= item.StateVersion && committed.LastSequence == item.Sequence)
                snapshot.TrySetResult(item);
        });
        using var c2 = cameraClient.On<ZoneMetricsUpdated>("ZoneMetricsUpdated", item => { metrics.TrySetResult(item); });
        using var c3 = cameraClient.On<CameraStatusChanged>("CameraStatusChanged", item => { status.TrySetResult(item); });
        using var c4 = siteClient.On<AnalyticsSummaryUpdated>("AnalyticsSummaryUpdated", item => { summary.TrySetResult(item); });
        using var c5 = outsider.On<CameraSnapshotUpdated>("CameraSnapshotUpdated", item => { leaking.TrySetResult(item); });

        var first = host.Payload("signalr", 1);
        Assert.Equal(IngestResult.Accepted, await host.Ingest(first));
        var update = await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var z = await metrics.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(zone.Id, z.ZoneId);
        Assert.Equal(update.StateVersion, z.StateVersion);
        Assert.Equal("Fresh", (await status.Task.WaitAsync(TimeSpan.FromSeconds(8))).DataFreshness);
        Assert.Equal(update.StateVersion, (await summary.Task.WaitAsync(TimeSpan.FromSeconds(8))).StateVersion);
        Assert.False(leaking.Task.IsCompleted);
        var unexpected = NewCompletion<CameraSnapshotUpdated>();
        using var c6 = siteClient.On<CameraSnapshotUpdated>("CameraSnapshotUpdated", item => { unexpected.TrySetResult(item); });
        Assert.Equal(IngestResult.Duplicate, await host.Ingest(first));
        Assert.Equal(IngestResult.Rejected, await host.Ingest(host.Payload("signalr", 2, zoneId: "missing")));
        await Assert.ThrowsAsync<TimeoutException>(() => unexpected.Task.WaitAsync(TimeSpan.FromMilliseconds(400)));

        await cameraClient.InvokeAsync("UnsubscribeFromCamera", camera.Id);
        await cameraClient.StopAsync();
        await cameraClient.StartAsync();
        await cameraClient.InvokeAsync("SubscribeToCamera", camera.Id);
        var newer = NewCompletion<ZoneMetricsUpdated>();
        using var c7 = cameraClient.On<ZoneMetricsUpdated>("ZoneMetricsUpdated", item => { newer.TrySetResult(item); });
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("signalr", 3, occupancy: 7)));
        var updated = await newer.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(7, updated.Occupancy);
        Assert.True(updated.StateVersion > update.StateVersion);
        await siteClient.InvokeAsync("UnsubscribeFromSite", site.Id);
    }

    [BrokerFact]
    public async Task QoS1_delivery_is_retried_after_Sql_failure_using_the_persistent_id()
    {
        await using var host = new IntegrationHost(mqtt: true);
        using var client = host.CreateClient();
        var (_, camera, _) = await host.Register(client);
        var broker = host.Services.GetRequiredService<MqttConnectionStatus>();
        await IntegrationHost.Eventually(() => Task.FromResult(broker.Subscribed));
        using var publisher = new MqttClientFactory().CreateMqttClient();
        await publisher.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(IntegrationHost.BrokerHost!, IntegrationHost.BrokerPort).WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId("publisher-" + Guid.NewGuid().ToString("N")).Build());

        await host.InScope(db => db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE [ZoneSnapshots] ADD CONSTRAINT [CK_Test_Occupancy] CHECK ([Occupancy] <> 99);"));
        var bytes = host.Payload("broker", 1, occupancy: 99);
        await Publish(bytes);
        await IntegrationHost.Eventually(() => Task.FromResult(!broker.Subscribed));
        Assert.Equal(0, await host.InScope(db => db.ReceivedAnalyticsMessages.CountAsync()));
        await host.InScope(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE [ZoneSnapshots] DROP CONSTRAINT [CK_Test_Occupancy];"));
        // Publishing once is sufficient. The broker re-delivers the original unacknowledged QoS 1 message.
        await IntegrationHost.Eventually(() => host.InScope(db => db.CameraCurrentStates.AnyAsync(x => x.CameraId == camera.Id)), 20);
        Assert.True(broker.Subscribed);
        await Publish(bytes);
        await Publish("not JSON"u8.ToArray());
        await IntegrationHost.Eventually(() => host.InScope(db => db.IngestionRejections.AnyAsync()));
        Assert.Equal(1, await host.InScope(db => db.ReceivedAnalyticsMessages.CountAsync()));
        Assert.Equal(1, await host.InScope(db => db.ZoneSnapshots.CountAsync()));
        var live = await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{camera.Id}/live");
        Assert.Equal(99, live!.Zones.Single().Occupancy);

        Task<MqttClientPublishResult> Publish(byte[] payload) => publisher.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(host.Topic).WithPayload(payload).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
    }

    private static HubConnection Connection(IntegrationHost host) => new HubConnectionBuilder()
        .WithUrl(new Uri(host.Server.BaseAddress, "/hubs/analytics"), options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
        }).Build();

    private static TaskCompletionSource<T> NewCompletion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
