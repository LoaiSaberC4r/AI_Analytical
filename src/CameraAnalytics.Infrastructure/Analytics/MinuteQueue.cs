using CameraAnalytics.Infrastructure.Persistence;

namespace CameraAnalytics.Infrastructure.Analytics;

internal static class MinuteQueue
{
    public static DateTime Floor(DateTime time) => new(time.Ticks - time.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    public static async Task MarkZone(AnalyticsDb db, Guid zoneId, DateTime from, DateTime to, DateTime now, CancellationToken ct)
    {
        for (var minute = Floor(from); minute <= to; minute = minute.AddMinutes(1))
            if (await db.DirtyZoneMinutes.FindAsync([zoneId, minute], ct) is null)
                db.DirtyZoneMinutes.Add(new() { CameraZoneId = zoneId, MinuteStartUtc = minute, DirtyAtUtc = now });
    }

}
