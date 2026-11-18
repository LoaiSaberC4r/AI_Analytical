using System.Globalization;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Api.Controllers;

[ApiController]
[Route("api/v1/analytics")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(404)]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = Extensions.SiteAccess.Administration)]
public sealed class AnalyticsController(IReportQueries reports, IOptions<AnalyticsOptions> options) : ControllerBase
{
    private double MinimumCoverage => options.Value.MinimumCoveragePercentForPeakRanking;

    [HttpGet("overview")]
    public async Task<OverviewReport> Overview([FromQuery] ReportFilter filter, CancellationToken ct) =>
        ReportSummaries.Overview(await reports.Zones(filter, false, ct));

    [HttpGet("zones/rankings")]
    public async Task<RankingReport> Ranking([FromQuery] ReportFilter filter, CancellationToken ct, string metric = "arrivals") =>
        ReportRanking.Rank(await reports.Zones(filter, false, ct), metric, 0);

    [HttpGet("zones/{zoneId:guid}/summary")]
    public async Task<ZoneResult> Summary(Guid zoneId, [FromQuery] ReportFilter filter, CancellationToken ct) =>
        (await reports.Zones(filter with { ZoneId = zoneId }, false, ct)).Zones.Single();

    [HttpGet("zones/{zoneId:guid}/timeseries")]
    public Task<ReportResult> Timeseries(Guid zoneId, [FromQuery] ReportFilter filter, CancellationToken ct) =>
        reports.Zones(filter with { ZoneId = zoneId }, true, ct);

    [HttpGet("occupancy")]
    public async Task<OccupancyReport> Occupancy([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var report = await reports.Zones(filter, true, ct);
        if (report.Period.TimeBasis == "sourceTime")
            filter = filter with { VideoLoop = report.Period.VideoLoop?.ToString(CultureInfo.InvariantCulture) ?? "unmarked" };
        var hourly = report.Period.Interval == "hour" ? report : await reports.Zones(filter with { Interval = "hour" }, true, ct);
        var peaks = ReportRanking.PeakHours(hourly, MinimumCoverage).Zones.ToDictionary(x => x.ZoneId, x => x.Occupancy);
        return new(report.Period, report.Zones.Select(x => new OccupancyZone(x.ZoneId, x.ZoneName, x.AverageOccupancy,
            x.PeakOccupancy, x.PeakAtUtc, x.PeakAtSourceTimeMs, peaks.GetValueOrDefault(x.ZoneId), x.Capacity,
            x.PeakUtilizationPercent, x.DataQuality.CoveragePercent, x.DataQuality.ObservationDurationSeconds, x.DataQuality, x.Series)).ToArray());
    }

    [HttpGet("peak-hours")]
    public async Task<PeakHoursReport> PeakHours([FromQuery] ReportFilter filter, CancellationToken ct) =>
        ReportRanking.PeakHours(await reports.Zones(filter with { Interval = "hour" }, true, ct), MinimumCoverage);

    [HttpGet("occupancy/utilization")]
    public async Task<UtilizationReport> Utilization([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var data = await reports.Zones(filter, false, ct);
        return new(data.Period, data.Zones.Select(x => new UtilizationZone(x.ZoneId, x.ZoneName, x.Capacity,
            x.AverageUtilizationPercent, x.PeakUtilizationPercent, x.DataQuality)).ToArray());
    }

    [HttpGet("traffic")]
    public Task<TrafficReport> Traffic([FromQuery] ReportFilter filter, CancellationToken ct) => TrafficResult(filter, false, ct);

    [HttpGet("traffic/trends")]
    public Task<TrafficReport> Trends([FromQuery] ReportFilter filter, CancellationToken ct) => TrafficResult(filter, true, ct);

    [HttpGet("dwell")]
    public async Task<DwellReport> Dwell([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var data = await reports.Zones(filter, false, ct);
        return new(data.Period, data.Zones.Select(x => new DwellZone(x.ZoneId, x.ZoneName, x.ReportedAverageDwellSeconds,
            x.LongestActiveDwellSeconds, x.EstimatedCompletedVisits, x.EstimatedTotalDwellSeconds,
            x.EstimatedHistoricalAverageDwellSeconds, x.UncertainVisits,
            x.DataQuality with { Kind = x.EstimatedCompletedVisits > 0 || x.UncertainVisits > 0 ? "Estimated" : x.DataQuality.Kind })).ToArray());
    }

    [HttpGet("dwell/rankings")]
    public async Task<RankingReport> DwellRankings([FromQuery] ReportFilter filter, CancellationToken ct, string metric = "estimatedAverageDwell")
    {
        if (metric is not ("reportedAverageDwell" or "estimatedAverageDwell" or "longestActiveDwell"))
            throw ApiError.Invalid("Choose reportedAverageDwell, estimatedAverageDwell or longestActiveDwell.");
        return ReportRanking.Rank(await reports.Zones(filter, false, ct), metric, 0);
    }

    [HttpGet("wrong-way")]
    public async Task<WrongWayReport> WrongWay([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        var report = await reports.Zones(filter, true, ct);
        var total = ReportSummaries.Traffic(report);
        return new(report.Period, total.WrongWayIn, total.WrongWayOut, total.WrongWayEvents,
            ReportRanking.Rank(report, "wrongWayEvents", 0).Items.FirstOrDefault(),
            report.Zones.Select(x => new TrafficZone(x.ZoneId, x.ZoneName, x.Traffic, x.DataQuality, x.Series)).ToArray());
    }

    [HttpGet("staff")]
    public Task<StaffReport> Staff([FromQuery] ReportFilter filter, CancellationToken ct) => reports.Staff(filter, ct);

    [HttpGet("sessions")]
    public Task<Page<SessionData>> Sessions(CancellationToken ct, int page = 1, int pageSize = 20, string? search = null,
        Guid? siteId = null, Guid? cameraId = null, string? sourceType = null) =>
        reports.Sessions(new(page, pageSize, search), siteId, cameraId, sourceType, ct);

    [HttpGet("sessions/{id:guid}")]
    public Task<SessionData> Session(Guid id, CancellationToken ct) => reports.Session(id, ct);

    [HttpGet("sessions/{id:guid}/report")]
    public Task<SessionReport> SessionReport(Guid id, [FromQuery] ReportFilter filter, CancellationToken ct) => reports.SessionReport(id, filter, ct);

    private async Task<TrafficReport> TrafficResult(ReportFilter filter, bool includeSeries, CancellationToken ct)
    {
        var report = await reports.Zones(filter, includeSeries, ct);
        return new(report.Period, ReportSummaries.Traffic(report), report.Zones.Select(x =>
            new TrafficZone(x.ZoneId, x.ZoneName, x.Traffic, x.DataQuality, x.Series)).ToArray());
    }
}

public sealed record OccupancyZone(Guid ZoneId, string ZoneName, double? AverageOccupancy, int? PeakOccupancy,
    DateTime? PeakAtUtc, long? PeakAtSourceTimeMs, ZonePeriod? PeakHour, int? Capacity, double? PeakUtilizationPercent,
    double CoveragePercent, double ObservationDurationSeconds, DataQuality DataQuality, IReadOnlyList<ZonePeriod> Series);
public sealed record OccupancyReport(ReportPeriod Period, IReadOnlyList<OccupancyZone> Zones);
public sealed record WrongWayReport(ReportPeriod Period, decimal WrongWayIn, decimal WrongWayOut, decimal WrongWayEvents,
    RankingRow? HighestWrongWayZone, IReadOnlyList<TrafficZone> Zones);
