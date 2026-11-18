namespace CameraAnalytics.Application.Abstractions;

public sealed record CameraObservation(string ExternalCameraId, string SourceType, string SourceName,
    string ExternalSessionId, long? SourceTimeMs, double ProcessingFps, int StaffCount, string Status,
    long Sequence, DateTime SourceTimestampUtc, IReadOnlyList<ZoneObservation> Zones, long? VideoLoop = null,
    ReportedAnalyticsMetadata? ReportedMetadata = null)
{
    public bool IsVideo => SourceType == "video_file";
    public bool IsLive => SourceType == "live";
}

public sealed record ZoneObservation(string ExternalZoneId, int Occupancy, Counters Counters,
    double AverageDwellSeconds, IReadOnlyList<VisitorObservation> Visitors, ReportedAnalyticsMetadata? ReportedMetadata = null)
{
    public double? LongestActiveDwell => Visitors.Count == 0 ? null : Visitors.Max(x => x.DwellSeconds);
}

public sealed record ReportedCounts(long? NormalIn, long? NormalOut, long? ActualGateIn, long? ActualGateOut,
    long? GateBalanceDelta, long? UnverifiedLosses, long? LoopBoundaryClosures, long? ExclusionRemovals, long? ZoneBalanceDelta);
public sealed record ReportedAnalyticsMetadata(ReportedCounts? Counts, ReportedCounts? Totals);

public sealed record VisitorObservation(long TrackId, double DwellSeconds);
public readonly record struct Counters(long Arrivals, long Departures, long Entry, long Exit, long WrongWayIn, long WrongWayOut);
public readonly record struct PayloadRead(CameraObservation? Observation, string? Rejection);
public enum IngestResult { Accepted, HistoricalOnly, Duplicate, Rejected }

public interface IMqttPayloadReader
{
    PayloadRead Read(string topic, ReadOnlySpan<byte> payload);
}

public interface IAnalyticsIngestor
{
    Task<IngestResult> Process(string topic, ReadOnlyMemory<byte> payload, DateTime receivedAtUtc, CancellationToken ct);
}
