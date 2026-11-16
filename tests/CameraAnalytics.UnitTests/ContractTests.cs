using System.Text;
using System.Text.Json.Nodes;
using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Domain.ValueObjects;

namespace CameraAnalytics.UnitTests;

public sealed class ContractTests
{
    private const string Topic = "cameras/1/analytics";
    private static readonly byte[] Sample = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "camera-analytics-v2.json"));
    private readonly MqttPayloadReader reader = new();

    [Theory]
    [InlineData("1", true)]
    [InlineData("Zone_01", true)]
    [InlineData("", false)]
    [InlineData(" x ", false)]
    [InlineData("x/y", false)]
    [InlineData("#", false)]
    [InlineData("x+y", false)]
    [InlineData("a\nb", false)]
    public void External_ids_respect_topic_segments(string value, bool valid) =>
        Assert.Equal(valid, Identifiers.IsSegment(value));

    [Theory]
    [InlineData("cameras/1/analytics", "1")]
    [InlineData("cameras/north-cam/analytics", "north-cam")]
    [InlineData("cameras/+/analytics", null)]
    [InlineData("cameras/1/analytics/status", null)]
    public void Topic_is_parsed(string topic, string? expected) => Assert.Equal(expected, Identifiers.CameraFromTopic(topic));

    [Fact]
    public void Parses_snake_case_and_keeps_source_time_separate_from_timestamp()
    {
        var result = reader.Read(Topic, Sample);
        var observation = Assert.IsType<CameraAnalytics.Application.Abstractions.CameraObservation>(result.Observation);
        Assert.Equal(25533, observation.SourceTimeMs);
        Assert.Equal(new DateTime(2026, 11, 16, 13, 17, 54, 687, DateTimeKind.Utc), observation.SourceTimestampUtc);
        Assert.Equal(23.7, observation.Zones[2].LongestActiveDwell);
        Assert.Null(observation.Zones[0].LongestActiveDwell);
    }

    [Fact]
    public void Rejects_payload_camera_mismatch() =>
        Assert.Equal("camera_topic_mismatch", reader.Read("cameras/other/analytics", Sample).Rejection);

    [Theory]
    [InlineData("sequence")]
    [InlineData("source_time_ms")]
    [InlineData("staff_count")]
    [InlineData("processing_fps")]
    public void Rejects_missing_required_fields(string name)
    {
        var json = Parse();
        json.Remove(name);
        Assert.Equal("invalid_json_contract", Read(json));
    }

    [Theory]
    [InlineData("occupancy")]
    [InlineData("zone_arrivals")]
    [InlineData("average_dwell_seconds")]
    [InlineData("active_visitors")]
    public void Rejects_missing_zone_fields(string name)
    {
        var json = Parse();
        json["zones"]![0]!.AsObject().Remove(name);
        Assert.Equal("invalid_json_contract", Read(json));
    }

    [Fact]
    public void Unknown_fields_and_no_zones_are_allowed()
    {
        var json = Parse();
        json["new_info"] = "ignored";
        json["zones"] = new JsonArray();
        Assert.Null(Read(json));
    }

    [Fact]
    public void A_null_video_position_is_valid_metadata()
    {
        var json = Parse();
        json["source_time_ms"] = null;
        var read = reader.Read(Topic, Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.NotNull(read.Observation);
        Assert.Null(read.Observation.SourceTimeMs);
        Assert.Null(read.Observation.VideoLoop);
    }

    [Theory]
    [InlineData("null", null, null)]
    [InlineData("0", 0L, null)]
    [InlineData("21", 21L, null)]
    [InlineData("-1", null, "invalid_camera_values")]
    [InlineData("\"21\"", null, "invalid_json_contract")]
    [InlineData("1.5", null, "invalid_json_contract")]
    public void Reads_optional_video_loop_as_a_nonnegative_integer(string field, long? loop, string? error)
    {
        var json = Parse();
        json["video_loop"] = JsonNode.Parse(field);
        var result = reader.Read(Topic, Encoding.UTF8.GetBytes(json.ToJsonString()));
        Assert.Equal(error, result.Rejection);
        Assert.Equal(loop, result.Observation?.VideoLoop);
    }

    [Fact]
    public void Rejects_duplicate_zones()
    {
        var json = Parse();
        json["zones"]![0]!["zone_id"] = "zone_02";
        Assert.Equal("invalid_or_duplicate_zone", Read(json));
    }

    [Fact]
    public void Rejects_wrong_types_and_negative_counters()
    {
        var json = Parse();
        json["zones"]![0]!["zone_arrivals"] = -1;
        Assert.Equal("invalid_zone_values", Read(json));
        json["zones"]![0]!["zone_arrivals"] = "1";
        Assert.Equal("invalid_json_contract", Read(json));
    }

    [Fact]
    public void Rejects_ambiguous_timestamps_and_nonfinite_values()
    {
        var json = Parse();
        json["timestamp"] = "2026-11-16T15:17:54";
        Assert.Equal("invalid_json_contract", Read(json));
        var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Sample).Replace("\"processing_fps\": 8", "\"processing_fps\": 1e400"));
        Assert.Equal("invalid_camera_values", reader.Read(Topic, bytes).Rejection);
    }

    private static JsonObject Parse() => JsonNode.Parse(Sample)!.AsObject();
    private string? Read(JsonObject json) => reader.Read(Topic, Encoding.UTF8.GetBytes(json.ToJsonString())).Rejection;
}
