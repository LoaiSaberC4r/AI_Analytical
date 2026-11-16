using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CameraAnalytics.Api.Extensions;

public static class Schema
{
    public static async Task ApplyConfiguredMigrations(this WebApplication app)
    {
        if (!app.Services.SettingsValue<DatabaseOptions>().ApplyMigrationsOnStartup)
            return;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDb>();
        if (!db.Database.GetMigrations().Any())
            throw new InvalidOperationException("No EF Core migrations were found. Deploy the checked-in migrations.");
        await db.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
    }
}
