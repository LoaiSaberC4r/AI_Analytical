using CameraAnalytics.Domain.ValueObjects;

namespace CameraAnalytics.Application.Common;

public static class Input
{
    public static string Required(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
            throw ApiError.Invalid($"{field} is required and must have at most {max} characters.");
        return value.Trim();
    }

    public static string? Description(string? value)
    {
        if (value?.Length > 1000)
            throw ApiError.Invalid("Description must have at most 1000 characters.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string ExternalId(string? value, string field) => Identifiers.IsSegment(value)
        ? value! : throw ApiError.Invalid($"{field} must be a valid non-empty identifier without '/', '+' or '#'.");

    public static TimeZoneInfo TimeZone(string? id)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(Required(id, "TimeZoneId", 128), out var zone))
            throw ApiError.Invalid("TimeZoneId is not recognized.");
        return zone;
    }

    public static byte[] RowVersion(string? value)
    {
        Span<byte> token = stackalloc byte[8];
        if (value is null || !Convert.TryFromBase64String(value, token, out var written) || written != token.Length)
            throw ApiError.Invalid("An 8-byte Base64 rowVersion is required.");
        return token.ToArray();
    }

    public static int Skip(ListFilter filter)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100
            || (long)(filter.Page - 1) * filter.PageSize > int.MaxValue || filter.Search?.Length > 128)
            throw ApiError.Invalid("Use page >= 1, pageSize between 1 and 100 and a search of at most 128 characters.");
        return (filter.Page - 1) * filter.PageSize;
    }
}
