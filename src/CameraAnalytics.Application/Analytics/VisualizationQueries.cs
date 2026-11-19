using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Analytics;

public sealed class VisualizationQueries(ISmartQueries queries, VisualizationOptions options)
{
    private static readonly MetricQuality Empty = new("InsufficientData", 0, 0, 0, 0, 0, 0, true, false, ["insufficient_data"]);

    public async Task<VisualizationResponse> Get(string metric, string? type, ReportFilter filter, CancellationToken ct)
    {
        var definition = Describe(metric);
        type ??= definition.Types[0];
        if (!definition.Types.Contains(type, StringComparer.Ordinal))
            throw ApiError.Invalid("The visualization type is not supported for this metric.");
        if (type == "histogram")
        {
            var report = await queries.Dwell(filter, ct);
            var active = metric == "activeDwell";
            var histograms = report.Zones.Select(z =>
            {
                var quality = active ? z.ActiveQuality : z.CompletedQuality;
                return new HistogramSeries(z.Scope.ZoneId!.Value.ToString(), z.ZoneName, z.Scope,
                    (active ? z.ActiveBins : z.EstimatedCompletedBins)?.Select(bin => new VisualizationBin(
                        bin.UpperSeconds.HasValue ? $"{bin.LowerSeconds:g}–{bin.UpperSeconds:g} s" : $"{bin.LowerSeconds:g}+ s",
                        bin.LowerSeconds, bin.UpperSeconds, bin.Count, quality)).ToArray() ?? []);
            }).ToArray();
            return new HistogramVisualization(metric, definition.Title, definition.Unit, report.Period, histograms)
            {
                RefreshSeconds = options.DefaultRefreshSeconds,
                Scope = report.Scope,
                Quality = Summary(report.Zones.Select(z => active ? z.ActiveQuality : z.CompletedQuality))
            };
        }
        if (metric == "dataQualityTimeline")
        {
            var report = await queries.Quality(filter, ct);
            var observations = report.Cameras.SelectMany(camera => camera.Buckets.Select(bucket => new TimelineItem(camera.Scope,
                camera.CameraName, bucket.Period, bucket.Quality.GapCount > 0 || bucket.Quality.ResetCount > 0 ? "Warning" : "Info",
                bucket.Quality.SampleCount == 0 && bucket.Quality.CoveredSeconds == 0 ? "NoObservation"
                    : bucket.Quality.GapCount > 0 ? "SequenceGap" : bucket.Quality.ResetCount > 0 ? "CounterReset"
                    : bucket.Quality.IsPartial ? "PartialCoverage" : "Observed", null, bucket.Quality.CoveredSeconds, bucket.Quality))).ToArray();
            return new TimelineVisualization(metric, definition.Title, definition.Unit, report.Period,
                observations.OrderBy(x => x.Period.StartUtc).ThenBy(x => x.Period.StartSourceTimeMs).Take(options.MaxPointsPerSeries).ToArray())
                { Quality = Summary(observations.Select(x => x.Quality)), Scope = report.Scope, RefreshSeconds = options.DefaultRefreshSeconds };
        }
        if (type is "gauge" or "timeline")
        {
            var report = await queries.Congestion(filter, ct);
            var quality = Summary(report.Zones.Select(x => x.Quality));
            return type == "gauge"
                ? new GaugeVisualization(metric, definition.Title, definition.Unit, report.Period, report.Zones.Select(Gauge).ToArray())
                    { Quality = quality, Scope = report.Scope, RefreshSeconds = options.DefaultRefreshSeconds }
                : new TimelineVisualization(metric, definition.Title, definition.Unit, report.Period, report.Zones.SelectMany(zone =>
                    zone.Episodes.Select(x => new TimelineItem(zone.Scope, zone.ZoneName, x.Period, x.Severity, x.Status,
                        x.ResolutionReason, x.AboveWarningSeconds, zone.Quality))).OrderBy(x => x.Period.StartUtc)
                    .ThenBy(x => x.Period.StartSourceTimeMs).Take(options.MaxPointsPerSeries).ToArray())
                    { Quality = quality, Scope = report.Scope, RefreshSeconds = options.DefaultRefreshSeconds };
        }

        ReportPeriod period;
        MetricScope? scope;
        VisualizationSeries[] series;
        if (metric is "staff" or "fps" or "missingSequences")
        {
            var report = await queries.Quality(filter, ct);
            period = report.Period;
            scope = report.Scope;
            series = report.Cameras.Select(c => new VisualizationSeries(c.Scope.CameraId!.Value.ToString(), c.CameraName, c.Scope,
                c.Buckets.Select(x => Point(x.Period, metric switch
                {
                    "staff" => x.AverageStaffCount,
                    "fps" => x.AverageFps,
                    _ => (double?)x.MissingSequences
                }, x.Quality)).ToArray())).ToArray();
        }
        else
        {
            var report = await queries.Flow(filter, ct);
            period = report.Period;
            scope = report.Scope;
            series = (metric switch
            {
                "entryExit" => report.Zones.SelectMany(z => new[] { ZoneSeries(z, metric, "Entry", x => (double?)x.ObservedCounts?.Entries),
                    ZoneSeries(z, metric, "Exit", x => (double?)x.ObservedCounts?.Exits) }),
                "wrongWay" => report.Zones.SelectMany(z => new[] { ZoneSeries(z, metric, "In", x => (double?)x.ObservedCounts?.WrongWayIn),
                    ZoneSeries(z, metric, "Out", x => (double?)x.ObservedCounts?.WrongWayOut) }),
                _ => report.Zones.Select(z => ZoneSeries(z, metric, null, x => FlowValue(metric, x)))
            }).ToArray();
        }
        if (series.Length > options.MaxSeries)
            throw ApiError.Invalid("The requested chart has too many series. Narrow the scope.");
        var overview = Summary(series.SelectMany(x => x.Points.Select(p => p.Quality)));
        return type switch
        {
            "line" => new LineVisualization(metric, definition.Title, definition.Unit, period, series)
                { Quality = overview, Scope = scope, RefreshSeconds = options.DefaultRefreshSeconds },
            "area" => new AreaVisualization(metric, definition.Title, definition.Unit, period, series)
                { Quality = overview, Scope = scope, RefreshSeconds = options.DefaultRefreshSeconds },
            "groupedBar" => new GroupedBarVisualization(metric, definition.Title, definition.Unit, period, series)
                { Quality = overview, Scope = scope, RefreshSeconds = options.DefaultRefreshSeconds },
            _ => new DivergingBarVisualization(metric, definition.Title, definition.Unit, period, series)
                { Quality = overview, Scope = scope, RefreshSeconds = options.DefaultRefreshSeconds }
        };
    }

