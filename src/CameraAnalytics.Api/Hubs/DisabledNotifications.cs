using CameraAnalytics.Application.Abstractions;

namespace CameraAnalytics.Api.Hubs;

internal sealed class DisabledNotifications : IAnalyticsNotifications
{
    public Task AnalyticsCommitted(CommittedAnalytics message, CancellationToken ct) => Task.CompletedTask;
    public Task FreshnessChanged(FreshnessChange message, CancellationToken ct) => Task.CompletedTask;
}
