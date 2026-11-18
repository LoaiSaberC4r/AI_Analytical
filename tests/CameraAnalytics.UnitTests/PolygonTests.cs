using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Analytics;
using CameraAnalytics.Application.Common;

namespace CameraAnalytics.UnitTests;

public sealed class PolygonTests
{
    [Fact]
    public void Validates_bounds_crossings_duplicates_collinear_and_size()
    {
        PolygonVertex[] polygon = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        PolygonGeometry.Validate(polygon, 4);
        Assert.Throws<ApiError>(() => PolygonGeometry.Validate(polygon, 3));
        Assert.Throws<ApiError>(() => PolygonGeometry.Validate([polygon[0], polygon[2], polygon[1], polygon[3]], 4));
        Assert.Throws<ApiError>(() => PolygonGeometry.Validate([polygon[0], polygon[1], polygon[0]], 4));
        Assert.Throws<ApiError>(() => PolygonGeometry.Validate([polygon[0], polygon[1], new(0.5, 0)], 4));
        Assert.Throws<ApiError>(() => PolygonGeometry.Validate([polygon[0], polygon[1], new(double.NaN, 0)], 4));
        Assert.Throws<ApiError>(() => PolygonGeometry.Validate([polygon[0], polygon[1], new(-0.1, 1.1)], 4));
    }
}
