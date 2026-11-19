using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed partial class SmartQueries
{
    public async Task<CongestionHistory> CongestionHistory(ReportFilter filter, ListFilter page, CancellationToken ct)
    {
        var skip = Input.Skip(page);
        await using var transaction = await WriteTransaction.Read(db, ct);
        var scope = await Resolve(filter, ct);
        var timeline = scope.Timeline;
        var effective = scope.Filter;
        var query = db.CongestionEpisodes.AsNoTracking().Where(x => x.Zone.Camera.SiteId == effective.SiteId
            && (!effective.CameraId.HasValue || x.Zone.CameraId == effective.CameraId)
            && (!effective.ZoneId.HasValue || x.CameraZoneId == effective.ZoneId)
            && (!effective.SessionId.HasValue || x.AnalyticsSessionId == effective.SessionId)
            && x.Session.SourceType == timeline.Period.SourceType);
        var timelineQuery = timeline.IsVideo ? query.Where(x => x.VideoLoop == timeline.Period.VideoLoop
                && x.EndSourceTimeMs > timeline.Window.Start && x.StartSourceTimeMs < timeline.Window.End)
                .OrderByDescending(x => x.StartSourceTimeMs)
            : query.Where(x => x.EndedAtUtc > timeline.Period.From && x.StartedAtUtc < timeline.Period.To).OrderByDescending(x => x.StartedAtUtc);
        var rows = await timelineQuery.ThenBy(x => x.Id).Skip(skip).Take(page.PageSize).Select(x => new { Episode = x, x.Zone.CameraId }).ToListAsync(ct);
        var items = rows.Select(row =>
        {
            var x = row.Episode;
            var window = timeline.IsVideo ? new NumericWindow(x.StartSourceTimeMs!.Value, x.EndSourceTimeMs!.Value)
                : new NumericWindow(ReportTimeline.Ms(x.StartedAtUtc), ReportTimeline.Ms(x.EndedAtUtc));
            var coverage = window.Seconds > 0 ? Math.Min(100, x.ObservedAboveWarningSeconds / window.Seconds * 100) : 0;
            var quality = SmartMetricMath.Quality(new("Derived", x.SampleCount, coverage, x.ObservedAboveWarningSeconds, 0, 0, 0),
                window.Seconds, true, warning: x.Status == "Uncertain" ? x.ResolutionReason : null);
            return new CongestionHistoryItem(x.Id, new(effective.SiteId!.Value, row.CameraId, x.CameraZoneId), x.AnalyticsSessionId,
                x.VideoLoop, timeline.Describe(window), x.ObservedAboveCriticalSeconds > 0 ? "Critical" : "Warning", x.Status,
                x.ResolutionReason, "capacity-at-observation-v1", x.Capacity, x.WarningThresholdPercent, x.CriticalThresholdPercent,
                x.MinimumObservedSeconds, x.ObservedAboveWarningSeconds, x.ObservedAboveCriticalSeconds, quality);
        }).ToArray();
        return new(timeline.Period, new(items, page.Page, page.PageSize, await timelineQuery.CountAsync(ct)));
    }
}
