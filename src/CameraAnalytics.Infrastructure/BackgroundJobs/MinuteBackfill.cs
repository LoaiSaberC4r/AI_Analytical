using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Infrastructure.BackgroundJobs;

internal static class MinuteBackfill
{
    private const int CursorId = 1;

    public static async Task Advance(AnalyticsDb db, int days, int gapSeconds, DateTime eligibleEnd, DateTime now, CancellationToken ct)
    {
        if (await db.MinuteBackfills.AsNoTracking().AnyAsync(x => x.Id == CursorId && x.NextMinuteUtc >= x.EndUtc, ct))
            return;
        await using var transaction = await WriteTransaction.Begin(db, ct);
        var cursor = await db.MinuteBackfills.FindAsync([CursorId], ct);
        if (cursor is null)
            cursor = db.MinuteBackfills.Add(new MinuteBackfillState { Id = CursorId, NextMinuteUtc = eligibleEnd.AddDays(-days), EndUtc = eligibleEnd }).Entity;
        var from = cursor.NextMinuteUtc;
        var to = from.AddHours(1) < cursor.EndUtc ? from.AddHours(1) : cursor.EndUtc;
        if (from >= to)
            return;
        var earliest = from.AddSeconds(-gapSeconds);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @from datetime2 = {from}, @to datetime2 = {to}, @earliest datetime2 = {earliest}, @now datetime2 = {now};
            ;WITH Minutes AS (
                SELECT @from AS StartUtc UNION ALL SELECT DATEADD(minute, 1, StartUtc) FROM Minutes WHERE DATEADD(minute, 1, StartUtc) < @to
            ), ZoneKeys AS (
                SELECT DISTINCT z.CameraZoneId, m.StartUtc FROM Minutes m
                JOIN ZoneSnapshots z ON z.SourceTimestampUtc < DATEADD(minute, 1, m.StartUtc)
                    AND (z.SourceTimestampUtc >= m.StartUtc OR z.ObservationEndUtc > m.StartUtc)
                JOIN AnalyticsSessions s ON s.Id = z.AnalyticsSessionId AND s.SourceType = 'live'
                WHERE z.IsOrdered = 1 AND z.SourceTimestampUtc >= @earliest AND z.SourceTimestampUtc < @to
            )
            INSERT INTO DirtyZoneMinutes (CameraZoneId, MinuteStartUtc, DirtyAtUtc)
            SELECT k.CameraZoneId, k.StartUtc, @now FROM ZoneKeys k WHERE NOT EXISTS (
                SELECT 1 FROM ZoneMinuteAggregates a WHERE a.CameraZoneId = k.CameraZoneId AND a.MinuteStartUtc = k.StartUtc)
                AND NOT EXISTS (SELECT 1 FROM DirtyZoneMinutes a WHERE a.CameraZoneId = k.CameraZoneId AND a.MinuteStartUtc = k.StartUtc);
            ;WITH Minutes AS (
                SELECT @from AS StartUtc UNION ALL SELECT DATEADD(minute, 1, StartUtc) FROM Minutes WHERE DATEADD(minute, 1, StartUtc) < @to
            ), CameraKeys AS (
                SELECT DISTINCT c.CameraId, m.StartUtc FROM Minutes m
                JOIN CameraSnapshots c ON c.SourceTimestampUtc < DATEADD(minute, 1, m.StartUtc)
                    AND (c.SourceTimestampUtc >= m.StartUtc OR c.ObservationEndUtc > m.StartUtc)
                JOIN AnalyticsSessions s ON s.Id = c.AnalyticsSessionId AND s.SourceType = 'live'
                WHERE c.IsOrdered = 1 AND c.SourceTimestampUtc >= @earliest AND c.SourceTimestampUtc < @to
            )
            INSERT INTO DirtyCameraMinutes (CameraId, MinuteStartUtc, DirtyAtUtc)
            SELECT k.CameraId, k.StartUtc, @now FROM CameraKeys k WHERE NOT EXISTS (
                SELECT 1 FROM CameraMinuteAggregates a WHERE a.CameraId = k.CameraId AND a.MinuteStartUtc = k.StartUtc)
                AND NOT EXISTS (SELECT 1 FROM DirtyCameraMinutes a WHERE a.CameraId = k.CameraId AND a.MinuteStartUtc = k.StartUtc);
            """, ct);
        cursor.NextMinuteUtc = to;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
