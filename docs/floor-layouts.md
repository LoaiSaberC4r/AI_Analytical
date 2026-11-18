# Floor layouts

A site can have several named floors ordered by `displayOrder`. A registered zone can have one polygon per floor. The floor and camera must belong to the same site. Layouts use JSON arrays of `{x, y}` vertices in normalized `[0,1]` canvas coordinates, left to right and top to bottom. The server checks finite bounds, unique vertices, non-collinear corners, positive area and simple edges. Writes and deletes use the current Base64 `rowVersion`. A create uses no token. HTTP 409 requires reloading the persisted geometry.

The canvas shows only the configured zone outlines. It can filter by camera. An overlap represents separate zone observations. Layouts contain no individual coordinates or tracking paths. This deployment uses an optional-background canvas without asset upload or storage paths.

`GET /api/v1/analytics/heatmap?floorId=<guid>&metricKey=arrivals` defaults to a recently finalized UTC minute. Dirty and absent zone aggregates produce an unavailable cell. Choose `freshOccupancy` or `utilization` for a current live measurement. `averageOccupancy`, `estimatedAverageDwell`, `wrongWay` and `congestion` provide historical overlays. The measured period, unit and quality travel with the returned zones.
