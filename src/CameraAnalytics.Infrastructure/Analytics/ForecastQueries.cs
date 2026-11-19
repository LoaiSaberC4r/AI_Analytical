using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.Analytics;

public sealed class ForecastQueries(AnalyticsDb db, TimeProvider clock, IOptions<ForecastOptions> options) : IForecasts
{
    public async Task<ForecastReport> Occupancy(Guid zoneId, Guid? siteId, CancellationToken ct)
    {
        await using var transaction = await WriteTransaction.Read(db, ct);
        var zone = await db.CameraZones.AsNoTracking().Where(x => x.Id == zoneId)
            .Select(x => new { x.Name, x.CameraId, x.Camera.SiteId, x.Camera.Site.TimeZoneId })
            .SingleOrDefaultAsync(ct) ?? throw ApiError.NotFound("Zone");
        if (siteId.HasValue && siteId != zone.SiteId)
            throw ApiError.Invalid("The zone does not belong to the requested site.");
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = AggregateQueue.FloorHour(now);
        var since = cutoff.AddDays(-options.Value.PreferredLookbackDays);
        var history = options.Value.Enabled ? await db.ZoneHourlyAggregates.AsNoTracking().Where(x => x.CameraZoneId == zoneId
            && x.SourceType == "live" && x.HourStartUtc >= since && x.HourStartUtc < cutoff
            && !db.DirtyZoneHours.Any(d => d.CameraZoneId == zoneId && d.SourceType == "live" && d.HourStartUtc == x.HourStartUtc))
            .Select(x => new ForecastHour(x.HourStartUtc, x.AverageOccupancy, x.CoveragePercent,
                x.GapCount == 0 && x.DataQuality != "InsufficientData")).ToArrayAsync(ct) : [];
        return HourlyForecast.Calculate(new(zone.SiteId, zone.CameraId, zoneId), zone.Name, Input.TimeZone(zone.TimeZoneId), now, history, options.Value);
    }
}
