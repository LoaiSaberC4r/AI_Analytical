using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.BackgroundJobs;

public sealed class AggregationWorker(IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<AggregationOptions> aggregation, IOptions<AnalyticsOptions> analytics, IOptions<RetentionOptions> retention,
    ILogger<AggregationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!aggregation.Value.Enabled)
            return;
        using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(aggregation.Value.IntervalSeconds), clock);
        do
        {
            try
            {
                for (var i = 0; i < aggregation.Value.BatchSize; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await RefreshOne(scope.ServiceProvider.GetRequiredService<AnalyticsDb>(), stoppingToken))
                        break;
                }

                if (retention.Value.Enabled)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await Prune(scope.ServiceProvider.GetRequiredService<AnalyticsDb>(), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception e) { logger.LogWarning(e, "Analytics aggregation will retry at the next interval"); }
        } while (!stoppingToken.IsCancellationRequested && await ticker.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<bool> RefreshOne(AnalyticsDb db, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var lastFinished = AggregateQueue.FloorHour(now.AddSeconds(-analytics.Value.MaximumSnapshotGapSeconds));
        if (!await db.DirtyZoneHours.AnyAsync(x => x.HourStartUtc < lastFinished, ct))
            return false;
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var job = await db.DirtyZoneHours.Where(x => x.HourStartUtc < lastFinished)
            .OrderBy(x => x.DirtyAtUtc).ThenBy(x => x.HourStartUtc).FirstOrDefaultAsync(ct);
        if (job is null)
            return false;

        var to = job.HourStartUtc.AddHours(1);
        var window = new NumericWindow(ReportTimeline.Ms(job.HourStartUtc), ReportTimeline.Ms(to));
        var timeline = new ReportTimeline(new(job.SourceType, "eventTime", "hour", "UTC", job.HourStartUtc, to, null, null),
            window, TimeZoneInfo.Utc);
        var bins = new ReportBins(timeline, []);
        await new ReportData(db, analytics.Value.MaximumSnapshotGapSeconds).Zone(timeline, window, job.CameraZoneId, null, bins, ct);
        var stats = bins.Total;
        var aggregate = await db.ZoneHourlyAggregates.SingleOrDefaultAsync(x => x.CameraZoneId == job.CameraZoneId
            && x.SourceType == job.SourceType && x.HourStartUtc == job.HourStartUtc, ct);
        if (aggregate is null)
        {
            aggregate = new ZoneHourlyAggregate { CameraZoneId = job.CameraZoneId, SourceType = job.SourceType, HourStartUtc = job.HourStartUtc };
            db.ZoneHourlyAggregates.Add(aggregate);
        }
        aggregate.ObservationDurationSeconds = stats.Duration;
        aggregate.AverageOccupancy = stats.Average;
        aggregate.PeakOccupancy = stats.Peak;
        aggregate.PeakAtUtc = timeline.Time(stats.PeakAt);
        aggregate.ArrivalsDelta = stats.Traffic.Arrivals;
        aggregate.DeparturesDelta = stats.Traffic.Departures;
        aggregate.EntryDelta = stats.Traffic.Entries;
        aggregate.ExitDelta = stats.Traffic.Exits;
        aggregate.WrongWayInDelta = stats.Traffic.WrongWayIn;
        aggregate.WrongWayOutDelta = stats.Traffic.WrongWayOut;
        aggregate.LatestReportedDwellSeconds = stats.LatestReportedDwell;
        aggregate.LatestReportedAtUtc = timeline.Time(stats.LatestReportedAt);
        aggregate.LongestActiveDwellSeconds = stats.LongestActiveDwell;
        aggregate.CounterBaselineCount = stats.Baselines;
        aggregate.CounterResetCount = stats.Resets;
        aggregate.GapCount = stats.Gaps;
        aggregate.SampleCount = stats.Samples;
        aggregate.UncertainVisits = stats.UncertainVisits;
        aggregate.EstimatedCompletedVisits = stats.EstimatedVisits;
        aggregate.EstimatedTotalDwellSeconds = double.IsFinite(stats.EstimatedTotalDwell) ? stats.EstimatedTotalDwell : null;
        aggregate.CoveragePercent = stats.Coverage(3600);
        aggregate.DataQuality = stats.Quality(3600).Kind;
        aggregate.UpdatedAtUtc = now;
        db.DirtyZoneHours.Remove(job);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task Prune(AnalyticsDb db, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rawBefore = AggregateQueue.FloorHour(now.AddDays(-retention.Value.RawSnapshotsDays))
            .AddSeconds(-analytics.Value.MaximumSnapshotGapSeconds);
        var rejectionBefore = now.AddDays(-retention.Value.ProcessedMessagesDays);
        var aggregateBefore = AggregateQueue.FloorHour(now.AddDays(-retention.Value.HourlyAggregatesDays));

        await using var transaction = await WriteTransaction.Begin(db, ct);
        // Keep live evidence until its session is closed and every affected hour has a finalized rollup.
        if (!await db.DirtyZoneHours.AnyAsync(x => x.HourStartUtc <= rawBefore, ct))
        {
            var oldZones = db.ZoneSnapshots.Where(x => x.SourceTimestampUtc < rawBefore && x.Session.SourceType == "live"
                && x.Session.EndedAtUtc < rawBefore).OrderBy(x => x.SourceTimestampUtc).Take(5000).Select(x => x.Id);
            await db.ZoneSnapshots.Where(x => oldZones.Contains(x.Id)).ExecuteDeleteAsync(ct);
            // Camera snapshots support the staff report, so they share the aggregate retention horizon.
            var oldCameras = db.CameraSnapshots.Where(x => x.SourceTimestampUtc < aggregateBefore && x.Session.SourceType == "live"
                && x.Session.EndedAtUtc < aggregateBefore).OrderBy(x => x.SourceTimestampUtc).Take(5000).Select(x => x.Id);
            await db.CameraSnapshots.Where(x => oldCameras.Contains(x.Id)).ExecuteDeleteAsync(ct);
        }

        var oldErrors = db.IngestionRejections.Where(x => x.ReceivedAtUtc < rejectionBefore)
            .OrderBy(x => x.Id).Take(5000).Select(x => x.Id);
        await db.IngestionRejections.Where(x => oldErrors.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var oldHours = db.ZoneHourlyAggregates.Where(x => x.HourStartUtc < aggregateBefore)
            .OrderBy(x => x.HourStartUtc).Take(5000).Select(x => x.Id);
        await db.ZoneHourlyAggregates.Where(x => oldHours.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
