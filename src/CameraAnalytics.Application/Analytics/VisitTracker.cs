using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Application.Analytics;

public static class VisitTracker
{
    public static IEnumerable<TrackedVisit> Observe(IEnumerable<TrackedVisit> open,
        IReadOnlyList<VisitorObservation> active, Guid cameraId, Guid zoneId, Guid sessionId,
        CameraObservation observation, DateTime now, double adjacentSeconds, bool contiguous, int graceSeconds)
    {
        var visible = active.ToDictionary(x => x.TrackId);
        foreach (var visit in open)
        {
            if (visible.Remove(visit.ExternalTrackId, out var current))
            {
                if (!contiguous || current.DwellSeconds < visit.LastDwellSeconds)
                {
                    Resolve(visit, observation, now, VisitStatus.Uncertain,
                        !contiguous ? "observation_gap" : "dwell_regressed");
                    yield return Start(current);
                    continue;
                }
                Update(visit, current, observation, now);
            }
            else if (!contiguous)
            {
                Resolve(visit, observation, now, VisitStatus.Uncertain, "observation_gap");
            }
            else if (visit.Status == VisitStatus.Active)
            {
                visit.Status = VisitStatus.TemporarilyMissing;
                visit.ObservedMissingSeconds = 0;
                visit.UpdatedAtUtc = now;
            }
            else
            {
                visit.ObservedMissingSeconds += adjacentSeconds;
                visit.UpdatedAtUtc = now;
                if (visit.ObservedMissingSeconds >= graceSeconds)
                    Resolve(visit, observation, now, VisitStatus.EstimatedCompleted, "absent_in_valid_observations");
            }
        }

        foreach (var current in visible.Values)
            yield return Start(current);

        TrackedVisit Start(VisitorObservation current)
        {
            var visit = new TrackedVisit
            {
                CameraId = cameraId, CameraZoneId = zoneId, AnalyticsSessionId = sessionId,
                ExternalTrackId = current.TrackId, VideoLoop = observation.VideoLoop, FirstSeenAtUtc = observation.SourceTimestampUtc,
                FirstSourceTimeMs = observation.SourceTimeMs, CreatedAtUtc = now
            };
            Update(visit, current, observation, now);
            return visit;
        }
    }

    private static void Update(TrackedVisit visit, VisitorObservation current, CameraObservation data, DateTime now)
    {
        visit.Status = VisitStatus.Active;
        visit.LastSeenAtUtc = data.SourceTimestampUtc;
        visit.LastSourceTimeMs = data.SourceTimeMs;
        visit.LastDwellSeconds = current.DwellSeconds;
        visit.MaximumObservedDwellSeconds = Math.Max(visit.MaximumObservedDwellSeconds, current.DwellSeconds);
        visit.ObservedMissingSeconds = 0;
        visit.UpdatedAtUtc = now;
    }

    public static void Resolve(TrackedVisit visit, CameraObservation? observation, DateTime now, VisitStatus status, string reason)
    {
        visit.Status = status;
        visit.CompletionReason = reason;
        visit.ResolvedAtUtc = observation?.SourceTimestampUtc ?? visit.LastSeenAtUtc;
        visit.ResolvedSourceTimeMs = observation?.SourceTimeMs ?? visit.LastSourceTimeMs;
        visit.EstimatedCompletedAtUtc = status == VisitStatus.EstimatedCompleted ? visit.ResolvedAtUtc : null;
        visit.UpdatedAtUtc = now;
    }

    public static double? EstimatedAverage(double total, int count) => count == 0 ? null : total / count;
}
