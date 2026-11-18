using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.UnitTests;

public sealed class SmartAnalyticsTests
{
    [Fact]
    public void Insights_suppress_poor_evidence_and_repeat_the_same_id_for_the_same_session()
    {
        var scope = new MetricScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var period = new ReportPeriod("video_file", "sourceTime", "minute", "UTC", null, null, 0, 60_000, 2)
        { SessionId = Guid.NewGuid() };
        var at = new PeriodPoint(null, null, null, 0, 60_000);
        var quality = new MetricQuality("Derived", 60, 60, 100, 0, 0, 0, false, false, []);
        var total = new FlowBucket(at, new(3, 0, 2, 0, 0, 0), null, 2, null, null, null, null, quality);
        var row = new ZoneFlow(scope, "Lobby", total, [], null);
        var flow = new FlowReport(period, [row]);
        var congestion = new CongestionReport(period, []);
        var dwell = new DwellDistributionReport(period, 300, DateTime.UnixEpoch, []);
        var first = Assert.Single(InsightRulesEngine.Generate(flow, congestion, dwell, DateTime.UnixEpoch, 80).Items);
        var next = Assert.Single(InsightRulesEngine.Generate(flow, congestion, dwell, DateTime.UnixEpoch.AddMinutes(1), 80).Items);
        Assert.Equal(first.InsightKey, next.InsightKey);
        Assert.Equal(3, first.Value);
        var other = flow with { Period = period with { SessionId = Guid.NewGuid() } };
        Assert.NotEqual(first.InsightKey, Assert.Single(InsightRulesEngine.Generate(other, congestion, dwell, DateTime.UnixEpoch, 80).Items).InsightKey);
        var sparse = row with { Total = total with { Quality = quality with { CoveragePercent = 20 } } };
        Assert.Empty(InsightRulesEngine.Generate(flow with { Zones = [sparse] }, congestion, dwell, DateTime.UnixEpoch, 80).Items);
    }

    [Theory]
    [InlineData(60, false, false, 10d)]
    [InlineData(30, false, false, 20d)]
    [InlineData(60, true, false, null)]
    [InlineData(60, false, true, null)]
    public void Rates_use_the_actual_covered_window_and_reject_unreliable_assignment(int seconds, bool gap, bool reset, double? expected)
    {
        var window = new NumericWindow(0, seconds * 1000L);
        var timeline = new ReportTimeline(new("live", "eventTime", "minute", "UTC", null, null, null, null), window, TimeZoneInfo.Utc);
        var stats = new ReportStatistics();
        Assert.Null(SmartMetricMath.Flow(timeline, window, stats, 80).ObservedCounts);
        stats.Observe(window, 0, window.End, 0);
        stats.CountTraffic(new(10, 2, 1, 3, 0, 2), reset ? CounterQuality.Reset : CounterQuality.Valid, gap);
        var bucket = SmartMetricMath.Flow(timeline, window, stats, 80);
        Assert.Equal(expected, bucket.EventsPerMinute?.Arrivals);
        Assert.Equal(10, bucket.ObservedCounts!.Arrivals);
        Assert.Equal(-2, bucket.TrafficBalance);
        Assert.Equal(0, bucket.AverageOccupancy);
    }

    [Fact]
    public void Congestion_counts_threshold_equality_and_clips_overlap()
    {
        var session = Guid.NewGuid();
        var c = new CongestionCalculator(new(0, 8_000), new() { MinObservedDurationSeconds = 3, RecoveryObservedSeconds = 2 }, new());
        c.Observe(new(session, 1, 0, 2000, 8, 10, 80, 100, false));
        c.Observe(new(session, 1, 1000, 4000, 12, 10, 80, 100, false));
        c.Observe(new(session, 1, 4000, 6000, 0, 10, 80, 100, false));
        c.Observe(new(session, 1, 6000, 8000, 0, null, null, null, false));
        var episode = Assert.Single(c.Complete());
        Assert.Equal("observed_recovery", episode.ResolutionReason);
        Assert.Equal(4, episode.WarningSeconds);
        Assert.Equal(2, episode.CriticalSeconds);
        Assert.Equal(6, c.CoveredSeconds);
        Assert.Equal(120, c.PeakUtilization);
    }

    [Fact]
    public void Loop_and_gap_end_an_observed_episode_without_filling_the_gap()
    {
        var session = Guid.NewGuid();
        var c = new CongestionCalculator(new(0, 25_000), new() { MinObservedDurationSeconds = 2, MaxObservationGapSeconds = 2 }, new());
        c.Observe(new(session, 1, 0, 2000, 10, 10, 80, 100, false));
        c.Observe(new(session, 2, 3000, 5000, 10, 10, 80, 100, false));
        c.Observe(new(session, 2, 20_000, 21_000, 10, 10, 80, 100, false));
        var episodes = c.Complete();
        Assert.Equal(new[] { "video_loop_changed", "observation_gap" }, episodes.Select(x => x.ResolutionReason));
        Assert.Equal(5, c.AboveWarningSeconds);
    }

    [Fact]
    public void Histogram_places_boundaries_into_the_next_bin()
    {
        var bins = SmartMetricMath.Histogram([0, 29.999, 30, 60, 600, 900], [30, 60, 600]);
        Assert.Equal(new[] { 2, 1, 1, 2 }, bins.Select(x => x.Count));
        Assert.Null(bins[^1].UpperSeconds);
    }
}
