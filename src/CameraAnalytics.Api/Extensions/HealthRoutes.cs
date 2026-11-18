using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Mqtt;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Api.Extensions;

public sealed record LiveHealth(string Status);
public sealed record ReadyHealth(string Status, string Database, string Mqtt);

public static class HealthRoutes
{
    public static void MapAnalyticsHealth(this WebApplication app)
    {
        app.MapGet("/api/v1/health/live", () => new LiveHealth("Healthy")).WithTags("Health").AllowAnonymous();
        app.MapGet("/api/v1/health/ready", Ready).WithTags("Health").Produces<ReadyHealth>(503).AllowAnonymous();
    }

    private static async Task<Results<Ok<ReadyHealth>, JsonHttpResult<ReadyHealth>>> Ready(AnalyticsDb db,
        MqttConnectionStatus broker, IOptions<MqttOptions> mqtt, ILoggerFactory log, CancellationToken ct)
    {
        var database = "Unavailable";
        try
        {
            if (await db.Database.CanConnectAsync(ct))
                database = !db.Database.GetMigrations().Any() || (await db.Database.GetPendingMigrationsAsync(ct)).Any()
                    ? "MigrationRequired" : "Ready";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.CreateLogger("Readiness").LogWarning(e, "Database readiness check failed");
        }

        var brokerStatus = !mqtt.Value.Enabled ? "Disabled" : broker.Subscribed ? "Subscribed" : "Disconnected";
        return database != "Ready"
            ? TypedResults.Json(new ReadyHealth("Unhealthy", database, brokerStatus), statusCode: 503)
            : TypedResults.Ok(new ReadyHealth(brokerStatus == "Disconnected" ? "Degraded" : "Healthy", database, brokerStatus));
    }
}
