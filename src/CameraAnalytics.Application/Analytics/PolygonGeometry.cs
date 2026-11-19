using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;

namespace CameraAnalytics.Application.Analytics;

public static class PolygonGeometry
{
    private const double Tolerance = 1e-12;

    public static void Validate(IReadOnlyList<PolygonVertex>? vertices, int limit)
    {
        if (vertices is null || vertices.Count < 3 || vertices.Count > limit
            || vertices.Any(v => v.X is not (>= 0 and <= 1) || v.Y is not (>= 0 and <= 1)))
            throw ApiError.Invalid($"A polygon requires 3 to {limit} finite vertices in the range [0,1].");
        if (vertices.Distinct().Count() != vertices.Count)
            throw ApiError.Invalid("Polygon vertices must be distinct.");

        var area = 0d;
        var count = vertices.Count;
        for (var i = 0; i < count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % count];
            var c = vertices[(i + 2) % count];
            if (Math.Abs(Cross(a, b, c)) < Tolerance)
                throw ApiError.Invalid("Adjacent polygon edges must form a non-collinear corner.");
            area += a.X * b.Y - b.X * a.Y;
            for (var k = i + 2; k < count && (i > 0 || k < count - 1); k++)
                if (Intersects(a, b, vertices[k], vertices[(k + 1) % count]))
                    throw ApiError.Invalid("Polygon edges must not intersect.");
        }
        if (Math.Abs(area) < Tolerance)
            throw ApiError.Invalid("The polygon must enclose a positive area.");
    }

    private static double Cross(PolygonVertex a, PolygonVertex b, PolygonVertex c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool Intersects(PolygonVertex a, PolygonVertex b, PolygonVertex c, PolygonVertex d)
    {
        var cSide = Side(a, b, c);
        var dSide = Side(a, b, d);
        var aSide = Side(c, d, a);
        var bSide = Side(c, d, b);
        return cSide * dSide < 0 && aSide * bSide < 0
            || cSide == 0 && Within(a, b, c) || dSide == 0 && Within(a, b, d)
            || aSide == 0 && Within(c, d, a) || bSide == 0 && Within(c, d, b);
    }

    private static int Side(PolygonVertex a, PolygonVertex b, PolygonVertex point)
    {
        var cross = Cross(a, b, point);
        return Math.Abs(cross) <= Tolerance ? 0 : Math.Sign(cross);
    }

    private static bool Within(PolygonVertex a, PolygonVertex b, PolygonVertex p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X)
        && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
}
