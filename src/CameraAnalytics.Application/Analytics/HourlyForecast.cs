using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Analytics;

public readonly record struct ForecastHour(DateTime Start, double? Value, double CoveragePercent, bool Reliable);

public static class HourlyForecast
{
    public const string Model = "weekday-local-hour-median-v1";
    public const string Method = "Median of qualified matching weekdays and local hours. P25–P75 is a historical variability band.";
    public static ForecastReport Calculate(MetricScope scope, string zoneName, TimeZoneInfo zone, DateTime now,
        IEnumerable<ForecastHour> history, ForecastOptions options)
    {
        var cutoff = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);
        var since = cutoff.AddDays(-options.PreferredLookbackDays);
        var accepted = history.Where(x => x.Start >= since && x.Start < cutoff && x.Reliable && x.CoveragePercent >= options.MinimumCoveragePercent
                && x.Value.HasValue && double.IsFinite(x.Value.Value) && x.Value.Value >= 0)
            .Select(x => (x.Start, Value: x.Value!.Value, Local: LocalHour(x.Start, zone)))
            .Where(x => x.Local.HasValue).OrderBy(x => x.Start).ToArray();
        var days = accepted.Select(x => x.Local!.Value.Date).Distinct().Count();
        var lookup = accepted.GroupBy(x => (x.Local!.Value.DayOfWeek, x.Local.Value.Hour))
            .ToDictionary(group => group.Key, group => group.GroupBy(x => x.Local!.Value.Date).Select(date => date.First().Value).Order().ToArray());

        var points = new List<ForecastPoint>(options.HorizonHours);
        var first = now == cutoff ? cutoff : cutoff.AddHours(1);
        for (var index = 0; index < options.HorizonHours; index++)
        {
            var target = first.AddHours(index);
            var label = TimeZoneInfo.ConvertTime(new DateTimeOffset(target), zone);
            var aligned = LocalHour(target, zone).HasValue;
            var samples = aligned ? lookup.GetValueOrDefault((label.DayOfWeek, label.Hour)) ?? [] : [];
            var reason = !options.Enabled ? "forecast_disabled" : !aligned ? "local_hour_not_aligned_to_utc_rollup"
                : days < options.MinHistoryDays ? "insufficient_history_days"
                : samples.Length < options.MinComparableSlots ? "insufficient_comparable_slots" : null;
            points.Add(new(target, label, samples.Length, reason is null ? Quantile(samples, 0.5) : null,
                reason is null ? Quantile(samples, 0.25) : null, reason is null ? Quantile(samples, 0.75) : null,
                reason is null ? "Estimated" : "InsufficientData", reason));
        }
        return new(Model, "averageOccupancy", "people per zone", scope, zoneName, zone.Id, now,
            accepted.Length == 0 ? null : accepted[0].Start, accepted.Length == 0 ? null : accepted[^1].Start.AddHours(1),
            days, accepted.Length, 100d * accepted.Length / (options.PreferredLookbackDays * 24),
            points.Any(x => x.Prediction.HasValue) ? "Estimated" : "InsufficientData", Method, points);
    }

    private static DateTimeOffset? LocalHour(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), zone);
        return local.Minute == 0 && local.Second == 0 && zone.GetUtcOffset(utc.AddHours(1).AddTicks(-1)) == local.Offset
            ? local : null;
    }

    private static double Quantile(double[] sorted, double percentile)
    {
        var at = (sorted.Length - 1) * percentile;
        var lower = (int)Math.Floor(at);
        var upper = (int)Math.Ceiling(at);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (at - lower);
    }
}
