using System.Security.Cryptography;
using System.Text;
using CameraAnalytics.Application.Abstractions;

namespace CameraAnalytics.Application.Analytics;

public sealed record InsightCard(string InsightKey, string Severity, string Title, string Description, string MetricKey,
    double Value, string Unit, MetricScope Scope, ReportPeriod Period, PeriodPoint ObservationWindow, MetricQuality Quality,
    DateTime GeneratedAtUtc);
public sealed record InsightsReport(ReportPeriod Period, IReadOnlyList<InsightCard> Items, MetricQuality Quality);

public sealed class InsightRulesEngine(ISmartQueries queries, TimeProvider clock, double minimumCoverage)
{
    public async Task<InsightsReport> Get(ReportFilter filter, CancellationToken ct)
    {
        var flow = await queries.Flow(filter, ct);
        var congestion = await queries.Congestion(filter, ct);
        var dwell = await queries.Dwell(filter, ct);
        return Generate(flow, congestion, dwell, clock.GetUtcNow().UtcDateTime, minimumCoverage);
    }

    public static InsightsReport Generate(FlowReport flow, CongestionReport congestion, DwellDistributionReport dwell,
        DateTime generated, double minimum)
    {
        var cards = new List<InsightCard>();
        var eligible = flow.Zones.Where(z => Qualified(z.Total.Quality)).ToArray();
        var arrivals = eligible.Where(z => z.Total.ObservedCounts?.Arrivals > 0)
            .OrderByDescending(z => z.Total.ObservedCounts!.Arrivals).ThenBy(z => z.Scope.ZoneId).FirstOrDefault();
        if (arrivals is not null)
            Add("arrivals", "Most observed arrivals", $"{arrivals.ZoneName} recorded the most arrivals among eligible zones.",
                "Info", (double)arrivals.Total.ObservedCounts!.Arrivals, "events", arrivals.Scope, arrivals.Total.Period, arrivals.Total.Quality);
        var peak = flow.Zones.SelectMany(z => z.Buckets.Select(x => (Zone: z, Bucket: x)))
            .Where(x => Qualified(x.Bucket.Quality) && x.Bucket.AverageOccupancy > 0)
            .OrderByDescending(x => x.Bucket.AverageOccupancy).ThenBy(x => x.Zone.Scope.ZoneId).FirstOrDefault();
        if (peak.Bucket is not null)
            Add("averageOccupancy", "Busiest measured interval", $"{peak.Zone.ZoneName} had the highest eligible weighted occupancy.",
                "Info", peak.Bucket.AverageOccupancy!.Value, "people per zone", peak.Zone.Scope, peak.Bucket.Period, peak.Bucket.Quality);
        foreach (var zone in congestion.Zones.Where(x => Qualified(x.Quality)))
        {
            var episode = zone.Episodes.Where(x => x.Status != "Uncertain").OrderByDescending(x => x.AboveWarningSeconds).FirstOrDefault();
            if (episode is not null)
                Add("congestion", "Sustained congestion", $"{zone.ZoneName} sustained utilization at or above its warning threshold.",
                    episode.Severity, episode.AboveWarningSeconds, "observed seconds", zone.Scope, episode.Period, zone.Quality);
        }
        foreach (var zone in dwell.Zones.Where(x => !x.ActiveQuality.IsStale && x.LongStayActiveCount > 0))
        {
            var at = zone.ActiveObservedAtUtc.HasValue ? new PeriodPoint(zone.ActiveObservedAtUtc, null, null, null, null) : null;
            if (at is not null)
                Add("longStay", "Active long stays", $"Tracked visits in {zone.ZoneName} currently exceed the long-stay threshold.",
                    "Warning", zone.LongStayActiveCount!.Value, "tracked visits", zone.Scope, at, zone.ActiveQuality);
        }
        var quality = VisualizationQueries.Summary(cards.Select(x => x.Quality));
        return new(flow.Period, cards, quality);

        bool Qualified(MetricQuality quality) => quality.Provenance != "InsufficientData" && !quality.IsStale
            && quality.CoveragePercent >= minimum && quality.GapCount == 0 && quality.ResetCount == 0;

        void Add(string metric, string title, string description, string severity, double value, string unit,
            MetricScope scope, PeriodPoint window, MetricQuality evidence)
        {
            var canonical = FormattableString.Invariant($"{metric}|{scope.SiteId}|{scope.CameraId}|{scope.ZoneId}|{flow.Period.SourceType}|{flow.Period.SessionId}|{flow.Period.VideoLoop}|{window.StartUtc:O}|{window.EndUtc:O}|{window.StartSourceTimeMs}|{window.EndSourceTimeMs}");
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            cards.Add(new(key, severity, title, description, metric, value, unit, scope, flow.Period, window, evidence, generated));
        }
    }
}
