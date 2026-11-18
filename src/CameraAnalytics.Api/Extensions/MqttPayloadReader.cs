using System.Text.Json;
using System.Text.Json.Serialization;
using CameraAnalytics.Application.Abstractions;
using CameraAnalytics.Application.Common;
using CameraAnalytics.Contracts.Mqtt;
using CameraAnalytics.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace CameraAnalytics.Api.Extensions;

public sealed class MqttPayloadReader(IOptions<MqttOptions> settings) : IMqttPayloadReader
{
    public MqttPayloadReader() : this(Microsoft.Extensions.Options.Options.Create(new MqttOptions())) { }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        MaxDepth = 16,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        Converters = { new ExplicitOffsetConverter() }
    };

    public PayloadRead Read(string topic, ReadOnlySpan<byte> payload)
    {
        var limits = settings.Value;
        if (payload.Length > limits.MaxPayloadBytes)
            return new(null, "payload_too_large");
        var camera = Identifiers.CameraFromTopic(topic);
        if (camera is null)
            return new(null, "invalid_topic");
        CameraAnalyticsPayload? data;
        try
        {
            data = JsonSerializer.Deserialize<CameraAnalyticsPayload>(payload, Options);
        }
        catch (JsonException)
        {
            return new(null, "invalid_json_contract");
        }
        if (data is null)
            return new(null, "invalid_json_contract");
        if (data.SchemaVersion != "2.0")
            return new(null, "unsupported_schema");
        if (data.CameraId != camera)
            return new(null, "camera_topic_mismatch");
        if (!Identifiers.IsSegment(data.SessionId) || !Identifiers.IsSegment(data.SourceType)
            || data.SourceType.Length > 32 || string.IsNullOrWhiteSpace(data.SourceName)
            || data.SourceName.Any(char.IsControl) || data.Sequence < 0 || data.StaffCount < 0 || !Valid(data.ProcessingFps)
            || data.Status is not ("running" or "ended") || data.SourceTimeMs < 0 || data.VideoLoop < 0
            || data.Timestamp.UtcDateTime.Year is < 1900 or > 9900)
            return new(null, "invalid_camera_values");
        if (data.SourceName.Length > limits.MaxSourceNameLength)
            return new(null, "source_name_too_long");
        if (data.Zones.Length > limits.MaxZonesPerMessage)
            return new(null, "too_many_zones");
        if (!ValidMetadata(data))
            return new(null, "invalid_camera_metadata");

        var zones = new List<ZoneObservation>(data.Zones.Length);
        var zoneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in data.Zones)
        {
            if (zone is null || !Identifiers.IsSegment(zone.ZoneId) || !zoneIds.Add(zone.ZoneId))
                return new(null, "invalid_or_duplicate_zone");
            if (zone.Occupancy < 0 || zone.ZoneArrivals < 0 || zone.ZoneDepartures < 0 || zone.Entry < 0 || zone.Exit < 0
                || zone.WrongWayIn < 0 || zone.WrongWayOut < 0 || !Valid(zone.AverageDwellSeconds))
                return new(null, "invalid_zone_values");
            if (zone.ActiveVisitors.Length > limits.MaxActiveVisitorsPerZone)
                return new(null, "too_many_active_visitors");
            if (!ValidMetadata(zone))
                return new(null, "invalid_zone_metadata");
            var tracks = new HashSet<long>();
            var visitors = new List<VisitorObservation>(zone.ActiveVisitors.Length);
            foreach (var track in zone.ActiveVisitors)
            {
                if (track is null || track.TrackId < 0 || !Valid(track.DwellSeconds) || !tracks.Add(track.TrackId))
                    return new(null, "invalid_or_duplicate_track");
                visitors.Add(new(track.TrackId, track.DwellSeconds));
            }
            zones.Add(new(zone.ZoneId, zone.Occupancy,
                new(zone.ZoneArrivals, zone.ZoneDepartures, zone.Entry, zone.Exit, zone.WrongWayIn, zone.WrongWayOut),
                zone.AverageDwellSeconds, visitors, Metadata(zone)));
        }
        return new(new(camera, data.SourceType, data.SourceName, data.SessionId, data.SourceTimeMs,
            data.ProcessingFps, data.StaffCount, data.Status, data.Sequence, data.Timestamp.UtcDateTime, zones,
            data.SourceType == "video_file" ? data.VideoLoop : null, Metadata(data)), null);
    }

    private static bool ValidMetadata(OptionalAnalyticsFields fields) =>
        ValidCounts(fields) && (fields.Totals is null || ValidCounts(fields.Totals));

    private static bool ValidCounts(ReportedCountFields fields) => fields.NormalIn is not < 0 && fields.NormalOut is not < 0
        && fields.ActualGateIn is not < 0 && fields.ActualGateOut is not < 0 && fields.UnverifiedLosses is not < 0
        && fields.LoopBoundaryClosures is not < 0 && fields.ExclusionRemovals is not < 0;

    private static ReportedAnalyticsMetadata? Metadata(OptionalAnalyticsFields fields)
    {
        var counts = Counts(fields);
        var totals = fields.Totals is null ? null : Counts(fields.Totals);
        return counts is null && totals is null ? null : new(counts, totals);
    }

    private static ReportedCounts? Counts(ReportedCountFields fields) => fields is
        { NormalIn: null, NormalOut: null, ActualGateIn: null, ActualGateOut: null, GateBalanceDelta: null,
            UnverifiedLosses: null, LoopBoundaryClosures: null, ExclusionRemovals: null, ZoneBalanceDelta: null }
        ? null : new(fields.NormalIn, fields.NormalOut, fields.ActualGateIn, fields.ActualGateOut, fields.GateBalanceDelta,
            fields.UnverifiedLosses, fields.LoopBoundaryClosures, fields.ExclusionRemovals, fields.ZoneBalanceDelta);

    private static bool Valid(double value) => double.IsFinite(value) && value >= 0;

    private sealed class ExplicitOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Expected an ISO 8601 timestamp.");
            var value = reader.GetString()!;
            if (!value.EndsWith('Z') && (value.Length < 6 || value[^6] is not ('+' or '-') || value[^3] != ':'))
                throw new JsonException("Timestamp must include a UTC offset.");
            return reader.TryGetDateTimeOffset(out var timestamp)
                ? timestamp : throw new JsonException("Invalid timestamp.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
