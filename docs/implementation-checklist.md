# Implementation checklist

1. Initialize the solution, dependency boundaries, validated configuration, SQL Server, OpenAPI and startup checks. Build the foundation.
2. Implement site, camera and zone commands and queries with SQL constraints and concurrency. Generate and verify migrations.
3. Validate the MQTT v2 contract and implement transactional ingestion, durable rejection, ordered state and manual acknowledgment. Verify with a broker.
4. Implement snapshots, cumulative deltas, sessions, visit estimates and coverage-aware occupancy. Test the calculations and transitions.
5. Implement the reporting endpoints and repeatable aggregation. Verify results against persisted records.
6. Add SignalR subscriptions and post-commit events. Test group routing and state synchronization.
7. Build the Angular 22 management, monitoring and analytics screens. Run frontend build and tests.
8. Run the local demo and automated suite. Document startup, formulas and verified integration boundaries.
