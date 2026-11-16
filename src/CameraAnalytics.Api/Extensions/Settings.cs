using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.ValueObjects;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Options;

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
            && !string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")),
            "Set ConnectionStrings:DefaultConnection and a positive database command timeout.");
        Add<CorsSettings>("Cors", x => !x.Enabled || (x.AllowedOrigins.Length > 0 && x.AllowedOrigins.All(ValidOrigin)),
            "Configure explicit HTTP(S) CORS origins without paths, wildcards or credentials.");
        Add<MqttOptions>("Mqtt", x => x.Port is >= 1 and <= 65535 && x.Qos is >= 0 and <= 2
            && x.ConnectTimeoutSeconds > 0 && x.ReconnectDelaySeconds > 0 && x.KeepAliveSeconds is > 0 and <= 65535
            && x.MaxPayloadBytes > 0 && x.ProtocolVersion is "3.1.1" or "5.0"
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
        Add<RetentionOptions>("Retention", x => x.RawSnapshotsDays > 0 && x.ProcessedMessagesDays >= x.RawSnapshotsDays
            && x.HourlyAggregatesDays >= x.RawSnapshotsDays
            && (!x.Enabled || config.GetValue("Aggregation:Enabled", true)), "Retention requires aggregation and ordered retention windows.");
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

    public static T SettingsValue<T>(this IServiceProvider services) where T : class =>
        services.GetRequiredService<IOptions<T>>().Value;
}
