# Alerts and insights

An alert rule targets one registered camera or zone at its site. The rule stores its thresholds, evidence window, observed duration, recovery, coverage gate, cooldown and row version. Create a rule to start evaluation. Five kinds are available.

| Kind | Scope | Evidence |
| --- | --- | --- |
| `Congestion` | Zone | Capacity-at-observation utilization at or above the threshold for sustained represented time |
| `LongStay` | Zone | Longest observed active dwell at or above the threshold for sustained represented time, with a current qualifying visit count |
| `WrongWaySpike` | Zone | Sum of directional deltas in a qualifying trailing window |
| `CameraNoRecentData` | Camera | Seconds since last receipt after entering NoRecentData. The freshness transition and configured threshold must both hold. |
| `LowFps` | Camera | FPS below the threshold over sustained represented intervals |

Video evaluation is off per rule by default. Explicitly enabled rules evaluate one current session and loop. Delivery outages only apply to running live cameras. Stale, insufficient and missing samples do not advance measured duration. Terminal conditions such as a session change or disabled scope close alerts with a reason.

The evaluator and actions take the same SQL writer lock. A filtered unique index enforces one unresolved alert per rule. SQL persists state, evidence, cooldown, acknowledgment and resolution. Acknowledgment is idempotent and the condition remains under evaluation. Manual resolution is restricted when automatic evaluation is enabled for the rule. Alerts resolve on measured recovery, a terminal scope condition, or rule disable/change. `AnalyticsAlertChanged` carries the ID, site, camera, status and version after commit. Reconnect clients refresh from REST.

Insights are computed from report evidence without an external model. The engine emits qualified arrivals, weighted occupancy and congestion cards, plus observed active long-stay counts. IDs derive deterministically from the metric, scope and observation window. Names are plain text and cards contain numeric evidence, units and quality.

Capacity-based congestion also maintains SQL episode progress independently of configured alert rules. `GET /api/v1/analytics/congestion/history` pages closed measured episodes and retains severity, configuration, observed duration and closure reason. The ordinary congestion report calculates interval-clipped evidence from observations. Episode coverage describes time measured above the warning threshold within the persisted episode span.
