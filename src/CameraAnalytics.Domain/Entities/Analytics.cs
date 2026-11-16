using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Domain.Entities;

public sealed class AnalyticsSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CameraId { get; set; }
    public Camera Camera { get; set; } = null!;
    public string ExternalSessionId { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Status { get; set; } = "";
    public long FirstSequence { get; set; }
    public long LastSequence { get; set; }
    public long? FirstSourceTimeMs { get; set; }
    public long? LastSourceTimeMs { get; set; }
    public long? LastVideoLoop { get; set; }
    public DateTime FirstSourceTimestampUtc { get; set; }
    public DateTime LastSourceTimestampUtc { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastReceivedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ReceivedAnalyticsMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AnalyticsSessionId { get; set; }
    public long Sequence { get; set; }
    public string Topic { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public DateTime SourceTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public MessageStatus ProcessingStatus { get; set; }
    public string? RejectionReason { get; set; }
}

public sealed class IngestionRejection
{
    public long Id { get; set; }
    public string Topic { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }
}

public sealed class CameraSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReceivedMessageId { get; set; }
    public Guid CameraId { get; set; }
    public Guid AnalyticsSessionId { get; set; }
    public AnalyticsSession Session { get; set; } = null!;
    public long Sequence { get; set; }
    public long? SourceTimeMs { get; set; }
    public long? VideoLoop { get; set; }
    public DateTime SourceTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public double ProcessingFps { get; set; }
    public int StaffCount { get; set; }
    public string AnalyticsStatus { get; set; } = "";
    public bool IsOrdered { get; set; }
    public DateTime? ObservationEndUtc { get; set; }
    public long? ObservationEndSourceMs { get; set; }
}

public sealed class ZoneSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReceivedMessageId { get; set; }
    public Guid AnalyticsSessionId { get; set; }
    public AnalyticsSession Session { get; set; } = null!;
    public Guid CameraZoneId { get; set; }
    public long Sequence { get; set; }
    public long? SourceTimeMs { get; set; }
    public long? VideoLoop { get; set; }
    public DateTime SourceTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public int Occupancy { get; set; }
    public long ZoneArrivals { get; set; }
    public long ZoneDepartures { get; set; }
    public long Entry { get; set; }
    public long Exit { get; set; }
    public long WrongWayIn { get; set; }
    public long WrongWayOut { get; set; }
    public double ReportedAverageDwellSeconds { get; set; }
    public int ActiveVisitorsCount { get; set; }
    public double? LongestActiveDwellSeconds { get; set; }
    public bool IsOrdered { get; set; }
    public DateTime? ObservationEndUtc { get; set; }
    public long? ObservationEndSourceMs { get; set; }
}

public sealed class CameraCurrentState
{
    public Guid CameraId { get; set; }
    public Camera Camera { get; set; } = null!;
    public Guid AnalyticsSessionId { get; set; }
    public long LastSequence { get; set; }
    public long? LastVideoLoop { get; set; }
    public string AnalyticsStatus { get; set; } = "";
    public string SourceType { get; set; } = "";
    public double ProcessingFps { get; set; }
    public int StaffCount { get; set; }
    public DateTime LastSourceTimestampUtc { get; set; }
    public DateTime LastReceivedAtUtc { get; set; }
    public long StateVersion { get; set; }
    public DataFreshness DataFreshness { get; set; }
}

public sealed class ZoneCurrentState
{
    public Guid CameraZoneId { get; set; }
    public Guid AnalyticsSessionId { get; set; }
    public long LastSequence { get; set; }
    public long StateVersion { get; set; }
    public int Occupancy { get; set; }
    public long ZoneArrivals { get; set; }
    public long ZoneDepartures { get; set; }
    public long Entry { get; set; }
    public long Exit { get; set; }
    public long WrongWayIn { get; set; }
    public long WrongWayOut { get; set; }
    public double ReportedAverageDwellSeconds { get; set; }
    public int ActiveVisitorsCount { get; set; }
    public double? LongestActiveDwellSeconds { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }
}

public sealed class ZoneCounterState
{
    public Guid LastReceivedMessageId { get; set; }
    public Guid AnalyticsSessionId { get; set; }
    public Guid CameraZoneId { get; set; }
    public long LastSequence { get; set; }
    public long? LastSourceTimeMs { get; set; }
    public long? LastVideoLoop { get; set; }
    public DateTime LastSourceTimestampUtc { get; set; }
    public long ZoneArrivals { get; set; }
    public long ZoneDepartures { get; set; }
    public long Entry { get; set; }
    public long Exit { get; set; }
    public long WrongWayIn { get; set; }
    public long WrongWayOut { get; set; }
}

public sealed class ZoneCounterDelta
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AnalyticsSessionId { get; set; }
    public AnalyticsSession Session { get; set; } = null!;
    public Guid CameraZoneId { get; set; }
    public Guid ReceivedMessageId { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public long? VideoLoop { get; set; }
    public long? FromSourceTimeMs { get; set; }
    public long? ToSourceTimeMs { get; set; }
    public long ZoneArrivalsDelta { get; set; }
    public long ZoneDeparturesDelta { get; set; }
    public long EntryDelta { get; set; }
    public long ExitDelta { get; set; }
    public long WrongWayInDelta { get; set; }
    public long WrongWayOutDelta { get; set; }
    public CounterQuality CounterQuality { get; set; }
    public int ResetMask { get; set; }
    public bool HasObservationGap { get; set; }
}

public sealed class TrackedVisit
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CameraId { get; set; }
    public Guid CameraZoneId { get; set; }
    public Guid AnalyticsSessionId { get; set; }
    public AnalyticsSession Session { get; set; } = null!;
    public long ExternalTrackId { get; set; }
    public long? VideoLoop { get; set; }
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public long? FirstSourceTimeMs { get; set; }
    public long? LastSourceTimeMs { get; set; }
    public long? ResolvedSourceTimeMs { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public double ObservedMissingSeconds { get; set; }
    public double LastDwellSeconds { get; set; }
    public double MaximumObservedDwellSeconds { get; set; }
    public VisitStatus Status { get; set; }
    public string? CompletionReason { get; set; }
    public DateTime? EstimatedCompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ZoneHourlyAggregate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CameraZoneId { get; set; }
    public string SourceType { get; set; } = "";
    public DateTime HourStartUtc { get; set; }
    public double ObservationDurationSeconds { get; set; }
    public double? AverageOccupancy { get; set; }
    public int? PeakOccupancy { get; set; }
    public DateTime? PeakAtUtc { get; set; }
    public decimal ArrivalsDelta { get; set; }
    public decimal DeparturesDelta { get; set; }
    public decimal EntryDelta { get; set; }
    public decimal ExitDelta { get; set; }
    public decimal WrongWayInDelta { get; set; }
    public decimal WrongWayOutDelta { get; set; }
    public double? LatestReportedDwellSeconds { get; set; }
    public DateTime? LatestReportedAtUtc { get; set; }
    public double? LongestActiveDwellSeconds { get; set; }
    public int CounterBaselineCount { get; set; }
    public int CounterResetCount { get; set; }
    public int GapCount { get; set; }
    public int SampleCount { get; set; }
    public int UncertainVisits { get; set; }
    public int EstimatedCompletedVisits { get; set; }
    public double? EstimatedTotalDwellSeconds { get; set; }
    public double CoveragePercent { get; set; }
    public string DataQuality { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DirtyZoneHour
{
    public Guid CameraZoneId { get; set; }
    public string SourceType { get; set; } = "";
    public DateTime HourStartUtc { get; set; }
    public DateTime DirtyAtUtc { get; set; }
}
