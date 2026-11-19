using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.ValueObjects;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;

namespace CameraAnalytics.Api.Extensions;

public sealed class CorsSettings
{
    public bool Enabled { get; init; }
    public string[] AllowedOrigins { get; init; } = [];
    public bool AllowCredentials { get; init; }
}

public sealed class SignalRSettings
{
    public bool Enabled { get; init; }
    public string HubPath { get; init; } = "/hubs/analytics";
    public int KeepAliveIntervalSeconds { get; init; } = 15;
    public int ClientTimeoutSeconds { get; init; } = 30;
    public string Transport { get; init; } = "All";
}

public sealed class OpenApiSettings
{
    public bool Enabled { get; init; }
    public bool EnableSwaggerUi { get; init; }
}

public sealed class HealthSettings
{
    public bool Enabled { get; init; }
}

public static class Settings
{
    public static void AddValidatedSettings(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;
        Add<DatabaseOptions>("Database", x => x.CommandTimeoutSeconds is > 0 and <= 3600
            && ValidConnection(config.GetConnectionString("DefaultConnection"))
            && (!x.EnableSensitiveDataLogging || builder.Environment.IsDevelopment()),
            "Set a valid SQL Server connection in ConnectionStrings__DefaultConnection or Development User Secrets. Database command timeout must be positive and sensitive EF logging requires Development.");
        Add<CorsSettings>("Cors", x => !x.Enabled || (x.AllowedOrigins.Length > 0 && x.AllowedOrigins.All(ValidOrigin)),
            "Configure explicit HTTP(S) CORS origins without paths, wildcards or credentials.");
        Add<MqttOptions>("Mqtt", x => x.Port is >= 1 and <= 65535 && x.Qos is >= 0 and <= 2
            && x.ConnectTimeoutSeconds > 0 && x.ReconnectDelaySeconds > 0 && x.KeepAliveSeconds is > 0 and <= 65535
            && x.MaxPayloadBytes is > 0 and <= 16_777_216 && x.ProtocolVersion is "3.1.1" or "5.0"
            && x.MaxSourceNameLength is > 0 and <= 512 && x.MaxZonesPerMessage is > 0 and <= 2048
            && x.MaxActiveVisitorsPerZone is > 0 and <= 65_536
            && (!x.Enabled || (!string.IsNullOrWhiteSpace(x.Host) && !string.IsNullOrWhiteSpace(x.ClientId)
                && Identifiers.IsTopicFilter(x.TopicFilter) && (!x.PersistentSession || (x.Qos > 0 && x.SessionExpirySeconds > 0))
                && (x.Password is null || !string.IsNullOrWhiteSpace(x.Username)))),
            "Check MQTT host, port, topic, client ID, QoS, timeouts and credentials.");
        Add<SignalRSettings>("SignalR", x => x.KeepAliveIntervalSeconds > 0 && x.ClientTimeoutSeconds > x.KeepAliveIntervalSeconds
            && (!x.Enabled || (x.HubPath.StartsWith('/') && x.HubPath.Length > 1
                && !x.HubPath.Any(c => char.IsWhiteSpace(c) || c is '?' or '#' or '{' or '}')
                && (x.Transport == "All" || Enum.TryParse<HttpTransportType>(x.Transport, true, out var t)
                    && t is HttpTransportType.WebSockets or HttpTransportType.ServerSentEvents or HttpTransportType.LongPolling))),
            "Check the SignalR hub path, transport and keep-alive settings.");
        Add<AnalyticsOptions>("Analytics", x => x.ExpectedMessageIntervalSeconds > 0 && x.StaleAfterSeconds > 0
            && x.NoRecentDataAfterSeconds > x.StaleAfterSeconds && x.MaximumSnapshotGapSeconds >= x.ExpectedMessageIntervalSeconds
            && x.TrackMissingGraceSeconds > 0 && x.MaximumReportRangeDays is > 0 and <= 3660 && x.ClockSkewToleranceSeconds >= 0
            && x.MaximumMinuteReportRangeDays is > 0 and <= 3660
            && double.IsFinite(x.DefaultCriticalThresholdPercent)
            && x.DefaultWarningThresholdPercent >= 0 && x.DefaultCriticalThresholdPercent > x.DefaultWarningThresholdPercent
            && x.MinimumCoveragePercentForPeakRanking is >= 0 and <= 100
            && x.DefaultAggregationInterval is "minute" or "hour" or "day", "Check analytics thresholds, durations and interval.");
        Add<AggregationOptions>("Aggregation", x => x.IntervalSeconds > 0 && x.BatchSize is > 0 and <= 1000,
            "Provide a positive aggregation schedule and batch size.");
        Add<VisualizationOptions>("Visualization", x => x.MaxSeries is > 0 and <= 100 && x.MaxPointsPerSeries is > 0 and <= 10_000
            && x.DefaultRefreshSeconds is >= 2 and <= 300 && x.MinimumRateCoveragePercent is > 0 and <= 100,
            "Check visualization limits, refresh interval and rate coverage threshold.");
        Add<CongestionOptions>("Congestion", x => x.MinObservedDurationSeconds is > 0 and <= 3600
            && x.RecoveryObservedSeconds is > 0 and <= 3600 && x.MaxObservationGapSeconds is > 0 and <= 300,
            "Check congestion duration, recovery and maximum gap.");
        Add<DwellAnalyticsOptions>("DwellAnalytics", x => x.DefaultLongStaySeconds is > 0 and <= 86_400
            && x.HistogramBounds is { Length: >= 1 and <= 32 }
            && x.HistogramBounds.All(v => v is > 0 and <= 86_400)
            && x.HistogramBounds.Zip(x.HistogramBounds.Skip(1)).All(pair => pair.First < pair.Second),
            "Dwell histogram bounds must be positive, finite and strictly increasing.");
        Add<FloorPlanOptions>("FloorPlans", x => x.MaxPolygonVertices is >= 3 and <= 500 && x.MaxLayoutsPerFloor is > 0 and <= 300,
            "Check floor layout and polygon size limits.");
        Add<AlertOptions>("Alerts", x => x.EvaluateEverySeconds is >= 1 and <= 300 && x.MinimumCoveragePercent is > 0 and <= 100
            && x.DefaultCooldownSeconds is >= 0 and <= 86_400 && x.MaxRules is > 0 and <= 10_000, "Check alerts evaluation and evidence limits.");
        Add<ForecastOptions>("Forecast", x => x.MinHistoryDays >= 28 && x.PreferredLookbackDays is >= 28 and <= 365
            && x.MinHistoryDays <= x.PreferredLookbackDays && x.MinComparableSlots >= 4 && x.MinComparableSlots <= x.PreferredLookbackDays / 7
            && x.MinimumCoveragePercent is >= 80 and <= 100 && x.HorizonHours is >= 1 and <= 168,
            "Forecast requires at least 28 usable days, four matching slots and 80 percent coverage.");
        Add<RetentionOptions>("Retention", x => x.RawSnapshotsDays > 0 && x.ProcessedMessagesDays >= x.RawSnapshotsDays
            && x.HourlyAggregatesDays >= x.RawSnapshotsDays
            && x.MinuteAggregatesDays is > 0 and <= 3660 && x.AlertHistoryDays is > 0 and <= 3660
            && (!x.Enabled || config.GetValue("Aggregation:Enabled", true)), "Retention requires aggregation and ordered retention windows.");
        Add<MinuteAggregationOptions>("MinuteAggregation", x => x.IntervalSeconds is > 0 and <= 3600 && x.BatchSize is > 0 and <= 1000
            && x.FinalizationDelaySeconds is >= 0 and <= 3600 && x.BackfillDays is >= 0 and <= 90
            && (!config.GetValue("Retention:Enabled", false) || x.BackfillDays <= config.GetValue("Retention:RawSnapshotsDays", 7)),
            "Check the minute aggregation schedule, batch size and raw-history backfill horizon.");
        services.AddOptions<OpenApiSettings>().BindConfiguration("OpenApi").ValidateOnStart();
        services.AddOptions<HealthSettings>().BindConfiguration("HealthChecks").ValidateOnStart();
        return;

        void Add<T>(string section, Func<T, bool> validate, string message) where T : class =>
            services.AddOptions<T>().BindConfiguration(section).Validate(validate, message).ValidateOnStart();
    }

    private static bool ValidOrigin(string value) =>
        !value.Contains('*') && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
        && value.Equals(uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private static bool ValidConnection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return !string.IsNullOrWhiteSpace(new SqlConnectionStringBuilder(value).DataSource); }
        catch (ArgumentException) { return false; }
    }

    public static T SettingsValue<T>(this IServiceProvider services) where T : class =>
        services.GetRequiredService<IOptions<T>>().Value;
}
