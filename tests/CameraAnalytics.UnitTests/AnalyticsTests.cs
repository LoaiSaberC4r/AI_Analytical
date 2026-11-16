using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.UnitTests;

public sealed class AnalyticsTests
{
    private static readonly DateTime Origin = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Camera = Guid.NewGuid(), Zone = Guid.NewGuid(), Session = Guid.NewGuid();

    [Fact]
    public void First_value_and_new_session_use_a_baseline()
    {
        var delta = CounterCalculator.Calculate(null, new(20, 5, 16, 3, 6, 2), false);
        Assert.Equal(default, delta.Counters);
        Assert.Equal(CounterQuality.Baseline, delta.Quality);
    }

    [Fact]
    public void Counters_increase_independently_and_decreases_are_flagged()
    {
        var before = new Counters(20, 8, 10, 3, 2, 1);
        var after = new Counters(25, 8, 1, 3, 6, 3);
        var delta = CounterCalculator.Calculate(before, after, true);
        Assert.Equal(new Counters(5, 0, 0, 0, 4, 2), delta.Counters);
        Assert.Equal(CounterQuality.Reset, delta.Quality);
        Assert.Equal(4, delta.ResetMask);
        Assert.True(delta.HasGap);
    }

    [Fact]
    public void Unchanged_counters_produce_zero()
    {
        var counters = new Counters(20, 5, 16, 3, 6, 2);
        var delta = CounterCalculator.Calculate(counters, counters, false);
        Assert.Equal(default, delta.Counters);
        Assert.Equal(CounterQuality.Valid, delta.Quality);
    }

    [Fact]
    public void Ordering_rejects_duplicates_late_messages_and_ended_sessions()
    {
        var session = new AnalyticsSession
        {
            LastSequence = 5, LastSourceTimeMs = 5000, LastSourceTimestampUtc = Origin.AddSeconds(5)
        };
        Assert.False(ObservationRules.IsOrdered(Observation(5), session));
        Assert.False(ObservationRules.IsOrdered(Observation(4), session));
        Assert.False(ObservationRules.IsOrdered(Observation(6) with { SourceTimeMs = 4000 }, session));
        Assert.True(ObservationRules.IsOrdered(Observation(6), session));
        Assert.True(ObservationRules.IsBeforeCurrentSession(Observation(4), session));
        session.EndedAtUtc = Origin;
        Assert.False(ObservationRules.IsOrdered(Observation(6), session));
    }

    [Fact]
    public void Observation_weight_peak_and_coverage_follow_half_open_range()
    {
        var stats = new ReportStatistics();
        var range = new NumericWindow(1000, 10000);
        stats.Observe(range, 0, 2000, 3);
        stats.Observe(range, 2000, 5000, 7);
        stats.Observe(range, 8000, null, 9);
        stats.Observe(range, 10000, null, 20);
        Assert.Equal(6, stats.Average);
        Assert.Equal(9, stats.Peak);
        Assert.Equal(8000, stats.PeakAt);
        Assert.Equal(2, stats.Samples);
        Assert.Equal(44.444, stats.Coverage(9), 3);
    }

    [Fact]
    public void Peak_hour_requires_coverage_and_rankings_use_their_own_metric()
    {
        var period = new ReportPeriod("live", "eventTime", "hour", "UTC", Origin, Origin.AddHours(2), null, null);
        var quality = new DataQuality("Derived", 4, 82, 3000, 0, 1, 0);
        var quiet = new ZonePeriod(new(Origin, Origin.AddHours(1), Origin, null, null), 4, 8, Origin, null,
            new(8, 3, 1, 0, 0, 0), quality);
        var sparse = quiet with { AverageOccupancy = 50, DataQuality = quality with { CoveragePercent = 20 } };
        var room = new ZoneResult(Zone, Camera, "Room", null, 5, 8, Origin, null, null, null, 2, 5,
            0, 0, null, 0, quiet.Traffic, quality, [quiet, sparse]);
        var lobby = room with { ZoneId = Guid.NewGuid(), ZoneName = "Lobby", Traffic = new(2, 1, 9, 0, 1, 0) };
        var report = new ReportResult(period, [room, lobby]);
        Assert.Equal(quiet, ReportRanking.PeakHours(report, 80).Zones[0].Occupancy);
        Assert.Equal("Room", ReportRanking.Rank(report, "arrivals", 80).Items[0].ZoneName);
        Assert.Equal("Lobby", ReportRanking.Rank(report, "entries", 80).Items[0].ZoneName);
        Assert.Empty(ReportRanking.Rank(report, "estimatedAverageDwell", 80).Items);
    }

