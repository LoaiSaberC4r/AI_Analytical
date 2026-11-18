using System.Globalization;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed class ReportQueries(AnalyticsDb db, IOptions<AnalyticsOptions> settings, TimeProvider clock) : IReportQueries
{
    private ReportData Data => new(db, settings.Value.MaximumSnapshotGapSeconds);

    public async Task<ReportResult> Zones(ReportFilter filter, bool series, CancellationToken ct)
    {
        var scope = await Resolve(filter, ct);
        return await Zones(scope, series, ct);
    }

    private async Task<ReportResult> Zones(ReportScope scope, bool series, CancellationToken ct)
    {
        var (effective, timeline, _, _) = scope;
        var cameras = Cameras(effective);
        var zones = await db.CameraZones.AsNoTracking().Where(z => cameras.Any(c => c.Id == z.CameraId)
            && (!effective.ZoneId.HasValue || z.Id == effective.ZoneId))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new { x.Id, x.CameraId, x.Name, x.Capacity }).ToListAsync(ct);
        var window = timeline.Window;
        var periods = series ? timeline.Buckets().ToArray() : [];
        var rows = new List<ZoneResult>(zones.Count);
        foreach (var zone in zones)
        {
            var bins = new ReportBins(timeline, periods);
            await ReadZone(timeline, bins, zone.Id, effective.SessionId, ct);
            var total = bins.Total;
            var dwellSum = double.IsFinite(total.EstimatedTotalDwell) ? total.EstimatedTotalDwell : (double?)null;
            rows.Add(new(zone.Id, zone.CameraId, zone.Name, zone.Capacity, total.Average, total.Peak,
                timeline.Time(total.PeakAt), timeline.Position(total.PeakAt), ObservationRules.Utilization(total.Peak, zone.Capacity),
                ObservationRules.Utilization(total.Average, zone.Capacity), total.LatestReportedDwell, total.LongestActiveDwell,
                total.EstimatedVisits, dwellSum, dwellSum.HasValue ? VisitTracker.EstimatedAverage(dwellSum.Value, total.EstimatedVisits) : null,
                total.UncertainVisits, total.Traffic, total.Quality(window.Seconds), bins.Series.Select(x =>
                    new ZonePeriod(timeline.Describe(x.Period), x.Stats.Average, x.Stats.Peak, timeline.Time(x.Stats.PeakAt),
                        timeline.Position(x.Stats.PeakAt), x.Stats.Traffic, x.Stats.Quality(x.Period.Seconds))).ToArray()));
        }
        return new(timeline.Period, rows);
    }

    internal async Task ReadZone(ReportTimeline timeline, ReportBins bins, Guid zoneId, Guid? sessionId, CancellationToken ct,
        bool useMinutes = false)
    {
        var window = timeline.Window;
        if (window.End == window.Start)
            return;
        if (timeline.IsVideo || sessionId.HasValue)
        {
            await Data.Zone(timeline, window, zoneId, sessionId, bins, ct);
            return;
        }
        if (useMinutes && timeline.Period.Interval == "minute")
        {
            var minutes = await db.ZoneMinuteAggregates.AsNoTracking().Where(x => x.CameraZoneId == zoneId
                    && x.MinuteStartUtc >= timeline.Period.From && x.MinuteStartUtc <= timeline.Period.To!.Value.AddMinutes(-1)
                    && !db.DirtyZoneMinutes.Any(d => d.CameraZoneId == zoneId && d.MinuteStartUtc == x.MinuteStartUtc))
                .OrderBy(x => x.MinuteStartUtc).ToListAsync(ct);
            var next = window.Start;
            foreach (var minute in minutes)
            {
                var begin = ReportTimeline.Ms(minute.MinuteStartUtc);
                var span = new NumericWindow(begin, begin + 60_000);
                if (!bins.CanUseHour(span))
                    continue;
                if (next < begin)
                    await Data.Zone(timeline, new(next, begin), zoneId, sessionId, bins, ct);
                bins.Rollup(span, minute);
                next = span.End;
            }
            if (next < window.End)
                await Data.Zone(timeline, new(next, window.End), zoneId, sessionId, bins, ct);
            return;
        }
        var start = ReportTimeline.Utc(window.Start);
        var end = ReportTimeline.Utc(window.End).AddHours(-1);
        var source = timeline.Period.SourceType;
        var hours = await db.ZoneHourlyAggregates.AsNoTracking()
            .Where(x => x.CameraZoneId == zoneId && x.SourceType == source && x.HourStartUtc >= start && x.HourStartUtc <= end
                && !db.DirtyZoneHours.Any(d => d.CameraZoneId == x.CameraZoneId && d.SourceType == source && d.HourStartUtc == x.HourStartUtc))
            .OrderBy(x => x.HourStartUtc).ToListAsync(ct);
        var cursor = window.Start;
        foreach (var hour in hours)
        {
            var begin = ReportTimeline.Ms(hour.HourStartUtc);
            var span = new NumericWindow(begin, begin + 3_600_000);
            if (!bins.CanUseHour(span))
                continue;
            if (cursor < begin)
                await Data.Zone(timeline, new(cursor, begin), zoneId, sessionId, bins, ct);
            bins.Rollup(span, hour);
            cursor = span.End;
        }
        if (cursor < window.End)
            await Data.Zone(timeline, new(cursor, window.End), zoneId, sessionId, bins, ct);
    }

    public async Task<StaffReport> Staff(ReportFilter filter, CancellationToken ct)
    {
        var scope = await Resolve(filter, ct);
        return await Staff(scope, ct);
    }

    private async Task<StaffReport> Staff(ReportScope scope, CancellationToken ct)
    {
        var (effective, timeline, _, _) = scope;
        var cameras = await Cameras(effective).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var periods = timeline.Buckets().ToArray();
        var results = new List<StaffCamera>();
        foreach (var camera in cameras)
        {
            var bins = new ReportBins(timeline, periods);
            if (timeline.Window.End > timeline.Window.Start)
                await Data.Staff(timeline, camera.Id, effective.SessionId, bins, ct);
            var current = await db.CameraCurrentStates.AsNoTracking().Where(x => x.CameraId == camera.Id
                && (!effective.SessionId.HasValue || x.AnalyticsSessionId == effective.SessionId) && x.SourceType == timeline.Period.SourceType)
                .Where(x => !timeline.IsVideo || x.LastVideoLoop == timeline.Period.VideoLoop)
                .Select(x => new { x.StaffCount, x.LastReceivedAtUtc }).SingleOrDefaultAsync(ct);
            results.Add(new(camera.Id, camera.Name, current?.StaffCount, current?.LastReceivedAtUtc, bins.Total.Average,
                bins.Total.Peak, bins.Total.Quality(timeline.Window.Seconds), bins.Series.Select(x =>
                    new StaffPoint(timeline.Describe(x.Period), x.Stats.Average, x.Stats.Peak, x.Stats.Quality(x.Period.Seconds))).ToArray()));
        }
        return new(timeline.Period, results);
    }

    public async Task<Page<SessionData>> Sessions(ListFilter filter, Guid? siteId, Guid? cameraId, string? sourceType, CancellationToken ct)
    {
        var skip = Input.Skip(filter);
        var search = filter.Search?.Trim();
        var sessions = db.AnalyticsSessions.AsNoTracking().Where(x => (!siteId.HasValue || x.Camera.SiteId == siteId)
            && (!cameraId.HasValue || x.CameraId == cameraId) && (sourceType == null || x.SourceType == sourceType)
            && (search == null || x.SourceName.Contains(search) || x.ExternalSessionId.Contains(search)));
        var count = await sessions.CountAsync(ct);
        var items = await SessionRows(sessions.OrderByDescending(x => x.StartedAtUtc).ThenByDescending(x => x.Id))
            .Skip(skip).Take(filter.PageSize).ToListAsync(ct);
        return new(items, filter.Page, filter.PageSize, count);
    }

    public async Task<SessionData> Session(Guid id, CancellationToken ct) =>
        await SessionRows(db.AnalyticsSessions.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw ApiError.NotFound("Session");

    public async Task<SessionReport> SessionReport(Guid id, ReportFilter filter, CancellationToken ct)
    {
        var scope = await Resolve(filter with { SessionId = id }, ct);
        var loops = scope.Timeline.IsVideo
            ? await OrderedSnapshots(id).Select(x => x.VideoLoop).Distinct().OrderBy(x => x).ToListAsync(ct)
            : null;
        return new(scope.Session!, await Zones(scope, true, ct), await Staff(scope, ct), scope.Untimed, loops);
    }

    private static IQueryable<SessionData> SessionRows(IQueryable<AnalyticsSession> sessions) => sessions.Select(x =>
        new SessionData(x.Id, x.CameraId, x.Camera.Name, x.ExternalSessionId, x.SourceType, x.SourceName, x.Status, x.FirstSequence,
            x.LastSequence, x.FirstSourceTimeMs, x.LastSourceTimeMs, x.FirstSourceTimestampUtc, x.LastSourceTimestampUtc,
            x.StartedAtUtc, x.LastReceivedAtUtc, x.EndedAtUtc, x.CreatedAtUtc, x.LastVideoLoop));

    private IQueryable<Camera> Cameras(ReportFilter filter) => db.Cameras.AsNoTracking()
        .Where(x => (!filter.SiteId.HasValue || x.SiteId == filter.SiteId) && (!filter.CameraId.HasValue || x.Id == filter.CameraId));

    internal sealed record ReportScope(ReportFilter Filter, ReportTimeline Timeline, SessionData? Session, int Untimed);

    private IQueryable<CameraSnapshot> OrderedSnapshots(Guid sessionId) =>
        db.CameraSnapshots.AsNoTracking().Where(x => x.AnalyticsSessionId == sessionId && x.IsOrdered);

    internal async Task<ReportScope> Resolve(ReportFilter filter, CancellationToken ct)
    {
        var session = filter.SessionId.HasValue ? await Session(filter.SessionId.Value, ct) : null;
        if (session is not null)
            filter = WithCamera(filter, session.CameraId);

        if (filter.ZoneId.HasValue)
        {
            var zone = await db.CameraZones.AsNoTracking().Where(x => x.Id == filter.ZoneId)
                .Select(x => new { x.CameraId }).SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound("Zone");
            filter = WithCamera(filter, zone.CameraId);
        }

        if (filter.CameraId.HasValue)
        {
            var camera = await db.Cameras.AsNoTracking().Where(x => x.Id == filter.CameraId)
                .Select(x => new { x.SiteId }).SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound("Camera");
            if (filter.SiteId.HasValue && filter.SiteId != camera.SiteId)
                throw ApiError.Invalid("The camera does not belong to the selected site.");
            filter = filter with { SiteId = camera.SiteId };
        }
        var zoneId = filter.SiteId.HasValue
            ? await db.Sites.AsNoTracking().Where(x => x.Id == filter.SiteId).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct)
                ?? throw ApiError.NotFound("Site")
            : "UTC";
        VideoReportBounds? bounds = null;
        var untimed = 0;
        if (session?.SourceType == "video_file")
        {
            var loop = ResolveVideoLoop(filter.VideoLoop, session.LastVideoLoop);
            var positions = await OrderedSnapshots(session.Id).Where(x => x.VideoLoop == loop)
                .GroupBy(x => x.AnalyticsSessionId)
                .Select(g => new
                {
                    First = g.Min(x => x.SourceTimeMs),
                    Last = g.Max(x => x.SourceTimeMs),
                    Untimed = g.Count(x => x.SourceTimeMs == null)
                }).SingleOrDefaultAsync(ct);
            if (positions is null && filter.VideoLoop is not null && loop.HasValue)
                throw ApiError.NotFound("Video loop");
            bounds = new(loop, positions?.First, positions?.Last);
            untimed = positions?.Untimed ?? 0;
        }
        var timeline = ReportTimeline.Create(filter, settings.Value, clock.GetUtcNow().UtcDateTime, zoneId, session, bounds);
        return new(filter, timeline, session, untimed);
    }

    private static long? ResolveVideoLoop(string? selected, long? latest) => selected switch
    {
        null => latest,
        "unmarked" => null,
        _ when long.TryParse(selected, NumberStyles.None, CultureInfo.InvariantCulture, out var loop) => loop,
        _ => throw ApiError.Invalid("videoLoop must be a non-negative integer or unmarked.")
    };

    private static ReportFilter WithCamera(ReportFilter filter, Guid cameraId)
    {
        if (filter.CameraId.HasValue && filter.CameraId != cameraId)
            throw ApiError.Invalid("The selected filters refer to different cameras.");
        return filter with { CameraId = cameraId };
    }
}
