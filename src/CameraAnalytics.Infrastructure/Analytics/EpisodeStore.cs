using System.Text.Json;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Infrastructure.Analytics;

internal static class EpisodeStore
{
    public static void Observe(AnalyticsDb db, ZoneCurrentState state, ZoneSnapshot? previous, ZoneSnapshot next,
        bool video, bool contiguous, CongestionOptions settings)
    {
        var progress = Read(state);
        if (previous is null)
        {
            progress?.Close(state.CameraZoneId, "Uncertain", "observation_unavailable", Add);
            state.CongestionProgressJson = null;
            return;
        }
        var updated = CongestionProgress.Advance(progress, previous, next, video, contiguous, settings, Add);
        state.CongestionProgressJson = updated is null ? null : JsonSerializer.Serialize(updated);

        void Add(CongestionEpisode episode) => db.CongestionEpisodes.Add(episode);
    }

    public static async Task Close(AnalyticsDb db, Guid sessionId, string status, string reason, CancellationToken ct)
    {
        var rows = await db.ZoneCurrentStates.Where(x => x.AnalyticsSessionId == sessionId && x.CongestionProgressJson != null).ToListAsync(ct);
        foreach (var row in rows.Concat(db.ZoneCurrentStates.Local.Where(x => x.AnalyticsSessionId == sessionId)).Distinct())
        {
            Read(row)?.Close(row.CameraZoneId, status, reason, episode => db.CongestionEpisodes.Add(episode));
            row.CongestionProgressJson = null;
        }
    }

    private static CongestionProgress? Read(ZoneCurrentState state) => state.CongestionProgressJson is null ? null
        : JsonSerializer.Deserialize<CongestionProgress>(state.CongestionProgressJson);
}