    private static GaugeValue Gauge(CongestionZone zone)
    {
        var thresholds = new List<GaugeThreshold>();
        if (zone.LastObservedWarningThreshold is { } warning) thresholds.Add(new("Warning at latest observation", warning));
        if (zone.LastObservedCriticalThreshold is { } critical) thresholds.Add(new("Critical at latest observation", critical));
        var maximum = Math.Max(100, Math.Max(zone.PeakUtilizationPercent ?? 0, zone.LastObservedCriticalThreshold ?? 0));
        return new(zone.Scope, zone.ZoneName, zone.AverageUtilizationPercent, 0, maximum, zone.Quality, thresholds);
    }

    private static VisualizationSeries ZoneSeries(ZoneFlow zone, string metric, string? label, Func<FlowBucket, double?> value) =>
        new($"{zone.Scope.ZoneId}:{metric}:{label}", label is null ? zone.ZoneName : $"{zone.ZoneName} ({label})", zone.Scope,
            zone.Buckets.Select(x => Point(x.Period, value(x), x.Quality)).ToArray());

    private static VisualizationPoint Point(PeriodPoint period, double? value, MetricQuality quality) => new(period, value,
        value.HasValue ? quality : quality with { Provenance = "InsufficientData" });

    private static double? FlowValue(string metric, FlowBucket b) => metric switch
    {
        "arrivalsPerMinute" => b.EventsPerMinute?.Arrivals,
        "departuresPerMinute" => b.EventsPerMinute?.Departures,
        "arrivals" => (double?)b.ObservedCounts?.Arrivals,
        "averageOccupancy" => b.AverageOccupancy,
        "peakOccupancy" => b.PeakOccupancy,
        "trafficBalance" => (double?)b.TrafficBalance,
        _ => null
    };

    public static MetricQuality Summary(IEnumerable<MetricQuality> rows)
    {
        var values = rows.ToArray();
        var available = values.FirstOrDefault(x => x.Provenance != "InsufficientData");
        if (available is null)
            return Empty with { WarningCodes = values.SelectMany(x => x.WarningCodes).Append("insufficient_data").Distinct().ToArray() };
        return new(available.Provenance, values.Sum(x => x.SampleCount), values.Sum(x => x.CoveredSeconds), values.Average(x => x.CoveragePercent),
            values.Sum(x => x.GapCount), values.Sum(x => x.ResetCount), values.Sum(x => x.BaselineCount), values.Any(x => x.IsPartial),
            values.Any(x => x.IsStale), values.SelectMany(x => x.WarningCodes).Distinct().ToArray());
    }

    private static (string Title, string Unit, string[] Types) Describe(string metric) => metric switch
    {
        "arrivalsPerMinute" => ("Arrival rate", "events/min", ["line", "area"]),
        "departuresPerMinute" => ("Departure rate", "events/min", ["line", "area"]),
        "arrivals" => ("Observed arrivals", "events", ["line", "area", "groupedBar"]),
        "entryExit" => ("Entry and exit", "events", ["groupedBar"]),
        "wrongWay" => ("Wrong-way movements", "events", ["line", "groupedBar"]),
        "trafficBalance" => ("Traffic balance", "events", ["divergingBar"]),
        "averageOccupancy" => ("Weighted zone occupancy", "people per zone", ["area", "line"]),
        "peakOccupancy" => ("Peak zone occupancy", "people per zone", ["line"]),
        "staff" => ("Weighted camera staff count", "staff", ["line", "area"]),
        "fps" => ("Camera processing FPS", "frames/s", ["line"]),
        "missingSequences" => ("Missing message sequences", "sequences", ["groupedBar"]),
        "activeDwell" => ("Active tracked dwell", "visits", ["histogram"]),
        "completedDwell" => ("Estimated completed dwell", "visits", ["histogram"]),
        "utilization" => ("Weighted utilization", "%", ["gauge"]),
        "congestion" => ("Congestion episodes", "observed seconds", ["timeline"]),
        "dataQualityTimeline" => ("Camera observation timeline", "covered seconds", ["timeline"]),
        _ => throw ApiError.Invalid("The visualization metric is not supported.")
    };
}
