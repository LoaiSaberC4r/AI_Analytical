using System.Diagnostics.Metrics;

namespace CameraAnalytics.Infrastructure.Analytics;

public static class AnalyticsTelemetry
{
    public const string MeterName = "CameraAnalytics";
    private static readonly Meter Meter = new(MeterName, "1.0");
    public static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("ca.mqtt.deliveries");
    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("ca.mqtt.retries");
    public static readonly Gauge<int> Connected = Meter.CreateGauge<int>("ca.mqtt.connected");
    public static readonly Gauge<int> Subscribed = Meter.CreateGauge<int>("ca.mqtt.subscribed");
    public static readonly Histogram<double> ReceiptToAck = Meter.CreateHistogram<double>("ca.mqtt.receipt_to_ack", "ms");
    public static readonly Histogram<double> ReceiptToCommit = Meter.CreateHistogram<double>("ca.ingest.receipt_to_commit", "ms");
    public static readonly Histogram<double> SourceToCommit = Meter.CreateHistogram<double>("ca.ingest.source_to_commit", "ms");
    public static readonly Gauge<int> DirtyMinutes = Meter.CreateGauge<int>("ca.rollup.dirty_minutes");
    public static readonly Gauge<int> DirtyHours = Meter.CreateGauge<int>("ca.rollup.dirty_hours");
    public static readonly Histogram<double> MinuteLag = Meter.CreateHistogram<double>("ca.rollup.minute_lag", "s");
    public static readonly Counter<long> AlertTransitions = Meter.CreateCounter<long>("ca.alert.transitions");
    public static readonly Counter<long> RealtimeSignals = Meter.CreateCounter<long>("ca.realtime.signals");
}
