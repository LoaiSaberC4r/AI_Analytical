using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;

namespace CameraAnalytics.UnitTests;

public sealed class HistoricalAnalyticsTests
{
    [Fact]
    public void Forecast_requires_28_usable_dates_and_four_matching_history_slots()
    {
        var now = new DateTime(2026, 11, 16, 10, 30, 0, DateTimeKind.Utc);
        var cutoff = now.Date.AddHours(11);
        var hours = Enumerable.Range(1, 28).Select(day => new ForecastHour(cutoff.AddDays(-day), day, 95, true)).ToArray();
        var scope = new MetricScope(Guid.NewGuid());
        var opts = new ForecastOptions { HorizonHours = 2 };
        var lacking = HourlyForecast.Calculate(scope, "Zone", TimeZoneInfo.Utc, now, hours.Take(27), opts);
        Assert.Equal("insufficient_history_days", lacking.Points[0].UnavailableReason);
        var forecast = HourlyForecast.Calculate(scope, "Zone", TimeZoneInfo.Utc, now, hours, opts);
        Assert.Equal(17.5, forecast.Points[0].Prediction);
        Assert.Equal(12.25, forecast.Points[0].LowerHistoricalQuantile);
        Assert.Equal(22.75, forecast.Points[0].UpperHistoricalQuantile);
        Assert.Equal("insufficient_comparable_slots", forecast.Points[1].UnavailableReason);
        var dirty = HourlyForecast.Calculate(scope, "Zone", TimeZoneInfo.Utc, now,
            hours.Select(x => x.Value == 28 ? x with { Reliable = false } : x), opts);
        Assert.Null(dirty.Points[0].Prediction);
        Assert.Equal(forecast.Points, HourlyForecast.Calculate(scope, "Zone", TimeZoneInfo.Utc, now, hours.Reverse(), opts).Points);
    }

    [Fact]
    public void Local_day_comparison_detects_a_nonexistent_prior_boundary()
    {
        var start = new DateTime(2026, 3, 9, 6, 30, 0, DateTimeKind.Utc);
        var period = new ReportPeriod("live", "eventTime", "hour", "America/New_York", start, start.AddHours(1), null, null);
        Assert.Equal("nonexistent_local_boundary", PeriodComparison.Previous(new(), period, "previousLocalDay").Reason);
        var equivalent = PeriodComparison.Previous(new(), period, "previousDuration").Filter!;
        Assert.Equal(start.AddHours(-1), equivalent.From!.Value.UtcDateTime);
        Assert.Equal(start, equivalent.To!.Value.UtcDateTime);
    }

    [Fact]
    public void Autumn_clock_change_keeps_both_target_offsets()
    {
        var now = new DateTime(2026, 11, 1, 4, 30, 0, DateTimeKind.Utc);
        var forecast = HourlyForecast.Calculate(new(Guid.NewGuid()), "Zone", Input.TimeZone("America/New_York"), now, [],
            new() { HorizonHours = 2 });
        Assert.Equal(forecast.Points[0].TargetLocalStart.Hour, forecast.Points[1].TargetLocalStart.Hour);
        Assert.NotEqual(forecast.Points[0].TargetLocalStart.Offset, forecast.Points[1].TargetLocalStart.Offset);
    }
}
