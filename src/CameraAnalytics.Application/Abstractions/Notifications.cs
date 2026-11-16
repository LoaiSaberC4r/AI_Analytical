using CameraAnalytics.Domain.Entities;
using CameraAnalytics.Domain.Enums;

namespace CameraAnalytics.Application.Abstractions;

public sealed record CommittedAnalytics(Guid SiteId, CameraCurrentState Camera, IReadOnlyList<ZoneCurrentState> Zones, bool StatusChanged);
public sealed record FreshnessChange(Guid SiteId, Guid CameraId, Guid SessionId, long StateVersion,
    string AnalyticsStatus, DataFreshness Freshness, DateTime ReceivedAtUtc);

public interface IAnalyticsNotifications
{
    Task AnalyticsCommitted(CommittedAnalytics message, CancellationToken ct);
    Task FreshnessChanged(FreshnessChange message, CancellationToken ct);
}
