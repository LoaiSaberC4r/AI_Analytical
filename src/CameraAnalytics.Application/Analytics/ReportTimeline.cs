using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Analytics;

public sealed class ReportTimeline(ReportPeriod period, NumericWindow window, TimeZoneInfo zone)
{
    public ReportPeriod Period { get; } = period;
    public NumericWindow Window { get; } = window;
    public bool IsVideo => Period.TimeBasis == "sourceTime";
    public static long Ms(DateTime value) => (value.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
    public static long? Ms(DateTime? value) => value.HasValue ? Ms(value.Value) : null;
    public static DateTime Utc(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    public DateTime? Time(long? ms) => !IsVideo && ms.HasValue ? Utc(ms.Value) : null;
    public long? Position(long? ms) => IsVideo ? ms : null;

    public PeriodPoint Describe(NumericWindow part)
    {
        var utc = Time(part.Start);
        return new(utc, Time(part.End),
            utc.HasValue ? TimeZoneInfo.ConvertTime(new DateTimeOffset(utc.Value), zone) : null,
            Position(part.Start), Position(part.End));
    }

    public IEnumerable<NumericWindow> Buckets()
    {
        var width = Period.Interval switch { "minute" => 60_000L, "hour" => 3_600_000L, _ => 86_400_000L };
        for (var start = Window.Start; start < Window.End;)
        {
            var end = IsVideo ? start + Math.Min(width - start % width, Window.End - start) : NextWallBoundary(start, width);
            end = Math.Min(Window.End, end);
            yield return new(start, end);
            start = end;
        }
    }

    private long NextWallBoundary(long start, long width)
    {
        var utc = Utc(start);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        if (width < 86_400_000L)
        {
            var offset = zone.GetUtcOffset(utc);
            var wallMs = (local.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
            var remainder = (wallMs % width + width) % width;
            var next = start + width - remainder;
            if (zone.GetUtcOffset(Utc(next - 1)) == offset)
                return next;

            // An offset change inside this bucket ends it at the transition.
            var low = start + 1;
            var high = next;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (zone.GetUtcOffset(Utc(middle)) == offset) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        var midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(midnight))
            midnight = midnight.AddMinutes(1);
        var boundary = zone.IsAmbiguousTime(midnight)
            ? new DateTimeOffset(midnight, zone.GetAmbiguousTimeOffsets(midnight).Max()).UtcDateTime
            : TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
        return Ms(boundary);
    }

    public static ReportTimeline Create(ReportFilter filter, AnalyticsOptions options, DateTime utcNow, string zoneId,
        SessionData? session)
    {
        var source = filter.SourceType ?? session?.SourceType ?? "live";
        if ((session is not null && source != session.SourceType) || source is not ("live" or "video_file"))
            throw ApiError.Invalid("Report sourceType must match the selected session and be live or video_file.");
        var video = source == "video_file";
        var interval = filter.Interval ?? (video ? "minute" : options.DefaultAggregationInterval);
        if (interval is not ("minute" or "hour" or "day"))
            throw ApiError.Invalid("Interval must be minute, hour or day.");
        var maxDays = interval == "minute"
            ? Math.Min(options.MaximumReportRangeDays, options.MaximumMinuteReportRangeDays)
            : options.MaximumReportRangeDays;
        var maxMs = maxDays * 86_400_000L;

        if (video)
        {
            if (session is null || filter.From.HasValue || filter.To.HasValue)
                throw ApiError.Invalid("Video reports require a session and source-time bounds in milliseconds.");
            if (session.FirstSourceTimeMs is null && filter.FromSourceTimeMs is null && filter.ToSourceTimeMs is null)
                return new(new(source, "sourceTime", interval, zoneId, null, null, null, null),
                    new(0, 0), TimeZoneInfo.Utc);
            var start = filter.FromSourceTimeMs ?? session.FirstSourceTimeMs ?? 0;
            var last = session.LastSourceTimeMs ?? start;
            var end = filter.ToSourceTimeMs ?? (last < long.MaxValue ? last + 1 : last);
            Verify(start, end, maxMs, maxDays, true);
            return new(new(source, "sourceTime", interval, zoneId, null, null, start, end), new(start, end), TimeZoneInfo.Utc);
        }

        if (filter.FromSourceTimeMs.HasValue || filter.ToSourceTimeMs.HasValue)
            throw ApiError.Invalid("Live reports use timestamp bounds.");
        var to = filter.To?.UtcDateTime ?? (session is null ? utcNow : session.LastSourceTimestampUtc.AddMilliseconds(1));
        var from = filter.From?.UtcDateTime ?? (session?.FirstSourceTimestampUtc ?? (to.Year >= 1900 ? to.AddDays(-1) : to));
        if (from.Year is < 1900 or > 9900 || to.Year is < 1900 or > 9900)
            throw ApiError.Invalid("Timestamps are outside the supported range.");
        var window = new NumericWindow(Ms(from), Ms(to));
        Verify(window.Start, window.End, maxMs, maxDays, false);
        return new(new(source, "eventTime", interval, zoneId, from, to, null, null), window, Input.TimeZone(zoneId));
    }

    private static void Verify(long start, long end, long maxMs, int maxDays, bool video)
    {
        if ((video && start < 0) || end <= start || end - start > maxMs)
            throw ApiError.Invalid($"Use from < to and a range of at most {maxDays} days for the selected interval.");
    }
}
