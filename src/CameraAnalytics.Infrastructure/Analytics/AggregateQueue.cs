using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Persistence;

namespace CameraAnalytics.Infrastructure.Analytics;

internal static class AggregateQueue
{
    public static DateTime FloorHour(DateTime time) => new(time.Ticks - time.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Utc);

    public static async Task Mark(AnalyticsDb db, Guid zoneId, string sourceType, DateTime from, DateTime to, DateTime now,
        CancellationToken ct)
    {
        if (sourceType != "live")
            return;

        var end = FloorHour(to);
        for (var hour = FloorHour(from); hour <= end; hour = hour.AddHours(1))
        {
            var job = await db.DirtyZoneHours.FindAsync([zoneId, sourceType, hour], ct);
            if (job is null)
                db.DirtyZoneHours.Add(new() { CameraZoneId = zoneId, SourceType = sourceType, HourStartUtc = hour, DirtyAtUtc = now });
        }
    }
}
