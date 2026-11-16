# API contract

The 37 Core operations are listed in Swagger at `/swagger/v1/swagger.json`. The base route is `/api/v1`. HTTP uses camelCase JSON and GUID identifiers. External IDs are case-sensitive strings within their registry namespace.

| Resource | Method and path relative to the base |
| --- | --- |
| Sites | POST /sites · GET /sites · GET /sites/{id} · PUT /sites/{id} · PATCH /sites/{id}/status |
| Cameras | POST /cameras · GET /cameras · GET /cameras/{id} · PUT /cameras/{id} · PATCH /cameras/{id}/status |
| Zones | POST /cameras/{cameraId}/zones · GET /cameras/{cameraId}/zones · GET /zones/{id} · PUT /zones/{id} · PATCH /zones/{id}/status |
| Live | GET /sites/{siteId}/live · GET /cameras/{cameraId}/live · GET /zones/{zoneId}/live · GET /monitoring/status |
| Overview and zones | GET /analytics/overview · GET /analytics/zones/rankings · GET /analytics/zones/{zoneId}/summary · GET /analytics/zones/{zoneId}/timeseries |
| Occupancy | GET /analytics/occupancy · GET /analytics/peak-hours · GET /analytics/occupancy/utilization |
| Traffic | GET /analytics/traffic · GET /analytics/traffic/trends |
| Dwell | GET /analytics/dwell · GET /analytics/dwell/rankings |
| Wrong-way and staff | GET /analytics/wrong-way · GET /analytics/staff |
| Sessions | GET /analytics/sessions · GET /analytics/sessions/{id} · GET /analytics/sessions/{id}/report |
| Health | GET /health/live · GET /health/ready |

List queries accept `page` (default 1), `pageSize` (default 20, maximum 100) and `search`. Responses contain `items`, `page`, `pageSize`, `totalCount` and `totalPages`. Camera and session lists accept `siteId`, and session lists accept `cameraId` and `sourceType`.

POST returns 201 with the new record and Location. PUT returns the updated record. Pass the current record's Base64 `rowVersion` in each PUT and PATCH. A status patch takes both `isActive` and `rowVersion` and returns 204. A stale token returns 409 with code `concurrency_conflict`. API errors use ProblemDetails with `code` and `traceId`. Validation is 400, missing resources 404, conflicts 409 and unexpected failures 500.

## Reporting parameters

- Filter by `siteId`, `cameraId` and `zoneId`. Supply `from` and `to` as UTC or offset timestamps for `sourceType=live`. Dates use half-open ranges. Site-scoped buckets use the site's time zone. The default for an unscoped report is UTC.
- Set `interval` to `minute`, `hour` or `day`. The default range limit is seven days for minute buckets and 90 days for hour and day buckets. Both limits are configurable.
- For `video_file` reports use `sessionId` (or the session report path) and optional `fromSourceTimeMs` / `toSourceTimeMs`. Filters and response times are source-relative. Peak-hours always uses hour buckets.
- Ranking metrics are `arrivals`, `entries`, `departures`, `exits`, `averageOccupancy`, `peakOccupancy`, `utilization`, `reportedAverageDwell`, `estimatedAverageDwell`, `longestActiveDwell`, and `wrongWayEvents`. The dwell ranking accepts the three dwell metrics.

## SignalR

Default hub `/hubs/analytics`. Call `SubscribeToSite(siteId)` or `SubscribeToCamera(cameraId)` and use the corresponding unsubscribe methods to leave. Successful subscriptions join `site:{guid}` or `camera:{guid}`. IDs are validated against the registry.

| Event | Fields |
| --- | --- |
| `CameraSnapshotUpdated` | cameraId, siteId, sessionId, sequence, stateVersion, sourceType, analyticsStatus, dataFreshness, processingFps, staffCount, receivedAtUtc |
| `ZoneMetricsUpdated` | cameraId, zoneId, sessionId, sequence, stateVersion, occupancy, arrivals, departures, entry, exit, wrongWayIn, wrongWayOut, reportedAverageDwellSeconds, activeVisitorsCount, longestActiveDwellSeconds, receivedAtUtc |
| `CameraStatusChanged` | cameraId, siteId, sessionId, stateVersion, analyticsStatus, dataFreshness, receivedAtUtc |
| `AnalyticsSummaryUpdated` | cameraId, siteId, sessionId, stateVersion, receivedAtUtc |

Updates are sent to matching groups after SQL commit. The summary event is a refresh signal. Use REST after reconnect, and apply camera and zone version checks before replacing displayed values.
