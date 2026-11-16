namespace CameraAnalytics.Domain.ValueObjects;

public static class Identifiers
{
    public const int ExternalIdLength = 128;

    public static bool IsSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= ExternalIdLength && value == value.Trim()
        && !value.Any(c => char.IsControl(c) || c is '/' or '+' or '#');

    public static string? CameraFromTopic(string topic)
    {
        var parts = topic.Split('/');
        return parts.Length == 3 && parts[0] == "cameras" && parts[2] == "analytics" && IsSegment(parts[1])
            ? parts[1] : null;
    }

    public static string Topic(string camera) => $"cameras/{camera}/analytics";

    public static bool IsTopicFilter(string filter)
    {
        var parts = filter.Split('/');
        return parts.Length == 3 && parts[0] == "cameras" && parts[2] == "analytics"
            && (parts[1] == "+" || IsSegment(parts[1]));
    }
}
