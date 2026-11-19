using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Api.Hubs;
using CameraAnalytics.Api.Middleware;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Infrastructure.Analytics;
using CameraAnalytics.Infrastructure.BackgroundJobs;
using CameraAnalytics.Infrastructure.DependencyInjection;
using CameraAnalytics.Infrastructure.Mqtt;
using CameraAnalytics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddValidatedSettings();
builder.AddAnalyticsSecurity();
builder.Services.AddAnalyticsDatabase(builder.Configuration);
builder.Services.AddSingleton<IMqttPayloadReader, MqttPayloadReader>();
builder.Services.AddScoped<IAnalyticsIngestor, AnalyticsIngestor>();
builder.Services.AddScoped<ReportQueries>();
builder.Services.AddScoped<IReportQueries>(services => services.GetRequiredService<ReportQueries>());
builder.Services.AddScoped<ISmartQueries, SmartQueries>();
builder.Services.AddScoped<IFloorPlans, FloorPlans>();
builder.Services.AddScoped<IForecasts, ForecastQueries>();
builder.Services.AddScoped(services => new PeriodComparison(services.GetRequiredService<ISmartQueries>(),
    services.SettingsValue<AnalyticsOptions>().MinimumCoveragePercentForPeakRanking));
builder.Services.AddScoped<AlertStore>();
builder.Services.AddScoped<IAlerts>(services => services.GetRequiredService<AlertStore>());
builder.Services.AddScoped<AlertEvaluator>();
builder.Services.AddSingleton<IAlertNotifications, HubAlertNotifications>();
builder.Services.AddHostedService<AlertWorker>();
builder.Services.AddScoped(services => new VisualizationQueries(services.GetRequiredService<ISmartQueries>(),
    services.SettingsValue<VisualizationOptions>()));
builder.Services.AddScoped(services => new InsightRulesEngine(services.GetRequiredService<ISmartQueries>(),
    services.GetRequiredService<TimeProvider>(), services.SettingsValue<AnalyticsOptions>().MinimumCoveragePercentForPeakRanking));
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
builder.Services.AddHostedService<MinuteAggregationWorker>();
builder.Services.AddHostedService<FreshnessWorker>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code",
        context.ProblemDetails.Status == 400 ? "invalid_request" : "http_error");
});
builder.Services.AddExceptionHandler<ApiErrors>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    var invalid = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        var result = invalid(context);
        if (result is ObjectResult { Value: ProblemDetails problem })
        {
            problem.Extensions.TryAdd("code", "invalid_request");
            problem.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
        }
        return result;
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.UseOneOfForPolymorphism());
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
_ = app.Services.SettingsValue<SecuritySettings>();
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
app.UseAuthentication();
app.UseAuthorization();
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
    {
        options.CloseOnAuthenticationExpiration = true;
        options.Transports = realtime.Transport == "All"
            ? HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling
            : Enum.Parse<HttpTransportType>(realtime.Transport, true);
    }).RequireAuthorization();
if (app.Services.SettingsValue<HealthSettings>().Enabled)
    app.MapAnalyticsHealth();
await app.ApplyConfiguredMigrations();
await app.RunAsync();

public partial class Program;
