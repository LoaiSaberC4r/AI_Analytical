# Analytics definitions

| Metric | Provenance | Definition |
| --- | --- | --- |
| Occupancy and camera staff count | Reported | The latest valid field from a registered source. Missing first readings are null. Stale and previous-session readings retain their values and are flagged. |
| Peak | Derived | Maximum observed sample in the selected period, with its event timestamp or video position. |
| Weighted occupancy or staff | Derived | Sum of value × represented seconds divided by total represented seconds. A sample represents time only when a following ordered sample closes its observation interval. Long or missing-sequence gaps represent at most the configured expected interval. |
| Coverage | Derived | Represented seconds divided by the requested bucket duration. Zero coverage can coexist with an observed peak. Empty intervals return null average and peak. |
| Peak hour | Derived | Highest weighted occupancy among buckets meeting the coverage threshold. Traffic peaks select the highest positive count for each metric independently. Partial boundary buckets use the selected portion as their coverage denominator. |
| Most crowded zone | Derived | Highest weighted average occupancy. Instantaneous peak occupancy has its own ranking. |
| Observed zone average | Derived | Pooled occupancy-seconds divided by covered zone-seconds across the selected zones. |
| Utilization | Derived | Occupancy ÷ currently configured capacity × 100. Unknown capacity yields null. Values may exceed 100%. |
| Arrivals, departures, entries, exits and wrong-way counters | Derived | Positive changes between observations in the same camera, zone and session. First observations establish a baseline. A decrease records a reset for that counter and uses the new value as a baseline. Other monotonic counters keep their valid increments. A gap is flagged and an increment is assigned to the ending sample's time. |
| Most visited / top entry | Derived | Highest observed arrival increment total / highest entry increment total respectively. Zone counters count events within that zone. |
| Wrong-way | Derived | Separate direction totals and their sum, with counts by zone and time bucket. |
| Reported average dwell | Reported | Exact field from the latest matching snapshot. Each reported average remains an individual observation. |
| Longest active dwell | Derived | Maximum track dwell in the live snapshot. Historical ranking takes the maximum observed active dwell in its selected window. |
| Estimated visits and dwell | Estimated | Resolve a visit after the track is absent for the configured grace period in consecutive valid zone observations. Sum its last observed dwell once. Divide by the estimated-completed count for the historical average, with null when there are zero completions. |

Visit identity is scoped to camera, session, video loop, zone and track observation. A dwell regression or observation gap can split the records. Tracks still open at a loop boundary, session end, supersession or loss of recent messages are uncertain. Uncertain records contribute to the uncertainty count. A loop boundary resolves the visit at its last observed position in the old loop. Reappearance within valid grace observations in the same loop resumes a tracked visit.

Occupancy and staff durations end at the last known interval within each loop. Counter baselines carry across loops in the same session: cumulative increases count once in the receiving loop, with a gap flag because their exact source position is unknown. A decrease still uses the normal reset rule. Repeated source positions are reported separately for each loop.

`source_time_ms` is source-video position. A null position is valid and remains available in live monitoring and persisted history. Session reports count these observations separately because their position is unknown. The payload `timestamp` is normalized to UTC, and `ReceivedAtUtc` measures delivery freshness. Live event-time reports accept the source token `live` and reject future timestamps beyond the configured tolerance. Other source tokens can be persisted and monitored, and their reporting time basis requires an agreed contract. Archived or out-of-order snapshots are retained as historical evidence and excluded from ordered KPIs. Reports expose sample counts, coverage, reset and gap counts, and quality where applicable.
