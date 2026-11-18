using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Analytics;

public sealed class PeriodComparison(ISmartQueries queries, double minimumCoverage)
{
    public async Task<ComparisonReport> Get(ReportFilter filter, string metric, string alignment, CancellationToken ct)
    {
        var unit = metric switch
        {
            "arrivals" => "events",
            "averageOccupancy" => "people per zone",
            _ => throw ApiError.Invalid("Comparison supports arrivals or averageOccupancy.")
        };
        if (alignment is not ("previousDuration" or "previousLocalDay" or "previousLocalWeek"))
            throw ApiError.Invalid("Comparison alignment is not supported.");
        var current = await queries.Flow(filter, ct);
        var (previousFilter, reason) = Previous(filter, current.Period, alignment);
        var previous = previousFilter is not null ? await queries.Flow(previousFilter, ct) : null;
        var rows = previous?.Zones.ToDictionary(x => x.Scope.ZoneId!.Value);
        return new(metric, unit, alignment, current.Period, previous?.Period, current.Zones.Select(zone =>
        {
            var past = rows?.GetValueOrDefault(zone.Scope.ZoneId!.Value);
            var currentValue = Value(zone.Total);
            var previousValue = past is null ? null : Value(past.Total);
            var unavailable = reason ?? (Qualified(currentValue) && previousValue is not null && Qualified(previousValue)
                ? null : "insufficient_comparable_coverage");
            var difference = unavailable is null ? currentValue.Value - previousValue?.Value : null;
            return new ComparisonRow(zone.Scope, zone.ZoneName, currentValue, previousValue, difference,
                difference.HasValue && previousValue?.Value is double divisor && divisor != 0 ? difference / divisor * 100 : null,
                unavailable);
        }).ToArray());

        ComparisonValue Value(FlowBucket bucket)
        {
            var value = metric == "arrivals" ? (double?)bucket.ObservedCounts?.Arrivals : bucket.AverageOccupancy;
            return new(value, value.HasValue ? bucket.Quality : bucket.Quality with { Provenance = "InsufficientData" });
        }

        bool Qualified(ComparisonValue x) => x.Value.HasValue && x.Quality.CoveragePercent >= minimumCoverage
            && (metric != "arrivals" || x.Quality.GapCount == 0 && x.Quality.ResetCount == 0);
    }

    public static (ReportFilter? Filter, string? Reason) Previous(ReportFilter filter, ReportPeriod period, string alignment)
    {
        if (period.TimeBasis == "sourceTime")
        {
            if (alignment != "previousDuration")
                throw ApiError.Invalid("Video comparisons support previousDuration within the selected loop.");
            var from = period.FromSourceTimeMs.GetValueOrDefault();
            var duration = period.ToSourceTimeMs.GetValueOrDefault() - from;
            if (duration <= 0 || from < duration)
                return (null, "previous_window_outside_video_loop");
            return (filter with { FromSourceTimeMs = from - duration, ToSourceTimeMs = from }, null);
        }

        var start = period.From!.Value;
        var end = period.To!.Value;
        if (alignment == "previousDuration")
        {
            var prior = start - (end - start);
            return prior.Year < 1900 ? (null, "previous_window_outside_supported_range") : (filter with { From = prior, To = start }, null);
        }
        var zone = Input.TimeZone(period.TimeZoneId);
        var days = alignment == "previousLocalDay" ? 1 : 7;
        var fromLocal = Shift(start, days, zone);
        var toLocal = Shift(end, days, zone);
        if (!fromLocal.HasValue || !toLocal.HasValue)
            return (null, "nonexistent_local_boundary");
        if (fromLocal.Value.Year < 1900)
            return (null, "previous_window_outside_supported_range");
        if (toLocal - fromLocal != end - start)
            return (null, "elapsed_duration_differs_after_dst_alignment");
        return (filter with { From = fromLocal, To = toLocal }, null);
    }

    private static DateTimeOffset? Shift(DateTime utc, int days, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, zone).AddDays(-days), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return null;
        if (!zone.IsAmbiguousTime(local))
            return new(local, zone.GetUtcOffset(local));
        var offsets = zone.GetAmbiguousTimeOffsets(local);
        var original = zone.GetUtcOffset(utc);
        return new(local, offsets.Contains(original) ? original : offsets.Max());
    }
}
