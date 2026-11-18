namespace CameraAnalytics.Application.Common;

public sealed class DatabaseOptions
{
    public bool ApplyMigrationsOnStartup { get; init; } = true;
    public int CommandTimeoutSeconds { get; init; } = 30;
    public bool EnableSensitiveDataLogging { get; init; }
}

public sealed class MqttOptions
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 1883;
    public string ProtocolVersion { get; init; } = "3.1.1";
    public string TopicFilter { get; init; } = "";
    public string ClientId { get; init; } = "";
    public int Qos { get; init; } = 1;
    public bool PersistentSession { get; init; } = true;
    public uint SessionExpirySeconds { get; init; } = 604800;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseTls { get; init; }
    public int KeepAliveSeconds { get; init; } = 30;
    public int ConnectTimeoutSeconds { get; init; } = 10;
    public int ReconnectDelaySeconds { get; init; } = 5;
    public int MaxPayloadBytes { get; init; } = 1_048_576;
    public int MaxSourceNameLength { get; init; } = 512;
    public int MaxZonesPerMessage { get; init; } = 256;
    public int MaxActiveVisitorsPerZone { get; init; } = 4096;
}

public sealed class AnalyticsOptions
{
    public int ExpectedMessageIntervalSeconds { get; init; } = 1;
    public int StaleAfterSeconds { get; init; } = 5;
    public int NoRecentDataAfterSeconds { get; init; } = 15;
    public int TrackMissingGraceSeconds { get; init; } = 5;
    public int MaximumSnapshotGapSeconds { get; init; } = 5;
    public double DefaultWarningThresholdPercent { get; init; } = 80;
    public double DefaultCriticalThresholdPercent { get; init; } = 100;
    public string DefaultAggregationInterval { get; init; } = "hour";
    public int MaximumReportRangeDays { get; init; } = 90;
    public int MaximumMinuteReportRangeDays { get; init; } = 7;
    public double MinimumCoveragePercentForPeakRanking { get; init; } = 80;
    public int ClockSkewToleranceSeconds { get; init; } = 10;
}

public sealed class AggregationOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 30;
    public int BatchSize { get; init; } = 50;
}

public sealed class VisualizationOptions
{
    public int MaxSeries { get; init; } = 30;
    public int MaxPointsPerSeries { get; init; } = 1500;
    public int DefaultRefreshSeconds { get; init; } = 5;
    public double MinimumRateCoveragePercent { get; init; } = 80;
}

public sealed class CongestionOptions
{
    public int MinObservedDurationSeconds { get; init; } = 30;
    public int RecoveryObservedSeconds { get; init; } = 15;
    public int MaxObservationGapSeconds { get; init; } = 5;
}

public sealed class DwellAnalyticsOptions
{
    public double[]? HistogramUpperBoundsSeconds { get; init; }
    public double[] HistogramBounds => HistogramUpperBoundsSeconds ?? [30, 60, 180, 300, 600];
    public int DefaultLongStaySeconds { get; init; } = 300;
}

public sealed class RetentionOptions
{
    public bool Enabled { get; init; }
    public int RawSnapshotsDays { get; init; } = 7;
    public int ProcessedMessagesDays { get; init; } = 30;
    public int HourlyAggregatesDays { get; init; } = 365;
    public int MinuteAggregatesDays { get; init; } = 90;
    public int AlertHistoryDays { get; init; } = 365;
}

public sealed class MinuteAggregationOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 15;
    public int BatchSize { get; init; } = 100;
    public int FinalizationDelaySeconds { get; init; } = 5;
    public int BackfillDays { get; init; } = 7;
}

public sealed class FloorPlanOptions
{
    public int MaxPolygonVertices { get; init; } = 100;
    public int MaxLayoutsPerFloor { get; init; } = 100;
}

public sealed class AlertOptions
{
    public bool Enabled { get; init; } = true;
    public int EvaluateEverySeconds { get; init; } = 5;
    public double MinimumCoveragePercent { get; init; } = 80;
    public int DefaultCooldownSeconds { get; init; } = 300;
    public int MaxRules { get; init; } = 500;
}

public sealed class ForecastOptions
{
    public bool Enabled { get; init; } = true;
    public int MinHistoryDays { get; init; } = 28;
    public int PreferredLookbackDays { get; init; } = 56;
    public int MinComparableSlots { get; init; } = 4;
    public double MinimumCoveragePercent { get; init; } = 80;
    public int HorizonHours { get; init; } = 24;
}
