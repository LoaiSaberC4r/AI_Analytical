using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CameraAnalytics.Application.Abstractions;

namespace CameraAnalytics.IntegrationTests;

public sealed class FloorTests
{
    [SqlFact]
    public async Task Floor_geometry_is_versioned_and_heatmap_reports_fresh_zone_value()
    {
        await using var host = new IntegrationHost();
        using var client = host.CreateClient();
        var (site, _, zone) = await host.Register(client);
        var floor = await IntegrationHost.Post<FloorData>(client, $"sites/{site.Id}/floors", new { name = "Ground" });
        var path = $"api/v1/floors/{floor.Id}/zones/{zone.Id}/layout";
        PolygonVertex[] polygon = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new SaveLayout([polygon[0], polygon[1]]))).StatusCode);
        var saved = (await (await client.PutAsJsonAsync(path, new SaveLayout(polygon))).Content.ReadFromJsonAsync<ZoneLayoutData>())!;
        Assert.Equal(polygon, saved.Vertices);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, new SaveLayout(polygon))).StatusCode);
        var other = await IntegrationHost.Post<SiteData>(client, "sites", new { name = "Other", code = "other", timeZoneId = "UTC" });
        var otherFloor = await IntegrationHost.Post<FloorData>(client, $"sites/{other.Id}/floors", new { name = "Other Ground" });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"api/v1/floors/{otherFloor.Id}/zones/{zone.Id}/layout",
            new SaveLayout(polygon))).StatusCode);
        var query = $"api/v1/analytics/heatmap?floorId={floor.Id}&metricKey=freshOccupancy";
        var empty = await client.GetFromJsonAsync<JsonElement>(query);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("zones")[0].GetProperty("value").ValueKind);
        await host.Ingest(host.Payload("live", 1, 0, source: "live"));
        var heatmap = await client.GetFromJsonAsync<JsonElement>(query);
        Assert.Equal("heatmap", heatmap.GetProperty("visualizationType").GetString());
        Assert.Equal(0, heatmap.GetProperty("zones")[0].GetProperty("value").GetInt32());
        var token = Uri.EscapeDataString(Convert.ToBase64String(saved.RowVersion));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path + "?rowVersion=" + token)).StatusCode);
    }
}
