# MQTT integration

The external publisher sends JSON schema `2.0` on `cameras/{camera_id}/analytics` with QoS 1. The default filter is `cameras/+/analytics`. A single-camera filter is also supported. Camera ID in the topic must exactly match the payload. Fields and types are shown in [the sample](../samples/camera-analytics-v2.json). Unknown JSON fields are tolerated. A zero-length zone array keeps previous zone readings. Unknown IDs and invalid values lead to durable bounded rejection metadata.

See the [MQTT field catalog](mqtt-field-catalog.md) for nullable reported extensions, signed balance fields and the accepted array and string limits. The receiver preserves byte identity, ordering and acknowledgment rules for the original payload.

For `video_file`, the optional `video_loop` is a non-negative integer that increases whenever playback restarts. Keep the same `session_id` and continue increasing `sequence` across loops. The source timestamp still has to be non-decreasing. Within a loop, a known `source_time_ms` cannot go backward. The first numbered observation in an unmarked session establishes a loop and can rewind, as can any later observation with a higher loop number. Earlier numbered loops and same-loop rewinds are stored as historical evidence and do not update live state. Duplicate identity remains camera, session and sequence.

When `video_loop` is omitted or null, the message can continue the last known loop (or the unmarked portion of a session) under the usual sequence, timestamp and position checks. A rewind cannot be recognized without a loop number, and an unmarked message from an earlier loop cannot be identified if it passes those other checks. Send the loop on every video message for reliable ordering.

## Broker

With Mosquitto on a single development computer:

```powershell
mosquitto -p 1883
$env:Mqtt__Host = '127.0.0.1'
```

For the approved LAN broker, use the default appsettings host `192.168.1.8` and configure Mosquitto's listener, persistence and ACL on the broker machine. Enable persistence if the broker should retain queued QoS 1 delivery across broker restarts. Keep one subscriber Client ID per consumer with persistent sessions enabled. For MQTT 5, `SessionExpirySeconds` sets session lifetime.

A broker operator can configure an authenticated listener with `allow_anonymous false`, `password_file`, and a read-only subscriber ACL for `cameras/+/analytics`. Give publisher accounts write permission only to their allowed camera topics. For TLS configure a TLS listener with the broker hostname on its certificate, trust its issuing CA on the API host, and set `Mqtt__UseTls=true`, `Mqtt__Host` and `Mqtt__Port` to that listener.

## Verify

Use [the publisher script](../samples/publish-mqtt.ps1) during live monitoring. The script creates a new session each run by default. Verify the sequence and per-zone counters follow successful publishes. In SQL, `ReceivedAnalyticsMessages` keeps one row per accepted session/sequence, `ZoneCounterDeltas` records baselines, resets and valid increments, and `IngestionRejections` records poison-message or conflict reasons. Scope queries to the demo session.

The automated broker integration test injects a temporary SQL constraint in its own test database, publishes one QoS 1 message, verifies the rollback, removes the constraint and waits for broker redelivery using the persistent ID. It also checks duplicates and invalid payloads. Run the test with the variables in the README.

Connection failures retry at the configured interval. A received message is acknowledged after durable handling. For duplicate identities with identical bytes, the committed receipt satisfies this requirement. For conflicts, the original record remains and rejection metadata records the conflict. QoS 0 cannot recover an unacknowledged delivery. When changing broker settings, keep application and broker session policies consistent.
