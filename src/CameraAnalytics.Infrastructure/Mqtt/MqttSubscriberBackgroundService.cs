using System.Buffers;
using System.Diagnostics;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Analytics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace CameraAnalytics.Infrastructure.Mqtt;

public sealed class MqttConnectionStatus
{
    private volatile bool subscribed;
    public bool Subscribed { get => subscribed; internal set => subscribed = value; }
}

public sealed class MqttSubscriberBackgroundService(IServiceScopeFactory scopes, IOptions<MqttOptions> settings,
    MqttConnectionStatus status, TimeProvider clock, ILogger<MqttSubscriberBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        if (!options.Enabled)
            return;

        var factory = new MqttClientFactory();
        var connectionOptions = new MqttClientOptionsBuilder()
            .WithClientId(options.ClientId)
            .WithTcpServer(options.Host, options.Port)
            .WithProtocolVersion(options.ProtocolVersion == "5.0" ? MqttProtocolVersion.V500 : MqttProtocolVersion.V311)
            .WithCleanSession(!options.PersistentSession)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(options.KeepAliveSeconds))
            .WithTimeout(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
        if (options.ProtocolVersion == "5.0" && options.PersistentSession)
            connectionOptions.WithSessionExpiryInterval(options.SessionExpirySeconds);
        if (options.Username is not null)
            connectionOptions.WithCredentials(options.Username, options.Password);
        if (options.UseTls)
            connectionOptions.WithTlsOptions(tls => tls.UseTls());

        var subscription = factory.CreateSubscribeOptionsBuilder().WithTopicFilter(filter =>
            filter.WithTopic(options.TopicFilter).WithQualityOfServiceLevel((MqttQualityOfServiceLevel)options.Qos)).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            using var client = factory.CreateMqttClient();
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var retry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            client.DisconnectedAsync += e =>
            {
                status.Subscribed = false;
                AnalyticsTelemetry.Connected.Record(0);
                AnalyticsTelemetry.Subscribed.Record(0);
                disconnected.TrySetResult();
                logger.LogInformation("MQTT disconnected with reason {Reason}", e.Reason);
                return Task.CompletedTask;
            };
            // MQTTnet awaits this handler before dispatching the next message. There is no application-side queue.
            client.ApplicationMessageReceivedAsync += async e =>
            {
                e.AutoAcknowledge = false;
                if (retry.Task.IsCompleted || stoppingToken.IsCancellationRequested)
                    return;

                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var payload = e.ApplicationMessage.Payload;
                    var bytes = payload.IsSingleSegment ? payload.First
                        : payload.Slice(0, Math.Min(payload.Length, (long)options.MaxPayloadBytes + 1)).ToArray();
                    await using var scope = scopes.CreateAsyncScope();
                    var handler = scope.ServiceProvider.GetRequiredService<IAnalyticsIngestor>();
                    var outcome = await handler.Process(e.ApplicationMessage.Topic, bytes, clock.GetUtcNow().UtcDateTime, stoppingToken);
                    var tag = new KeyValuePair<string, object?>("outcome", outcome.ToString());
                    AnalyticsTelemetry.Deliveries.Add(1, tag);
                    await e.AcknowledgeAsync(stoppingToken);
                    AnalyticsTelemetry.ReceiptToAck.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tag);
                    logger.LogDebug("MQTT message handled with outcome {Outcome}", outcome);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception e2)
                {
                    AnalyticsTelemetry.Retries.Add(1);
                    logger.LogWarning(e2, "MQTT message was left unacknowledged. Reconnecting to retry delivery.");
                    retry.TrySetResult();
                }
            };

            try
            {
                var result = await client.ConnectAsync(connectionOptions.Build(), stoppingToken);
                if (result.ResultCode != MqttClientConnectResultCode.Success)
                    throw new InvalidOperationException($"Broker rejected the MQTT connection ({result.ResultCode}).");
                AnalyticsTelemetry.Connected.Record(1);
                // Always verify the configured subscription. Replacing the same filter leaves queued QoS 1 deliveries intact.
                var subscribed = await client.SubscribeAsync(subscription, stoppingToken);
                var granted = subscribed.Items.Select(x => (int)x.ResultCode).SingleOrDefault(-1);
                if (granted < options.Qos || granted > 2)
                    throw new InvalidOperationException("Broker did not grant the configured MQTT subscription QoS.");
                status.Subscribed = client.IsConnected;
                AnalyticsTelemetry.Subscribed.Record(status.Subscribed ? 1 : 0);
                logger.LogInformation("MQTT subscribed to {Filter} with QoS {Qos} and session present {SessionPresent}",
                    options.TopicFilter, options.Qos, result.IsSessionPresent);
                await Task.WhenAny(disconnected.Task, retry.Task).WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception e)
            {
                logger.LogWarning(e, "MQTT subscriber could not connect or subscribe");
            }
            finally
            {
                status.Subscribed = false;
                AnalyticsTelemetry.Subscribed.Record(0);
                if (client.IsConnected)
                {
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
                    try { await client.DisconnectAsync(factory.CreateClientDisconnectOptionsBuilder().Build(), limit.Token); }
                    catch (Exception e) { logger.LogDebug(e, "MQTT disconnect did not complete normally"); }
                }
                AnalyticsTelemetry.Connected.Record(0);
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(options.ReconnectDelaySeconds), clock, stoppingToken);
        }
    }
}
