using System.Security.Claims;
using CameraAnalytics.Application.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Api.Extensions;

public sealed class SecuritySettings
{
    public bool? Enabled { get; init; }
    public string? Authority { get; init; }
    public string? Audience { get; init; }
}

public sealed class SiteAccess(IOptions<SecuritySettings> options, IHostEnvironment environment)
{
    public const string Administration = "AnalyticsAdministration";
    public const string Administrator = "AnalyticsAdministrator";
    public const string Operator = "AnalyticsOperator";
    public const string Reader = "AnalyticsReader";
    public const string SiteClaim = "site_id";
    public bool Enabled => options.Value.Enabled ?? !AllowsDemo(environment);

    internal static bool AllowsDemo(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public bool Allows(ClaimsPrincipal? user, Guid siteId, bool write = false) =>
        !Enabled || (user?.Identity?.IsAuthenticated == true && (user.IsInRole(Administrator)
            || ((user.IsInRole(Operator) || (!write && user.IsInRole(Reader)))
                && user.FindAll(SiteClaim).Any(claim => Guid.TryParse(claim.Value, out var site) && site == siteId))));

    public void Demand(ClaimsPrincipal? user, Guid siteId, bool write = false)
    {
        if (!Allows(user, siteId, write))
            throw new ApiError(403, "site_access_denied", "Access to this site is denied.");
    }

    public void DemandReport(ClaimsPrincipal user, Guid? siteId)
    {
        if (!Enabled || user.IsInRole(Administrator))
            return;
        if (!siteId.HasValue)
            throw ApiError.Invalid("A siteId is required for site-scoped reports.");
        Demand(user, siteId.Value);
    }
}

public static class Security
{
    public static void AddAnalyticsSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var local = SiteAccess.AllowsDemo(builder.Environment);
        services.AddOptions<SecuritySettings>().BindConfiguration("Security").Validate(settings =>
        {
            if (!(settings.Enabled ?? !local))
                return local;
            return Uri.TryCreate(settings.Authority, UriKind.Absolute, out var issuer) && issuer.Scheme == "https"
                && issuer.UserInfo.Length == 0 && string.IsNullOrEmpty(issuer.Query) && string.IsNullOrEmpty(issuer.Fragment)
                && !string.IsNullOrWhiteSpace(settings.Audience);
        }, "Configure Security:Authority (HTTPS) and Security:Audience for JWT authentication. Demo mode requires Development or Testing.")
            .ValidateOnStart();
        services.AddSingleton<SiteAccess>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SecuritySettings>, IOptions<SignalRSettings>>((jwt, settings, signalR) =>
            {
                jwt.Authority = settings.Value.Authority;
                jwt.Audience = settings.Value.Audience;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.RoleClaimType = "role";
                jwt.IncludeErrorDetails = false;
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.SignalR.HubMetadata>() is not null
                            && context.Request.Path.StartsWithSegments(signalR.Value.HubPath)
                            && context.Request.Query.TryGetValue("access_token", out var value) && value.Count == 1)
                            context.Token = value[0];
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorizationBuilder();
        services.AddOptions<AuthorizationOptions>().Configure<SiteAccess>((options, access) =>
        {
            if (!access.Enabled)
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
                options.AddPolicy(SiteAccess.Administration, options.DefaultPolicy);
                return;
            }
            options.FallbackPolicy = options.DefaultPolicy;
            options.AddPolicy(SiteAccess.Administration, policy => policy.RequireAuthenticatedUser().RequireRole(SiteAccess.Administrator));
        });
    }
}
