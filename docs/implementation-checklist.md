# Implementation checklist

1. Initialize the solution, dependency boundaries, validated configuration, SQL Server, OpenAPI and startup checks. Build the foundation.
2. Implement site, camera and zone commands and queries with SQL constraints and concurrency. Generate and verify migrations.
3. Validate the MQTT v2 contract and implement transactional ingestion, durable rejection, ordered state and manual acknowledgment. Verify with a broker.
4. Implement snapshots, cumulative deltas, sessions, visit estimates and coverage-aware occupancy. Test the calculations and transitions.
5. Implement the reporting endpoints and repeatable aggregation. Verify results against persisted records.
6. Add SignalR subscriptions and post-commit events. Test group routing and state synchronization.
7. Build the Angular 22 management, monitoring and analytics screens. Run frontend build and tests.
8. Run the local demo and automated suite. Document startup, formulas and verified integration boundaries.

## Smart analytics backend gates

| Gate | Delivery and verification |
| --- | --- |
| 0 | Branch baseline recorded, SQL secret removed from defaults, production JWT and site-scoped authorization tested. |
| 1 | Nullable schema 2.0 metadata and bounded input validation verified alongside the original delivery contract. |
| 2 | Flow, time-weighted occupancy, camera quality, capacity, dwell and congestion covered by pure calculations and SQL report checks. |
| 3 | Camera and zone minute rollups, backfill cursor, retention guards and atomic queue writes. SQL tests cover duplicate, late repair, rollback and seeded migration cases. |
| 4 | Additive smart routes and typed visualizations, with an exact assertion for every original Swagger method and path. |
| 5 | Site-scoped floor metadata and normalized polygons, versioned writes and real measurement heatmaps. |
| 6 | Durable alert lifecycle, after-commit notifications, congestion history and deterministic insights. |
| 7 | Comparable periods and gated hourly forecast, with deterministic local-time tests and clean SQL rollup fixtures. |
| 8 | Angular excluded from delivery by request. |
| 9 | Real-broker load harness, SQL reconciliation, telemetry and deployment documentation. See [load test results](load-test-results.md) for conditions and remaining performance limits. |
