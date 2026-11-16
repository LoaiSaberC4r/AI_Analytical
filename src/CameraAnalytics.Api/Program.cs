using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Api.Hubs;
using CameraAnalytics.Api.Middleware;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.BackgroundJobs;
using CameraAnalytics.Infrastructure.DependencyInjection;
using CameraAnalytics.Infrastructure.Mqtt;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddValidatedSettings();
builder.Services.AddAnalyticsDatabase(builder.Configuration);
builder.Services.AddSingleton<IMqttPayloadReader, MqttPayloadReader>();
builder.Services.AddScoped<IAnalyticsIngestor, AnalyticsIngestor>();
builder.Services.AddScoped<IReportQueries, ReportQueries>();
builder.Services.AddScoped<IMonitoringQueries, MonitoringQueries>();
builder.Services.AddSignalR();
builder.Services.AddOptions<HubOptions>().Configure<IOptions<SignalRSettings>>((options, config) =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(config.Value.KeepAliveIntervalSeconds);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(config.Value.ClientTimeoutSeconds);
});
builder.Services.AddSingleton<IAnalyticsNotifications>(services => services.SettingsValue<SignalRSettings>().Enabled
    ? ActivatorUtilities.CreateInstance<HubNotifications>(services) : new DisabledNotifications());
builder.Services.AddSingleton<MqttConnectionStatus>();
builder.Services.AddHostedService<MqttSubscriberBackgroundService>();
builder.Services.AddHostedService<AggregationWorker>();
builder.Services.AddHostedService<FreshnessWorker>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code",
        context.ProblemDetails.Status == 400 ? "invalid_request" : "http_error");
});
builder.Services.AddExceptionHandler<ApiErrors>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((options, config) =>
{
    var settings = config.Value;
    if (settings.Enabled)
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(settings.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
            if (settings.AllowCredentials)
                policy.AllowCredentials();
        });
});

var app = builder.Build();
_ = app.Services.SettingsValue<DatabaseOptions>();
var cors = app.Services.SettingsValue<CorsSettings>();
_ = app.Services.SettingsValue<MqttOptions>();
_ = app.Services.SettingsValue<AnalyticsOptions>();
_ = app.Services.SettingsValue<AggregationOptions>();
_ = app.Services.SettingsValue<RetentionOptions>();
var realtime = app.Services.SettingsValue<SignalRSettings>();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (cors.Enabled)
    app.UseCors();
var openApi = app.Services.SettingsValue<OpenApiSettings>();
if (openApi.Enabled)
{
    app.UseSwagger();
    if (openApi.EnableSwaggerUi)
        app.UseSwaggerUI();
}
app.MapControllers();
if (realtime.Enabled)
    app.MapHub<AnalyticsHub>(realtime.HubPath, options =>
        options.Transports = realtime.Transport == "All"
            ? HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling
            : Enum.Parse<HttpTransportType>(realtime.Transport, true));
if (app.Services.SettingsValue<HealthSettings>().Enabled)
    app.MapAnalyticsHealth();
await app.ApplyConfiguredMigrations();
await app.RunAsync();

public partial class Program;
