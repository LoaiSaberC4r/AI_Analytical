using System.Net.Http.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Infrastructure.Analytics;
using Microsoft.Extensions.DependencyInjection;

namespace CameraAnalytics.IntegrationTests;

public sealed class AlertTests
{
    [SqlFact]
    public async Task Sustained_rules_open_from_covered_ordered_observations_with_qualified_evidence()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, camera, zone) = await host.Register(client);
        var configured = new (string Kind, Guid? Zone, double Threshold)[]
        {
            ("Congestion", zone.Id, 100), ("LongStay", zone.Id, 5), ("WrongWaySpike", zone.Id, 3), ("LowFps", null, 9)
        };
        var rules = new List<AlertRuleData>();
        foreach (var (kind, zoneId, threshold) in configured)
            rules.Add(await IntegrationHost.Post<AlertRuleData>(client, "alert-rules", new SaveAlertRule(site.Id, camera.Id, zoneId,
                kind, kind, "Warning", threshold, MinimumObservedSeconds: 2, RecoverySeconds: 2, WindowSeconds: 10)));
        var origin = DateTime.UtcNow.AddSeconds(-30);
        for (var i = 1; i <= 16; i++)
        {
            var timestamp = origin.AddSeconds(i);
            await host.Ingest(host.Payload("sustained", i, occupancy: 25, count: i * 12, timestamp: timestamp, source: "live", track: true));
        }
        var evaluation = origin.AddSeconds(17);
        foreach (var rule in rules)
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AlertEvaluator>().Evaluate(rule.Id, evaluation, default);
        }
        var result = (await client.GetFromJsonAsync<CameraAnalytics.Application.Common.Page<AlertData>>($"api/v1/alerts?siteId={site.Id}"))!;
        Assert.Equal(4, result.TotalCount);
        Assert.All(result.Items, x =>
        {
            Assert.Equal("Active", x.Status);
            Assert.True(x.Evidence.ObservedSeconds >= 2);
            Assert.True(x.Evidence.Quality.CoveragePercent >= 80);
        });
        Assert.Equal(1, result.Items.Single(x => x.Kind == "LongStay").Evidence.ActiveVisitCount);
    }

    [SqlFact]
    public async Task Concurrent_rules_evaluation_deduplicates_acknowledges_resolves_and_applies_cooldown()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, camera, _) = await host.Register(client);
        var rule = await IntegrationHost.Post<AlertRuleData>(client, "alert-rules",
            new SaveAlertRule(site.Id, camera.Id, null, "Camera freshness", "CameraNoRecentData", "Critical", 8,
                MinimumObservedSeconds: 0, CooldownSeconds: 60));
        var now = DateTime.UtcNow;
        await host.Ingest(host.Payload("alert-session", 1, timestamp: now, source: "live"), receivedAt: now);
        await Task.WhenAll(Evaluate(now.AddSeconds(10)), Evaluate(now.AddSeconds(11)));
        var alert = Assert.Single(await Read());
        Assert.Equal("Active", alert.Status);
        var path = $"alerts/{alert.Id}/acknowledge";
        var ack = await IntegrationHost.Post<AlertData>(client, path, new { });
        Assert.Equal("Acknowledged", ack.Status);
        Assert.Equal(ack.StateVersion, (await IntegrationHost.Post<AlertData>(client, path, new { })).StateVersion);
        await host.Ingest(host.Payload("alert-session", 2, timestamp: now.AddSeconds(12), source: "live"), receivedAt: now.AddSeconds(12));
        await Evaluate(now.AddSeconds(12));
        Assert.Equal("observed_recovery", Assert.Single(await Read()).ResolutionReason);
        await Evaluate(now.AddSeconds(22));
        Assert.Single(await Read());
        await Evaluate(now.AddSeconds(80));
        Assert.Equal(2, (await Read()).Count);

        async Task Evaluate(DateTime at)
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AlertEvaluator>().Evaluate(rule.Id, at, default);
        }

        async Task<IReadOnlyList<AlertData>> Read() =>
            (await client.GetFromJsonAsync<CameraAnalytics.Application.Common.Page<AlertData>>($"api/v1/alerts?siteId={site.Id}"))!.Items;
    }
}
