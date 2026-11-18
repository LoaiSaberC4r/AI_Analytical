using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace CameraAnalytics.Api.Controllers;

[ApiController]
[Route("api/v1/analytics")]
[Tags("Smart Analytics")]
public sealed class SmartAnalyticsController(ISmartQueries queries, VisualizationQueries visuals, InsightRulesEngine insights, SiteAccess access) : ControllerBase
{
    [HttpGet("traffic/flow-rate")]
    public Task<FlowReport> Flow([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        access.DemandReport(User, filter.SiteId);
        return queries.Flow(filter, ct);
    }

    [HttpGet("congestion")]
    public Task<CongestionReport> Congestion([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        access.DemandReport(User, filter.SiteId);
        return queries.Congestion(filter, ct);
    }

    [HttpGet("congestion/history")]
    public Task<CongestionHistory> CongestionHistory([FromQuery] ReportFilter filter, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        access.DemandReport(User, filter.SiteId);
        return queries.CongestionHistory(filter, new(page, pageSize), ct);
    }

    [HttpGet("dwell/distribution")]
    [HttpGet("dwell/long-stay")]
    public Task<DwellDistributionReport> Dwell([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        access.DemandReport(User, filter.SiteId);
        return queries.Dwell(filter, ct);
    }

    [HttpGet("quality")]
    public Task<QualityReport> Quality([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        access.DemandReport(User, filter.SiteId);
        return queries.Quality(filter, ct);
    }

    [HttpGet("visualizations/{metricKey}")]
    public Task<VisualizationResponse> Visualization(string metricKey, [FromQuery] string? visualizationType,
        [FromQuery] ReportFilter filter, CancellationToken ct)
    {
        access.DemandReport(User, filter.SiteId);
        return visuals.Get(metricKey, visualizationType, filter, ct);
    }

    [HttpGet("insights")]
    public Task<InsightsReport> Insights([FromQuery] ReportFilter filter, CancellationToken ct)
    {
        access.DemandReport(User, filter.SiteId);
        return insights.Get(filter, ct);
    }
}
