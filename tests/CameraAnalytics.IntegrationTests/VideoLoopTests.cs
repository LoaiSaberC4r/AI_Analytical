using System.Net;
using System.Net.Http.Json;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.IntegrationTests;

public sealed class VideoLoopTests
{
    [SqlFact]
    public async Task Replayed_positions_keep_live_state_and_historical_metrics_separate_by_loop()
    {
        await using var host = new IntegrationHost(cameraExternalId: "cam1");
        using var client = host.CreateClient();
        var (_, camera, zone) = await host.Register(client);
        var at = DateTime.UtcNow;
        await Send(6396, 20, null, 0, 10, track: false);
        await Send(6397, 20, 1000, 4, 10);
        await Send(6398, 20, 2000, 6, 13);
        var boundary = await Send(6399, 21, 1000, 10, 15);
        // A message without a loop continues loop 21 when its position is still ordered.
        await Send(6400, null, 2000, 12, 19);

        Assert.Equal(IngestResult.Duplicate, await host.Ingest(boundary));
        Assert.Equal(IngestResult.Rejected, await host.Ingest(Payload(6399, 22, 1000, 99, 99)));
        Assert.Equal(IngestResult.HistoricalOnly, await host.Ingest(Payload(6401, 20, 3000, 99, 99)));
        Assert.Equal(IngestResult.HistoricalOnly, await host.Ingest(Payload(6402, 21, 500, 99, 99)));

        var session = await host.InScope(db => db.AnalyticsSessions.SingleAsync());
        Assert.Equal("replay", session.ExternalSessionId);
        Assert.Equal(6400, session.LastSequence);
        Assert.Equal(21, session.LastVideoLoop);
        var live = (await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{camera.Id}/live"))!;
        Assert.Equal(session.Id, live.SessionId);
        Assert.Equal(6400, live.LastSequence);
        Assert.Equal(12, Assert.Single(live.Zones).Occupancy);
        Assert.Equal(21, await host.InScope(db => db.CameraCurrentStates.Select(x => x.LastVideoLoop).SingleAsync()));

        var cameras = await host.InScope(db => db.CameraSnapshots.OrderBy(x => x.Sequence).ToListAsync());
        Assert.Equal(new[] { 6396L, 6397, 6398, 6399, 6400 }, cameras.Where(x => x.IsOrdered).Select(x => x.Sequence));
        Assert.Equal(21, cameras.Single(x => x.Sequence == 6400).VideoLoop);
        Assert.Equal(20, cameras.Single(x => x.Sequence == 6401).VideoLoop);
        Assert.Null(cameras.Single(x => x.Sequence == 6398).ObservationEndSourceMs);
        var zones = await host.InScope(db => db.ZoneSnapshots.Where(x => x.IsOrdered).OrderBy(x => x.Sequence).ToListAsync());
        Assert.Equal(5, zones.Count);
        Assert.Null(zones.Single(x => x.Sequence == 6398).ObservationEndSourceMs);
        var deltas = await host.InScope(db => db.ZoneCounterDeltas.ToListAsync());
        Assert.Equal(5, deltas.Count);
        var firstInLoop = Assert.Single(deltas, x => x.VideoLoop == 21 && x.ToSourceTimeMs == 1000);
        Assert.Equal(2, firstInLoop.ZoneArrivalsDelta);
        Assert.Equal(0, firstInLoop.ResetMask);
        Assert.True(firstInLoop.HasObservationGap);
        Assert.Null(firstInLoop.FromSourceTimeMs);
        var visits = await host.InScope(db => db.TrackedVisits.OrderBy(x => x.VideoLoop).ToListAsync());
        Assert.Equal(2, visits.Count);
        Assert.Equal(visits[0].ExternalTrackId, visits[1].ExternalTrackId);
        Assert.Equal(VisitStatus.Uncertain, visits[0].Status);
        Assert.Equal("video_loop_changed", visits[0].CompletionReason);
        Assert.Equal(2000, visits[0].ResolvedSourceTimeMs);
        Assert.Null(visits[0].EstimatedCompletedAtUtc);
        Assert.Equal(VisitStatus.Active, visits[1].Status);
        Assert.Equal(1000, visits[1].FirstSourceTimeMs);

        var path = $"api/v1/analytics/sessions/{session.Id}/report";
        var latest = (await client.GetFromJsonAsync<SessionReport>(path))!;
        Assert.Equal(new long?[] { 20, 21 }, latest.AvailableVideoLoops);
        Assert.Equal(21, latest.Analytics.Period.VideoLoop);
        Assert.Equal(latest.Analytics.Period, latest.Staff.Period);
        Assert.Equal(0, latest.UntimedVideoObservations);
        AssertMetrics(latest, 10, 12, 6, 0);
        var previous = (await client.GetFromJsonAsync<SessionReport>($"{path}?videoLoop=20"))!;
        Assert.Equal(20, previous.Analytics.Period.VideoLoop);
        Assert.Equal(1, previous.UntimedVideoObservations);
        AssertMetrics(previous, 4, 6, 3, 1);
        Assert.Null(Assert.Single(previous.Staff.Cameras).LatestStaffCount);
        var narrow = (await client.GetFromJsonAsync<ReportResult>($"api/v1/analytics/zones/{zone.Id}/timeseries"
            + $"?sessionId={session.Id}&videoLoop=21&fromSourceTimeMs=1000&toSourceTimeMs=2000"))!;
        Assert.Equal(2, Assert.Single(narrow.Zones).Traffic.Arrivals);
        using var unknown = await client.GetAsync($"{path}?videoLoop=22");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var invalid = await client.GetAsync($"{path}?videoLoop=-1");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var liveWithLoop = await client.GetAsync("api/v1/analytics/traffic?videoLoop=0");
        Assert.Equal(HttpStatusCode.BadRequest, liveWithLoop.StatusCode);

        byte[] Payload(long sequence, long? loop, long? position, int occupancy, long count, bool track = true) =>
            host.Payload("replay", sequence, occupancy, count, at.AddSeconds(sequence - 6396),
                track: track, sourceMs: position, noPosition: position is null, videoLoop: loop);

        async Task<byte[]> Send(long sequence, long? loop, long? position, int occupancy, long count, bool track = true)
        {
            var bytes = Payload(sequence, loop, position, occupancy, count, track);
            Assert.Equal(IngestResult.Accepted, await host.Ingest(bytes));
            return bytes;
        }
    }

    [SqlFact]
    public async Task First_numbered_loop_recovers_an_unmarked_session_even_when_a_zone_misses_the_boundary()
    {
        await using var host = new IntegrationHost(cameraExternalId: "2");
        using var client = host.CreateClient();
        var (_, camera, _) = await host.Register(client);
        var at = DateTime.UtcNow;
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("existing", 6523,
            timestamp: at, sourceMs: 9000, count: 10, track: true)));
        Assert.Equal(IngestResult.HistoricalOnly, await host.Ingest(host.Payload("existing", 6524,
            timestamp: at.AddSeconds(1), sourceMs: 1000)));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("existing", 6525,
            timestamp: at.AddSeconds(2), noPosition: true, zoneId: null, videoLoop: 24)));
        var visit = await host.InScope(db => db.TrackedVisits.SingleAsync());
        Assert.Equal("video_loop_changed", visit.CompletionReason);
        Assert.Equal(VisitStatus.Uncertain, visit.Status);
        Assert.Null(await host.InScope(db => db.AnalyticsSessions.Select(x => x.LastSourceTimeMs).SingleAsync()));

        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("existing", 6526,
            timestamp: at.AddSeconds(3), sourceMs: 500, zoneId: null, videoLoop: 24)));
        Assert.Equal(IngestResult.Accepted, await host.Ingest(host.Payload("existing", 6527,
            timestamp: at.AddSeconds(4), sourceMs: 9500, occupancy: 8, count: 14, videoLoop: 24)));
        var oldZone = await host.InScope(db => db.ZoneSnapshots.SingleAsync(x => x.Sequence == 6523));
        Assert.Null(oldZone.VideoLoop);
        Assert.Null(oldZone.ObservationEndSourceMs);
        var live = (await client.GetFromJsonAsync<LiveCamera>($"api/v1/cameras/{camera.Id}/live"))!;
        Assert.Equal(6527, live.LastSequence);
        Assert.Equal(8, Assert.Single(live.Zones).Occupancy);
        var path = $"api/v1/analytics/sessions/{live.SessionId}/report";
        var latest = (await client.GetFromJsonAsync<SessionReport>(path))!;
        Assert.Equal(new long?[] { null, 24 }, latest.AvailableVideoLoops);
        Assert.Equal(500, latest.Analytics.Period.FromSourceTimeMs);
        Assert.Equal(9501, latest.Analytics.Period.ToSourceTimeMs);
        Assert.Equal(4, Assert.Single(latest.Analytics.Zones).Traffic.Arrivals);
        var unmarked = (await client.GetFromJsonAsync<SessionReport>($"{path}?videoLoop=unmarked"))!;
        Assert.Null(unmarked.Analytics.Period.VideoLoop);
        Assert.Equal(9000, unmarked.Analytics.Period.FromSourceTimeMs);
        Assert.Equal(9001, unmarked.Analytics.Period.ToSourceTimeMs);
        Assert.Equal(1, Assert.Single(unmarked.Analytics.Zones).UncertainVisits);
    }

    private static void AssertMetrics(SessionReport report, int average, int peak, int arrivals, int uncertainVisits)
    {
        Assert.Equal(1000, report.Analytics.Period.FromSourceTimeMs);
        Assert.Equal(2001, report.Analytics.Period.ToSourceTimeMs);
        var zone = Assert.Single(report.Analytics.Zones);
        Assert.Equal(average, zone.AverageOccupancy);
        Assert.Equal(peak, zone.PeakOccupancy);
        Assert.Equal(2, zone.DataQuality.SampleCount);
        Assert.Equal(1, zone.DataQuality.ObservationDurationSeconds);
        Assert.Equal(arrivals, zone.Traffic.Arrivals);
        Assert.Equal(uncertainVisits, zone.UncertainVisits);
        Assert.Equal(0, zone.EstimatedCompletedVisits);
        var staff = Assert.Single(report.Staff.Cameras);
        Assert.Equal(average / 2d, staff.AverageStaffCount);
        Assert.Equal(peak / 2, staff.PeakStaffCount);
        Assert.Equal(1, staff.DataQuality.ObservationDurationSeconds);
    }
}
