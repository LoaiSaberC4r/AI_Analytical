namespace CameraAnalytics.Application.Common;

public sealed class ApiError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static ApiError Invalid(string message) => new(400, "invalid_request", message);
    public static ApiError NotFound(string resource) => new(404, "not_found", $"{resource} was not found.");
    public static ApiError Conflict(string message) => new(409, "conflict", message);
    public static ApiError Concurrency() => new(409, "concurrency_conflict", "This record changed. Reload it before saving.");
}

public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    [System.Text.Json.Serialization.JsonPropertyName("page")]
    public int PageNumber { get; init; } = PageNumber;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ListFilter(int Page = 1, int PageSize = 20, string? Search = null);
public sealed record Saved(Guid Id);
