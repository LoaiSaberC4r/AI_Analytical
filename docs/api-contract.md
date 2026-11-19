# API contract

Swagger lists the 37 original operations and the smart analytics additions at `/swagger/v1/swagger.json`. The base route is `/api/v1`. HTTP uses camelCase JSON and GUID identifiers. External IDs are case-sensitive strings within their registry namespace.

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
- For `video_file` reports use `sessionId` (or the session report path) and optional `fromSourceTimeMs` / `toSourceTimeMs`. A report covers one playback loop and defaults to the latest accepted loop. Set `videoLoop=21` to select a numbered loop or `videoLoop=unmarked` to select history before loop numbering was known. A numbered loop with no accepted observations returns 404. Source positions and default bounds are relative to the selected loop; the upper bound is exclusive. Peak-hours always uses hour buckets.
- A numbered video report includes `period.videoLoop`. Session metadata includes `lastVideoLoop` once known, and video session reports list `availableVideoLoops` (a null entry denotes unmarked history). The untimed observation count covers the selected loop. The session's `firstSourceTimeMs` still refers to the session's first known position, and `lastSourceTimeMs` refers to the last known position in its latest loop.
- Ranking metrics are `arrivals`, `entries`, `departures`, `exits`, `averageOccupancy`, `peakOccupancy`, `utilization`, `reportedAverageDwell`, `estimatedAverageDwell`, `longestActiveDwell`, and `wrongWayEvents`. The dwell ranking accepts the three dwell metrics.

## SignalR

Default hub `/hubs/analytics`. Call `SubscribeToSite(siteId)` or `SubscribeToCamera(cameraId)` and use the corresponding unsubscribe methods to leave. Groups route updates by site and camera. ID validation and site access checks apply before joining.

| Event | Fields |
| --- | --- |
| `CameraSnapshotUpdated` | cameraId, siteId, sessionId, sequence, stateVersion, sourceType, analyticsStatus, dataFreshness, processingFps, staffCount, receivedAtUtc |
| `ZoneMetricsUpdated` | cameraId, zoneId, sessionId, sequence, stateVersion, occupancy, arrivals, departures, entry, exit, wrongWayIn, wrongWayOut, reportedAverageDwellSeconds, activeVisitorsCount, longestActiveDwellSeconds, receivedAtUtc |
| `CameraStatusChanged` | cameraId, siteId, sessionId, stateVersion, analyticsStatus, dataFreshness, receivedAtUtc |
| `AnalyticsSummaryUpdated` | cameraId, siteId, sessionId, stateVersion, receivedAtUtc |
| `AnalyticsAlertChanged` | alertId, siteId, cameraId, stateVersion, status |

Updates are sent to matching groups after SQL commit. The summary event is a refresh signal. Use REST after reconnect, and apply camera and zone version checks before replacing displayed values.

## Smart analytics and operations

| View | Method and path relative to `/api/v1` |
| --- | --- |
| Analytics | GET `/analytics/traffic/flow-rate`, `/analytics/congestion`, `/analytics/congestion/history`, `/analytics/dwell/distribution`, `/analytics/dwell/long-stay`, `/analytics/quality` |
| Visualizations | GET `/analytics/visualizations/{metricKey}`, `/analytics/heatmap` |
| Insights and history | GET `/analytics/insights`, `/analytics/comparisons`, `/analytics/forecast` |
| Alerts | GET `/alerts`, `/alerts/{id}` · POST `/alerts/{id}/acknowledge`, `/alerts/{id}/resolve` |
| Rules | GET and POST `/alert-rules` · GET and PUT `/alert-rules/{id}` · PATCH `/alert-rules/{id}/status` |
| Floors | GET and POST `/sites/{siteId}/floors` · GET and PUT `/floors/{id}` · PATCH `/floors/{id}/status` |
| Layouts | GET `/floors/{id}/layouts` · PUT and DELETE `/floors/{id}/zones/{zoneId}/layout` |

The smart reports reuse catalog filters. Select a site, camera, zone or session. Authenticated site-scoped reads supply `siteId` and may narrow to resources in that site. Video reports require `sessionId`, `videoLoop`, `fromSourceTimeMs` and `toSourceTimeMs`. Use `videoLoop=unmarked` for legacy untagged loops. [Visualization contract](visualization-contract.md) · [Floor layouts](floor-layouts.md)

Alert and rule lists require `siteId`. Alerts also support `cameraId`, `status` and paging. Rule and layout edits require the latest `rowVersion`. Layout DELETE takes `rowVersion` as a query parameter. See the lifecycle policy and required scopes. [Alerts and insights](alerts-and-insights.md) · [Security and deployment](security-and-deployment.md)

The closed congestion history route pages full durable episodes overlapping the requested range. The existing smart congestion route clips measured intervals to the report and can include an episode still in progress. Comparisons accept `metricKey` and `alignment`. Forecast requires `zoneId` and evaluates live occupancy only. [Comparisons and forecast](forecasting.md)
