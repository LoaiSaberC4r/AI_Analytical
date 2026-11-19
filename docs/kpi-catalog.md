# Smart analytics KPI catalog

Reports use half-open windows. Live reports use source timestamps on a UTC axis with site-local labels. Video reports use source milliseconds within a single explicit session and loop. Received UTC timestamps measure delivery freshness independently. Ordered snapshots and counter changes supply the metrics. An observation contributes duration through the next ordered sample, clipped to the selected period and capped by the configured gap policy.

| Metric | Scope and units | Formula and evidence |
| --- | --- | --- |
| Flow counts | Zone, events | Sum the positive observed changes in arrivals, departures, entry, exit, wrong-way in and wrong-way out at each ending observation. A decrease contributes zero for that counter and records a reset. An initial sample records a baseline. |
| Flow rates | Zone, events/min | Observed count divided by the requested bucket duration in minutes. Require the configured coverage, a usable counter observation, and zero gap, reset and baseline indicators. |
| Traffic balance | Zone, events | Observed entries minus observed exits. Negative values describe movement balance. |
| Occupancy and staff | Zone people and camera staff | Weighted sum divided by represented seconds. Peak is the maximum accepted sample inside the requested range. A single sample can contribute a peak with no represented duration. |
| Utilization | Zone, percent | Occupancy divided by the positive capacity captured on the same snapshot, times 100. Weight by represented capacity-known seconds. Qualifying values can exceed 100%. Older snapshots with uncertain capacity contribute no utilization. |
| Congestion | Zone, observed seconds | Clip represented time at or above the snapshotted warning and critical thresholds. Accumulate warning duration to qualify an episode. Measured recovery resolves it. Capacity or session changes end it. A qualifying gap makes it uncertain. |
| Active dwell and long stay | Zone, tracked visits and seconds | Use currently active visits in a fresh registered zone in the selected current source session and loop. Count those with dwell at or above the configured seconds and return their maximum recorded dwell. |
| Estimated completed dwell | Zone, visits and seconds | Count each estimated-completed fact in the window once. Total and mean use last observed dwell. Count uncertain resolved visits separately. The histogram uses half-open bins with an unbounded final bin. |
| FPS | Camera, frames/second | Minimum, maximum and arithmetic mean across accepted samples. These statistics use sample weighting. |
| Sequence coverage | Camera, missing sequence numbers | Sum positive differences between consecutive ordered sequence numbers minus one within a session. Assign the gap to the ending observation. Rows preceding the new field have unknown sequence coverage. |
| Source-to-receipt lag | Camera, milliseconds | Arithmetic mean of nonnegative received UTC minus source UTC readings on live samples. Video positions and future-skewed timestamps do not contribute. The response includes the qualifying sample count. |
| Rejections | Site/camera, deliveries | Group durable rejection records by bounded reason using the registered camera topics and received UTC window. Unknown topics stay outside scoped reports. Byte-identical duplicates leave no new receipt, so a historical duplicate count cannot be derived from these rows. |

Each series preserves scope, duration, sample count, gap/reset/baseline metadata, warnings, and provenance. A null metric has insufficient evidence. A zero is a recorded value or an observed change. Coverage is represented duration divided by requested duration. Per-zone observations and event counts may overlap across zones and cameras.
