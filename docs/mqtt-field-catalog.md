# MQTT field catalog

Schema `2.0` uses the UTF-8 JSON message and camera topic shown in the [checked-in fixture](../samples/camera-analytics-v2.json). External identifiers are case-sensitive. `source_time_ms` is required and accepts null. `video_loop` is optional. Timestamps require an explicit offset. Unknown JSON properties remain tolerated and duplicate properties are rejected.

| Scope | Verified fields | Meaning in this repository |
| --- | --- | --- |
| Camera | `schema_version`, `camera_id`, `source_type`, `source_name`, `session_id`, `sequence`, `timestamp`, `status` | Protocol, ordered message identity, source metadata and source time |
| Camera | `source_time_ms`, `video_loop` | Video position in milliseconds, and optional loop number |
| Camera | `processing_fps`, `staff_count` | Reported instantaneous camera values |
| Zone | `zone_id`, `occupancy`, `average_dwell_seconds` | Zone identity and reported values |
| Zone | `zone_arrivals`, `zone_departures`, `entry`, `exit`, `wrong_way_in`, `wrong_way_out` | Cumulative counters with derived positive changes and independent reset flags |
| Zone | `active_visitors` | Camera/session/loop/zone-scoped `track_id` and `dwell_seconds`, used internally for dwell tracking |

None of the proposed extensions appears in the baseline fixture, publisher or tests. The additive reader accepts the following nullable fields at camera and zone scope. The types below are the accepted repository wire format pending confirmation with the producer.

| Optional fields | Accepted format |
| --- | --- |
| `normal_in`, `normal_out`, `actual_gate_in`, `actual_gate_out`, `unverified_losses`, `loop_boundary_closures`, `exclusion_removals` | Nonnegative signed-64-bit JSON integer values |
| `gate_balance_delta`, `zone_balance_delta` | Signed 64-bit JSON integer values |
| `totals` | Object with the same recognized optional fields. Recognized values are kept separately and unknown members are ignored. |

Missing and null values are stored as absent. Supplied zero is preserved. The receiver stores recognized fields as bounded, nullable reported metadata on the source snapshot. Derived metrics continue to use the established zone counters. Confirm each field's scope, units, reset boundaries, whether it is cumulative or interval-based, and whether `totals` overlaps zone counts before using it in any KPI or consistency check.

The server checks `Mqtt__MaxPayloadBytes` before deserializing. It then applies `Mqtt__MaxSourceNameLength`, `Mqtt__MaxZonesPerMessage` and `Mqtt__MaxActiveVisitorsPerZone`. Failed checks persist a bounded rejection reason under the existing QoS 1 acknowledgment rules.
