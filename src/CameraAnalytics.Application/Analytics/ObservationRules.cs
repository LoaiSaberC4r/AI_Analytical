using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Application.Analytics;

public static class ObservationRules
{
    public static bool IsBeforeCurrentSession(CameraObservation data, AnalyticsSession current) =>
        data.SourceTimestampUtc < current.LastSourceTimestampUtc;

    public static bool IsOrdered(CameraObservation data, AnalyticsSession session) =>
        session.EndedAtUtc is null && data.Sequence > session.LastSequence
        && data.SourceTimestampUtc >= session.LastSourceTimestampUtc
        && (!data.IsVideo || data.SourceTimeMs is null || session.LastSourceTimeMs is null
            || data.SourceTimeMs >= session.LastSourceTimeMs);

    public static double Elapsed(CameraObservation data, DateTime previousUtc, long? previousSourceMs)
    {
        if (!data.IsVideo)
            return (data.SourceTimestampUtc - previousUtc).TotalSeconds;
        return data.SourceTimeMs.HasValue && previousSourceMs.HasValue
            ? (data.SourceTimeMs.Value - previousSourceMs.Value) / 1000d : double.PositiveInfinity;
    }

    public static DataFreshness Freshness(DateTime now, DateTime? latest, AnalyticsOptions options)
    {
        if (!latest.HasValue)
            return DataFreshness.NeverReceived;
        var seconds = (now - latest.Value).TotalSeconds;
        return seconds >= options.NoRecentDataAfterSeconds ? DataFreshness.NoRecentData
            : seconds >= options.StaleAfterSeconds ? DataFreshness.Stale : DataFreshness.Fresh;
    }

    public static double? Utilization(double? occupancy, int? capacity) =>
        capacity > 0 ? occupancy / capacity * 100 : null;
}
