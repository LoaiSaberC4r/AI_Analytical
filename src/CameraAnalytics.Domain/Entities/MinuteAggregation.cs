namespace CameraAnalytics.Domain.Entities;

// Minute aggregation applies to live event time only.
public sealed class ZoneMinuteAggregate : ZoneAggregateValues
{
    public Guid CameraZoneId { get; set; }
    public DateTime MinuteStartUtc { get; set; }
    public double? CapacityCoveredSeconds { get; set; }
    public double? AverageUtilizationPercent { get; set; }
    public double? AboveWarningSeconds { get; set; }
    public double? AboveCriticalSeconds { get; set; }
}

public sealed class CameraMinuteAggregate
{
    public Guid CameraId { get; set; }
    public DateTime MinuteStartUtc { get; set; }
    public double ObservationDurationSeconds { get; set; }
    public double? AverageStaffCount { get; set; }
    public int? PeakStaffCount { get; set; }
    public double? MinimumFps { get; set; }
    public double? MaximumFps { get; set; }
    public double? AverageFps { get; set; }
    public decimal? MissingSequences { get; set; }
    public int GapCount { get; set; }
    public int SampleCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DirtyZoneMinute
{
    public Guid CameraZoneId { get; set; }
    public DateTime MinuteStartUtc { get; set; }
    public DateTime DirtyAtUtc { get; set; }
}

public sealed class DirtyCameraMinute
{
    public Guid CameraId { get; set; }
    public DateTime MinuteStartUtc { get; set; }
    public DateTime DirtyAtUtc { get; set; }
}

public sealed class MinuteBackfillState
{
    public int Id { get; set; }
    public DateTime NextMinuteUtc { get; set; }
    public DateTime EndUtc { get; set; }
}
