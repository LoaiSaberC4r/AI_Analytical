using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.BackgroundJobs;

public sealed class MinuteAggregationWorker(IServiceScopeFactory scopes, TimeProvider clock, IOptions<MinuteAggregationOptions> settings,
    IOptions<AnalyticsOptions> analytics, IOptions<CongestionOptions> congestion, IOptions<RetentionOptions> retention,
    ILogger<MinuteAggregationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Value.Enabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.Value.IntervalSeconds), clock);
        var nextPrune = DateTime.MinValue;
        do
        {
            try
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var end = MinuteQueue.Floor(now.AddSeconds(-Math.Max(settings.Value.FinalizationDelaySeconds, analytics.Value.MaximumSnapshotGapSeconds)));
                for (var i = 0; i < settings.Value.BatchSize; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await Refresh(scope.ServiceProvider.GetRequiredService<AnalyticsDb>(), now, end, stoppingToken))
                        break;
                }
                await using var maintenance = scopes.CreateAsyncScope();
                var db = maintenance.ServiceProvider.GetRequiredService<AnalyticsDb>();
                if (AnalyticsTelemetry.DirtyMinutes.Enabled)
                    AnalyticsTelemetry.DirtyMinutes.Record(await db.DirtyZoneMinutes.CountAsync(stoppingToken)
                        + await db.DirtyCameraMinutes.CountAsync(stoppingToken));
                if (settings.Value.BackfillDays > 0)
                    await MinuteBackfill.Advance(db, settings.Value.BackfillDays, analytics.Value.MaximumSnapshotGapSeconds, end, now, stoppingToken);
                if (retention.Value.Enabled && now >= nextPrune)
                {
                    await Prune(db, now.AddDays(-retention.Value.MinuteAggregatesDays), stoppingToken);
                    nextPrune = now.AddMinutes(5);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning(e, "Minute aggregation will retry at the next interval"); }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> Refresh(AnalyticsDb db, DateTime now, DateTime end, CancellationToken ct)
    {
        if (!await db.DirtyZoneMinutes.AnyAsync(x => x.MinuteStartUtc < end, ct)
            && !await db.DirtyCameraMinutes.AnyAsync(x => x.MinuteStartUtc < end, ct))
            return false;
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var zone = await db.DirtyZoneMinutes.Where(x => x.MinuteStartUtc < end).OrderBy(x => x.DirtyAtUtc)
            .ThenBy(x => x.MinuteStartUtc).ThenBy(x => x.CameraZoneId).FirstOrDefaultAsync(ct);
        var camera = await db.DirtyCameraMinutes.Where(x => x.MinuteStartUtc < end).OrderBy(x => x.DirtyAtUtc)
            .ThenBy(x => x.MinuteStartUtc).ThenBy(x => x.CameraId).FirstOrDefaultAsync(ct);
        var data = new ReportData(db, analytics.Value.MaximumSnapshotGapSeconds);
        if (zone is not null)
        {
            var timeline = Timeline(zone.MinuteStartUtc);
            var bins = new ReportBins(timeline, []);
            await data.Zone(timeline, timeline.Window, zone.CameraZoneId, null, bins, ct);
            var capacity = new CongestionCalculator(timeline.Window, congestion.Value, analytics.Value);
            await data.Capacity(timeline, null, zone.CameraZoneId, capacity, ct);
            var row = await db.ZoneMinuteAggregates.FindAsync([zone.CameraZoneId, zone.MinuteStartUtc], ct);
            if (row is null)
                row = db.ZoneMinuteAggregates.Add(new() { CameraZoneId = zone.CameraZoneId, MinuteStartUtc = zone.MinuteStartUtc }).Entity;
            AggregateQueue.Fill(row, bins.Total, 60, now);
            row.CapacityCoveredSeconds = capacity.CoveredSeconds > 0 ? capacity.CoveredSeconds : null;
            row.AverageUtilizationPercent = capacity.AverageUtilization;
            row.AboveWarningSeconds = capacity.CoveredSeconds > 0 ? capacity.AboveWarningSeconds : null;
            row.AboveCriticalSeconds = capacity.CoveredSeconds > 0 ? capacity.AboveCriticalSeconds : null;
            db.DirtyZoneMinutes.Remove(zone);
        }
        if (camera is not null)
        {
            var timeline = Timeline(camera.MinuteStartUtc);
            var stats = new CameraSampleStatistics();
            await foreach (var sample in data.CameraSamples(timeline, camera.CameraId, null, ct))
                stats.Observe(timeline.Window, sample.At, sample.Until, sample.Staff, sample.Fps, sample.MissingSequences);
            var row = await db.CameraMinuteAggregates.FindAsync([camera.CameraId, camera.MinuteStartUtc], ct);
            if (row is null)
                row = db.CameraMinuteAggregates.Add(new() { CameraId = camera.CameraId, MinuteStartUtc = camera.MinuteStartUtc }).Entity;
            row.ObservationDurationSeconds = stats.Staff.Duration;
            row.AverageStaffCount = stats.Staff.Average;
            row.PeakStaffCount = stats.Staff.Peak;
            row.MinimumFps = stats.MinimumFps;
            row.MaximumFps = stats.MaximumFps;
            row.AverageFps = stats.AverageFps;
            row.MissingSequences = stats.MissingSequences;
            row.GapCount = stats.GapCount;
            row.SampleCount = stats.Staff.Samples;
            row.UpdatedAtUtc = now;
            db.DirtyCameraMinutes.Remove(camera);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (zone is not null) AnalyticsTelemetry.MinuteLag.Record((now - zone.MinuteStartUtc.AddMinutes(1)).TotalSeconds);
        if (camera is not null) AnalyticsTelemetry.MinuteLag.Record((now - camera.MinuteStartUtc.AddMinutes(1)).TotalSeconds);
        return zone is not null || camera is not null;
    }

    private static ReportTimeline Timeline(DateTime from)
    {
        var to = from.AddMinutes(1);
        return new(new("live", "eventTime", "minute", "UTC", from, to, null, null),
            new(ReportTimeline.Ms(from), ReportTimeline.Ms(to)), TimeZoneInfo.Utc);
    }

    private static async Task Prune(AnalyticsDb db, DateTime cutoff, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Begin(db, ct);
        await db.ZoneMinuteAggregates.Where(x => x.MinuteStartUtc < cutoff && !db.DirtyZoneMinutes.Any(d =>
                d.CameraZoneId == x.CameraZoneId && d.MinuteStartUtc == x.MinuteStartUtc)).OrderBy(x => x.MinuteStartUtc).Take(5000).ExecuteDeleteAsync(ct);
        await db.CameraMinuteAggregates.Where(x => x.MinuteStartUtc < cutoff && !db.DirtyCameraMinutes.Any(d =>
                d.CameraId == x.CameraId && d.MinuteStartUtc == x.MinuteStartUtc)).OrderBy(x => x.MinuteStartUtc).Take(5000).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
