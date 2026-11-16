using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Enums;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.BackgroundJobs;

public sealed class FreshnessWorker(IServiceScopeFactory scopes, TimeProvider clock, IOptions<AnalyticsOptions> settings,
    IAnalyticsNotifications notifications, ILogger<FreshnessWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ExpectedMessageIntervalSeconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AnalyticsDb>();
                var notices = await Update(db, options, stoppingToken);
                foreach (var notice in notices)
                    await notifications.FreshnessChanged(notice, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception e) { logger.LogWarning(e, "Camera freshness update failed"); }
        }
    }

    private async Task<IReadOnlyList<FreshnessChange>> Update(AnalyticsDb db, AnalyticsOptions options, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var staleAt = now.AddSeconds(-options.StaleAfterSeconds);
        var offlineAt = now.AddSeconds(-options.NoRecentDataAfterSeconds);
        var candidates = db.CameraCurrentStates.Where(x =>
            (x.DataFreshness == DataFreshness.Fresh && x.LastReceivedAtUtc <= staleAt)
            || (x.DataFreshness == DataFreshness.Stale && x.LastReceivedAtUtc <= offlineAt));
        if (!await candidates.AnyAsync(ct))
            return [];
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var states = await candidates.Include(x => x.Camera).ToListAsync(ct);
        var notices = new List<FreshnessChange>(states.Count);
        foreach (var state in states)
        {
            state.DataFreshness = ObservationRules.Freshness(now, state.LastReceivedAtUtc, options);
            state.StateVersion++;
            if (state.DataFreshness == DataFreshness.NoRecentData)
            {
                var visits = await db.TrackedVisits.Where(x => x.AnalyticsSessionId == state.AnalyticsSessionId
                    && (x.Status == VisitStatus.Active || x.Status == VisitStatus.TemporarilyMissing)).ToListAsync(ct);
                foreach (var visit in visits)
                {
                    VisitTracker.Resolve(visit, null, now, VisitStatus.Uncertain, "camera_no_recent_data");
                    await AggregateQueue.Mark(db, visit.CameraZoneId, state.SourceType, visit.ResolvedAtUtc!.Value,
                        visit.ResolvedAtUtc.Value, now, ct);
                }
            }
            notices.Add(new(state.Camera.SiteId, state.CameraId, state.AnalyticsSessionId, state.StateVersion,
                state.AnalyticsStatus, state.DataFreshness, state.LastReceivedAtUtc));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return notices;
    }
}
