using System.Text.Json;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Infrastructure.Analytics;

internal sealed class IngestionRollupKeys(bool minuteEnabled)
{
    private readonly HashSet<BucketKey> zoneHours = [];
    private readonly HashSet<BucketKey> zoneMinutes = [];
    private readonly HashSet<BucketKey> cameraMinutes = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Zone(Guid zoneId, string source, DateTime from, DateTime to)
    {
        if (source != "live") return;
        for (var bucket = AggregateQueue.FloorHour(from); bucket <= to; bucket = bucket.AddHours(1))
            zoneHours.Add(new(zoneId, bucket));
        if (minuteEnabled)
            for (var bucket = MinuteQueue.Floor(from); bucket <= to; bucket = bucket.AddMinutes(1))
                zoneMinutes.Add(new(zoneId, bucket));
    }

    public void Camera(Guid cameraId, string source, DateTime from, DateTime to)
    {
        if (!minuteEnabled || source != "live") return;
        for (var bucket = MinuteQueue.Floor(from); bucket <= to; bucket = bucket.AddMinutes(1))
            cameraMinutes.Add(new(cameraId, bucket));
    }

    // The caller holds the existing exclusive writer lock and commits the facts and repair keys together.
    public Task Save(AnalyticsDb db, DateTime now, CancellationToken ct)
    {
        if (zoneHours.Count + zoneMinutes.Count + cameraMinutes.Count == 0)
            return Task.CompletedTask;
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @dirtyAt datetime2 = {now};

            INSERT INTO DirtyZoneHours (CameraZoneId, HourStartUtc, SourceType, DirtyAtUtc)
            SELECT k.Id, k.Bucket, N'live', @dirtyAt
            FROM OPENJSON({JsonSerializer.Serialize(zoneHours, Json)}) WITH (Id uniqueidentifier '$.id', Bucket datetime2 '$.bucket') k
            WHERE NOT EXISTS (SELECT 1 FROM DirtyZoneHours d WHERE d.CameraZoneId = k.Id AND d.HourStartUtc = k.Bucket AND d.SourceType = N'live');

            INSERT INTO DirtyZoneMinutes (CameraZoneId, MinuteStartUtc, DirtyAtUtc)
            SELECT k.Id, k.Bucket, @dirtyAt
            FROM OPENJSON({JsonSerializer.Serialize(zoneMinutes, Json)}) WITH (Id uniqueidentifier '$.id', Bucket datetime2 '$.bucket') k
            WHERE NOT EXISTS (SELECT 1 FROM DirtyZoneMinutes d WHERE d.CameraZoneId = k.Id AND d.MinuteStartUtc = k.Bucket);

            INSERT INTO DirtyCameraMinutes (CameraId, MinuteStartUtc, DirtyAtUtc)
            SELECT k.Id, k.Bucket, @dirtyAt
            FROM OPENJSON({JsonSerializer.Serialize(cameraMinutes, Json)}) WITH (Id uniqueidentifier '$.id', Bucket datetime2 '$.bucket') k
            WHERE NOT EXISTS (SELECT 1 FROM DirtyCameraMinutes d WHERE d.CameraId = k.Id AND d.MinuteStartUtc = k.Bucket);
            """, ct);
    }

    private sealed record BucketKey(Guid Id, DateTime Bucket);
}
