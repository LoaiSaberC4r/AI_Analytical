# Aggregation and retention

Live ingestion retains ordered SQL snapshots, per-zone counter changes and visit facts. The ingestion transaction coalesces each affected zone and camera UTC minute in the corresponding dirty queue. The ending sample owns traffic deltas. A later observation can close an earlier interval and requeue an already finalized minute. Video reports continue to read one explicit session and video loop from source facts.

The write path inserts deduplicated hour and minute keys in one parameterized SQL batch under the existing transaction lock. A failed queue insert rolls back the observations and the receipt. Time-leading filtered indexes on ordered snapshots support time-window backfill. The rejection index supports scoped topic and receipt-time lookups.

`MinuteAggregationWorker` waits until the minute ends plus the greater of the configured finalization delay and maximum snapshot gap. It recomputes a full minute from source facts under the existing writer lock, then replaces the keyed aggregate and clears its dirty key in one transaction. Camera rows capture staff duration and FPS sample statistics. Zone rows capture occupancy, counter deltas, dwell estimates and represented utilization and congestion duration. Both tables and queues use a unique resource plus UTC minute key. The pre-existing hour worker maintains its own keys and rollups.

Smart minute flow reads clean full minutes and fills missing, dirty or partial segments using raw facts. It clips intervals at each segment boundary. Historical ranges beyond available facts or rollups may have zero coverage and unavailable weighted values. Traffic counters remain available wherever the underlying changes were retained. Existing report routes use their original hour and raw query paths.

Backfill discovers keys using retained ordered snapshots in a fixed window anchored at first worker startup. It advances a persisted cursor in one-hour transactions and can resume after interruption. `MinuteAggregation__BackfillDays=0` disables discovery. Ingestion continues adding dirty keys when minute aggregation is enabled. A completed cursor records the original backfill window.

Retention defaults to off. With retention enabled, the hour worker protects raw data while relevant repairs or initial backfill are pending. The minute worker removes expired clean aggregates in bounded batches. The stored message identities, video data, visit facts and counter deltas keep their existing retention behavior.
