# Architecture

Camera Analytics is a modular monolith with an Angular 22 standalone application. The Domain contains entities, statuses and identifiers. Application defines query/command boundaries and processing policies. Infrastructure owns SQL Server, MQTTnet and workers. API owns HTTP and SignalR transports and the adapter for MQTT JSON in the dependency-free Contracts assembly.

## Write path

1. The MQTT client awaits each delivery handler. The adapter checks the topic, payload length, required fields, types and schema version.
2. The SQL handler resolves registered IDs and checks active state. A SQL transaction-scoped writer lock serializes analytics mutations for the demo. Unique constraints independently protect message identity and zone delta identity.
3. An accepted message commits its receipt, session, snapshots, deltas, visit changes and current state in one transaction. Oversized or invalid input gets a bounded rejection record. An already committed byte-identical message needs no new record.
4. SignalR publishes versioned updates after commit. A notification failure is logged. The MQTT client acknowledges a durable outcome. Failed writes leave the delivery unacknowledged and trigger reconnection using the same Client ID.

One active subscriber per configured broker namespace is sufficient for the demo. MQTT clients using the same Client ID will disconnect each other. Historical reads use indexed SQL queries and no entity tracking. Catalog writes use SQL Server rowversion.

## SQL schema

`InitialCreate` creates catalog, sessions, receipts, rejections, snapshots, current state, counters, visits, hourly aggregates and the hourly refresh queue. `ObservationCoverage` adds bounded observation intervals and preserves dwell and higher-precision counter totals in rollups.

Persisted state keeps the last known readings across disconnects. REST live reads use a shared SQL lock so camera and zone data are read from the same committed state. A zone omitted from a message retains its earlier sample and age. A zone retained from an older session is flagged. Disabling a site or camera prevents ingestion while preserving history. Registered disabled zones are skipped.

## Reporting

The aggregation worker refreshes completed UTC hours from persisted observations using an idempotent replacement. Writes affecting a closed hour enqueue it again. Queries use clean UTC rollups where an entire rollup fits inside the requested reporting bucket. Partial windows and minute buckets use indexed raw observations and session reports use their own timeline. Empty periods carry zero duration and unavailable occupancy estimates. Staff series stream indexed camera observations.

The Angular connection service rejoins its selected group and re-fetches REST state when the connection reconnects. Version checks guard against stale events and overlapping REST responses. A periodic REST refresh covers temporarily unavailable or disabled SignalR.
