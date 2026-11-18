using System.Text;
using System.Text.Json.Nodes;
using CameraAnalytics.Api.Extensions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Domain.ValueObjects;
using Microsoft.Extensions.Options;

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

    [Fact]
    public void Optional_metadata_keeps_signed_zero_and_absent_values_separate()
    {
        Assert.Null(reader.Read(Topic, Sample).Observation!.ReportedMetadata);
        var json = Parse();
        json["actual_gate_in"] = 0;
        json["gate_balance_delta"] = -4;
        json["totals"] = new JsonObject { ["actual_gate_in"] = 8 };
        json["zones"]![0]!["zone_balance_delta"] = -2;
        var data = reader.Read(Topic, Encoding.UTF8.GetBytes(json.ToJsonString())).Observation!;
        Assert.Equal(0, data.ReportedMetadata!.Counts!.ActualGateIn);
        Assert.Null(data.ReportedMetadata.Counts.ActualGateOut);
        Assert.Equal(-4, data.ReportedMetadata.Counts.GateBalanceDelta);
        Assert.Equal(8, data.ReportedMetadata.Totals!.ActualGateIn);
        Assert.Equal(-2, data.Zones[0].ReportedMetadata!.Counts!.ZoneBalanceDelta);
        Assert.Null(data.Zones[1].ReportedMetadata);
    }

    [Theory]
    [InlineData("-1", "invalid_camera_metadata")]
    [InlineData("1.25", "invalid_json_contract")]
    [InlineData("\"1\"", "invalid_json_contract")]
    [InlineData("9223372036854775808", "invalid_json_contract")]
    public void Rejects_invalid_optional_count(string value, string reason)
    {
        var json = Parse();
        json["normal_in"] = JsonNode.Parse(value);
        Assert.Equal(reason, Read(json));
    }

    [Fact]
    public void Rejects_duplicate_metadata_and_tracks()
    {
        var json = Parse().ToJsonString().Insert(1, "\"normal_in\":1,\"normal_in\":2,");
        Assert.Equal("invalid_json_contract", reader.Read(Topic, Encoding.UTF8.GetBytes(json)).Rejection);
        var payload = Parse();
        var visitors = payload["zones"]![2]!["active_visitors"]!.AsArray();
        visitors.Add(visitors[0]!.DeepClone());
        Assert.Equal("invalid_or_duplicate_track", Read(payload));
    }

    [Fact]
    public void Applies_configured_limits()
    {
        Assert.Equal("payload_too_large", ReadWith(new() { MaxPayloadBytes = Sample.Length - 1 }));
        Assert.Equal("source_name_too_long", ReadWith(new() { MaxSourceNameLength = 1 }));
        Assert.Equal("too_many_zones", ReadWith(new() { MaxZonesPerMessage = 1 }));
        var json = Parse();
        var visitors = json["zones"]![2]!["active_visitors"]!.AsArray();
        visitors.Add(visitors[0]!.DeepClone());
        Assert.Equal("too_many_active_visitors", ReadWith(new() { MaxActiveVisitorsPerZone = 1 },
            Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    private static string? ReadWith(MqttOptions limits, byte[]? payload = null) =>
        new MqttPayloadReader(Options.Create(limits)).Read(Topic, payload ?? Sample).Rejection;

    private static JsonObject Parse() => JsonNode.Parse(Sample)!.AsObject();
    private string? Read(JsonObject json) => reader.Read(Topic, Encoding.UTF8.GetBytes(json.ToJsonString())).Rejection;
}
