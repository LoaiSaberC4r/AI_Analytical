using System.Text.Json.Serialization;

namespace CameraAnalytics.Application.Abstractions;

public sealed record VisualizationPoint(PeriodPoint Period, double? Value, MetricQuality Quality);
public sealed record VisualizationSeries(string SeriesId, string Label, MetricScope Scope, IReadOnlyList<VisualizationPoint> Points);
public sealed record VisualizationBin(string Label, double? LowerSeconds, double? UpperSeconds, double? Value, MetricQuality Quality);
public sealed record HistogramSeries(string SeriesId, string Label, MetricScope Scope, IReadOnlyList<VisualizationBin> Bins);
public sealed record GaugeThreshold(string Label, double Value);
public sealed record GaugeValue(MetricScope Scope, string Label, double? Value, double Minimum, double Maximum,
    MetricQuality Quality, IReadOnlyList<GaugeThreshold> Thresholds);
public sealed record TimelineItem(MetricScope Scope, string Label, PeriodPoint Period, string Severity, string Status,
    string? ResolutionReason, double ObservedSeconds, MetricQuality Quality);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "visualizationType")]
[JsonDerivedType(typeof(LineVisualization), "line")]
[JsonDerivedType(typeof(AreaVisualization), "area")]
[JsonDerivedType(typeof(GroupedBarVisualization), "groupedBar")]
[JsonDerivedType(typeof(DivergingBarVisualization), "divergingBar")]
[JsonDerivedType(typeof(HistogramVisualization), "histogram")]
[JsonDerivedType(typeof(GaugeVisualization), "gauge")]
[JsonDerivedType(typeof(TimelineVisualization), "timeline")]
[JsonDerivedType(typeof(HeatmapVisualization), "heatmap")]
public abstract record VisualizationResponse(string MetricKey, string Title, string Unit, ReportPeriod Period)
{
    public int SchemaVersion => 1;
    public string SourceType => Period.SourceType;
    public string TimeBasis => Period.TimeBasis;
    public string Interval => Period.Interval;
    public int RefreshSeconds { get; init; }
    public MetricScope? Scope { get; init; }
    public required MetricQuality Quality { get; init; }
}

public abstract record SeriesVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<VisualizationSeries> Series) : VisualizationResponse(MetricKey, Title, Unit, Period);
public sealed record LineVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<VisualizationSeries> Series) : SeriesVisualization(MetricKey, Title, Unit, Period, Series);
public sealed record AreaVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<VisualizationSeries> Series) : SeriesVisualization(MetricKey, Title, Unit, Period, Series);
public sealed record GroupedBarVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<VisualizationSeries> Series) : SeriesVisualization(MetricKey, Title, Unit, Period, Series);
public sealed record DivergingBarVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<VisualizationSeries> Series) : SeriesVisualization(MetricKey, Title, Unit, Period, Series);
public sealed record HistogramVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<HistogramSeries> Series) : VisualizationResponse(MetricKey, Title, Unit, Period);
public sealed record GaugeVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<GaugeValue> Values) : VisualizationResponse(MetricKey, Title, Unit, Period);
public sealed record TimelineVisualization(string MetricKey, string Title, string Unit, ReportPeriod Period,
    IReadOnlyList<TimelineItem> Items) : VisualizationResponse(MetricKey, Title, Unit, Period);
