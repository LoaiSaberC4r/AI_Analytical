# Visualization contract

`GET /api/v1/analytics/visualizations/{metricKey}` returns schema version 1 and the `visualizationType` discriminator. An optional `visualizationType` query must match the metric whitelist. The response contains title, units, period, refresh interval and quality. Series repeat their exact resource scope. A null value means evidence was unavailable. It is rendered as an empty value or break in the chart. A measured zero remains visible.

| Metric | Supported types | Payload |
| --- | --- | --- |
| `arrivalsPerMinute`, `departuresPerMinute` | `line`, `area` | Time series |
| `arrivals` | `line`, `area`, `groupedBar` | Time series |
| `entryExit`, `missingSequences` | `groupedBar` | Time series |
| `wrongWay` | `line`, `groupedBar` | One directional series per zone and direction |
| `trafficBalance` | `divergingBar` | Time series with signed values |
| `averageOccupancy`, `staff` | `area`, `line` | Time series |
| `peakOccupancy`, `fps` | `line` | Time series |
| `activeDwell`, `completedDwell` | `histogram` | Bins with inclusive lower and exclusive upper seconds, null for the unbounded upper bin |
| `utilization` | `gauge` | Per-zone value, bounds and latest observed thresholds. The display bound accommodates measured utilization above 100 percent. |
| `congestion` | `timeline` | Episodes with period, severity and resolution reason |
| `dataQualityTimeline` | `timeline` | Camera observation status, coverage, sequence gaps and zone counter resets by bucket |

A series includes `seriesId`, `label`, `scope` and `points`. Each point has `period`, `value` and `quality`. A live period sends `startUtc` and `endUtc` with an offset-aware `localStart`. A video period sends `startSourceTimeMs` and `endSourceTimeMs` and its report specifies the selected loop. Clients select the axis using `timeBasis`. No chart code or layout configuration comes from the server.

`quality` contains `provenance`, `sampleCount`, `coveredSeconds`, `coveragePercent`, `gapCount`, `resetCount`, `baselineCount`, `isPartial`, `isStale` and `warningCodes`. A requested live bucket extends through the requested upper bound. When part of that bucket is unobserved, it contributes no covered duration and the client can display the partial marker.

The following type-specific fragments use illustrative values. Each fragment extends the same versioned response envelope, with the requested scope, period and full quality record.

```json
[
  {"visualizationType":"line","metricKey":"arrivalsPerMinute","series":[{"seriesId":"zone:arrivals","label":"Lobby","points":[{"period":{"startUtc":"2026-11-18T10:00:00Z","endUtc":"2026-11-18T10:01:00Z","localStart":"2026-11-18T12:00:00+02:00"},"value":4}]}]},
  {"visualizationType":"area","metricKey":"averageOccupancy","series":[{"seriesId":"zone:occupancy","label":"Lobby","points":[{"period":{"startSourceTimeMs":1000,"endSourceTimeMs":2000},"value":2.5}]}]},
  {"visualizationType":"groupedBar","metricKey":"entryExit","series":[{"seriesId":"zone:entry","label":"Lobby (Entry)","points":[{"value":3}]},{"seriesId":"zone:exit","label":"Lobby (Exit)","points":[{"value":5}]}]},
  {"visualizationType":"divergingBar","metricKey":"trafficBalance","series":[{"seriesId":"zone:balance","label":"Lobby","points":[{"value":-2}]}]},
  {"visualizationType":"histogram","metricKey":"completedDwell","series":[{"seriesId":"zone:dwell","label":"Lobby","bins":[{"label":"30–60 s","lowerSeconds":30,"upperSeconds":60,"value":3}]}]},
  {"visualizationType":"gauge","metricKey":"utilization","values":[{"label":"Lobby","value":108,"minimum":0,"maximum":125,"thresholds":[{"label":"Warning at latest observation","value":80}]}]},
  {"visualizationType":"timeline","metricKey":"congestion","items":[{"label":"Lobby","severity":"Critical","status":"Resolved","resolutionReason":"observed_recovery","observedSeconds":42}]},
  {"visualizationType":"heatmap","metricKey":"arrivals","zones":[{"layout":{"zoneName":"Lobby","vertices":[{"x":0.1,"y":0.1},{"x":0.4,"y":0.1},{"x":0.3,"y":0.4}]},"value":null}]}
]
```
