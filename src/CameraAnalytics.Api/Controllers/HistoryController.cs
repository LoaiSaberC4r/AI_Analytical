using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace CameraAnalytics.Api.Controllers;

[ApiController]
[Route("api/v1/analytics")]
public sealed class HistoryController(PeriodComparison comparisons, IForecasts forecasts, SiteAccess access) : ControllerBase
{
    [HttpGet("comparisons")]
    public Task<ComparisonReport> Compare([FromQuery] ReportFilter filter, CancellationToken ct,
        string metricKey = "arrivals", string alignment = "previousDuration")
    {
        access.DemandReport(User, filter.SiteId);
        return comparisons.Get(filter, metricKey, alignment, ct);
    }

    [HttpGet("forecast")]
    public Task<ForecastReport> Forecast(Guid zoneId, Guid? siteId, CancellationToken ct)
    {
        access.DemandReport(User, siteId);
        return forecasts.Occupancy(zoneId, siteId, ct);
    }
}
