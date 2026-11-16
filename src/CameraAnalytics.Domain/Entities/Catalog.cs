namespace CameraAnalytics.Domain.Entities;

public abstract class CatalogRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Site : CatalogRecord
{
    public string Code { get; set; } = "";
    public string TimeZoneId { get; set; } = "";
}

public sealed class Camera : CatalogRecord
{
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    public string ExternalCameraId { get; set; } = "";
}

public sealed class CameraZone : CatalogRecord
{
    public Guid CameraId { get; set; }
    public Camera Camera { get; set; } = null!;
    public string ExternalZoneId { get; set; } = "";
    public int? Capacity { get; set; }
    public double WarningThresholdPercent { get; set; }
    public double CriticalThresholdPercent { get; set; }
}
