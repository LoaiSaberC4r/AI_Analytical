namespace CameraAnalytics.Domain.Entities;

public sealed class SiteFloor : CatalogRecord
{
    public Guid SiteId { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class ZoneSpatialLayout
{
    public Guid SiteFloorId { get; set; }
    public Guid CameraZoneId { get; set; }
    public CameraZone Zone { get; set; } = null!;
    public string VerticesJson { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
