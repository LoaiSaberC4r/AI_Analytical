using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CameraAnalytics.IntegrationTests;

public sealed class SecurityTests
{
    [SqlFact]
    public async Task Legacy_api_requires_admin_and_hub_enforces_site_membership()
    {
        const string issuer = "https://issuer.example.test";
        const string audience = "test-analytics";
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        await using var host = new IntegrationHost();
        await using var secured = host.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Enabled"] = "true", ["Security:Authority"] = issuer, ["Security:Audience"] = audience
            }));
            builder.ConfigureServices(services => services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                jwt =>
                {
                    jwt.Configuration = new OpenIdConnectConfiguration { Issuer = issuer };
                    jwt.Configuration.SigningKeys.Add(key);
                }));
        });
        using var client = secured.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/sites")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(SiteAccess.Administrator));
        var (site, camera, _) = await host.Register(client);
        var other = await IntegrationHost.Post<SiteData>(client, "sites", new { name = "Other", code = "OTHER", timeZoneId = "UTC" });
        var otherCamera = await IntegrationHost.Post<CameraData>(client, "cameras",
            new { name = "Other camera", siteId = other.Id, externalCameraId = "other" });
        var reader = Token(SiteAccess.Reader, site.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reader);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/v1/sites")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"api/v1/analytics/traffic/flow-rate?siteId={site.Id}&interval=hour")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"api/v1/analytics/traffic/flow-rate?siteId={other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"api/v1/sites/{site.Id}/floors", new SaveFloor("Lobby"))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(SiteAccess.Operator, site.Id));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"api/v1/sites/{site.Id}/floors", new SaveFloor("Lobby"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"api/v1/sites/{other.Id}/floors", new SaveFloor("Lobby"))).StatusCode);

        await using var connection = new HubConnectionBuilder().WithUrl(new Uri(client.BaseAddress!, "/hubs/analytics"), options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.AccessTokenProvider = () => Task.FromResult<string?>(reader);
            options.HttpMessageHandlerFactory = _ => secured.Server.CreateHandler();
        }).Build();
        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToSite", site.Id);
        await connection.InvokeAsync("SubscribeToCamera", camera.Id);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SubscribeToSite", Guid.NewGuid()));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SubscribeToCamera", otherCamera.Id));

        string Token(string role, Guid? authorizedSite = null)
        {
            var claims = new List<Claim> { new("sub", "test-user"), new("role", role) };
            if (authorizedSite.HasValue)
                claims.Add(new("site_id", authorizedSite.Value.ToString()));
            var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new(key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
