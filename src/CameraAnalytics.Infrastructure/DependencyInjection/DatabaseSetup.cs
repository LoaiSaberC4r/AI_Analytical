using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Infrastructure.DependencyInjection;

public static class DatabaseSetup
{
    public static IServiceCollection AddAnalyticsDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AnalyticsDb>((provider, db) =>
        {
            var settings = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            db.UseSqlServer(configuration.GetConnectionString("DefaultConnection"),
                sql => sql.CommandTimeout(settings.CommandTimeoutSeconds));
            db.EnableSensitiveDataLogging(settings.EnableSensitiveDataLogging);
        });
        services.AddScoped<ICatalogQueries, CatalogQueries>();
        services.AddScoped<ICatalogCommands, CatalogCommands>();
        return services;
    }
}
