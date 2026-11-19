using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.BackgroundJobs;

public sealed class AlertWorker(IServiceScopeFactory scopes, TimeProvider clock, IOptions<AlertOptions> options,
    IOptions<RetentionOptions> retention, ILogger<AlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.EvaluateEverySeconds), clock);
        var nextCleanup = DateTime.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AnalyticsDb>();
                var rules = await db.AlertRules.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(stoppingToken);
                foreach (var id in rules)
                {
                    await using var evaluation = scopes.CreateAsyncScope();
                    try
                    {
                        await evaluation.ServiceProvider.GetRequiredService<AlertEvaluator>().Evaluate(id, clock.GetUtcNow().UtcDateTime, stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogWarning(e, "Alert rule {RuleId} evaluation will be retried", id);
                    }
                }
                var now = clock.GetUtcNow().UtcDateTime;
                if (retention.Value.Enabled && now >= nextCleanup)
                {
                    await using var tx = await WriteTransaction.Begin(db, stoppingToken);
                    var cutoff = now.AddDays(-retention.Value.AlertHistoryDays);
                    await db.AnalyticsAlerts.Where(x => x.ResolvedAtUtc < cutoff).OrderBy(x => x.ResolvedAtUtc).Take(5000)
                        .ExecuteDeleteAsync(stoppingToken);
                    await tx.CommitAsync(stoppingToken);
                    nextCleanup = now.AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning(e, "Alert evaluation will retry at the next interval"); }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }
}