    [Fact]
    public void Visit_disappears_returns_then_completes_from_valid_absent_observations()
    {
        var visit = Assert.Single(Observe([], [new(1, 2)], 0));
        Assert.Empty(Observe([visit], [new(1, 3)], 1));
        Assert.Equal(3, visit.LastDwellSeconds);
        Assert.Empty(Observe([visit], [], 2));
        Assert.Equal(VisitStatus.TemporarilyMissing, visit.Status);
        Assert.Empty(Observe([visit], [new(1, 5)], 3));
        Assert.Equal(VisitStatus.Active, visit.Status);
        for (var seq = 4; seq <= 9; seq++)
            Assert.Empty(Observe([visit], [], seq));
        Assert.Equal(VisitStatus.EstimatedCompleted, visit.Status);
        var stats = new ReportStatistics();
        stats.CountVisit(visit.Status, visit.LastDwellSeconds);
        Assert.Equal(1, stats.EstimatedVisits);
        Assert.Equal(5, stats.EstimatedTotalDwell);
        Assert.Equal(5, VisitTracker.EstimatedAverage(stats.EstimatedTotalDwell, stats.EstimatedVisits));
        Assert.Null(VisitTracker.EstimatedAverage(0, 0));
    }

    [Fact]
    public void Gap_reused_track_and_session_end_are_uncertain()
    {
        var first = Assert.Single(Observe([], [new(1, 2)], 0));
        var next = Assert.Single(Observe([first], [new(1, 1)], 1));
        Assert.Equal("dwell_regressed", first.CompletionReason);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Empty(Observe([next], [], 10, false));
        Assert.Equal(VisitStatus.Uncertain, next.Status);
        var ending = Assert.Single(Observe([], [new(1, 5)], 11));
        VisitTracker.Resolve(ending, Observation(12), Origin, VisitStatus.Uncertain, "session_ended");
        Assert.Null(ending.EstimatedCompletedAtUtc);
    }

    [Theory]
    [InlineData(4, DataFreshness.Fresh)]
    [InlineData(5, DataFreshness.Stale)]
    [InlineData(14, DataFreshness.Stale)]
    [InlineData(15, DataFreshness.NoRecentData)]
    public void Freshness_thresholds(int seconds, DataFreshness expected) =>
        Assert.Equal(expected, ObservationRules.Freshness(Origin.AddSeconds(seconds), Origin, new()));

    [Fact]
    public void Missing_capacity_and_unobserved_state_remain_null()
    {
        Assert.Null(ObservationRules.Utilization(19, null));
        Assert.Null(ObservationRules.Utilization(null, 20));
        Assert.Equal(125, ObservationRules.Utilization(25, 20));
        Assert.Equal(DataFreshness.NeverReceived, ObservationRules.Freshness(Origin, null, new()));
    }

    [Fact]
    public void Cairo_hour_labels_use_the_configured_timezone()
    {
        var timeline = ReportTimeline.Create(new(From: Origin, To: Origin.AddHours(2), Interval: "hour"),
            new(), Origin, "Africa/Cairo", null);
        Assert.Equal(2, timeline.Buckets().Count());
        Assert.Equal(12, timeline.Describe(timeline.Buckets().First()).LocalStart!.Value.Hour);
    }

    private static CameraObservation Observation(int sequence) => new("cam", "video_file", "file.mp4", "sess",
        sequence * 1000, 8, 0, "running", sequence, Origin.AddSeconds(sequence), []);

    private static TrackedVisit[] Observe(TrackedVisit[] visits, VisitorObservation[] tracks, int sequence, bool contiguous = true) =>
        VisitTracker.Observe(visits, tracks, Camera, Zone, Session, Observation(sequence), Origin, 1, contiguous, 5).ToArray();
}
