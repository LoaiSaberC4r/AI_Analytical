# Baseline verification

Starting branch `master`, commit `184416bfb89c4e173aa729a67fa8fef782362ef0`, clean working tree. Development ran on Windows with .NET SDK 10.0.401, npm 11.9.0, Node 22.22.3 for Angular, SQL Server Express 16.0.1000.6 with integrated authentication, and local Mosquitto on port 1883.

| Command | Result |
| --- | --- |
| `dotnet tool restore` and `dotnet restore` | Passed |
| `dotnet build --no-restore` | Passed with zero warnings or errors |
| `dotnet test --no-build` with SQL and MQTT test settings from the README | 58 unit and 12 integration tests passed, zero skips |
| `npm ci --no-audit --no-fund --prefer-offline` via the README's Node 22.22.3 wrapper | Passed |
| `npm run build` and `npm test` via that wrapper | Build passed, 6 tests passed across 3 files |

An early frontend build attempt ran before dependencies were present and failed to locate the Angular CLI. A clean install and both commands succeeded on the supported Node version.

The final backend run passed restore, build and `migrations has-pending-model-changes`. The regression suite passed 77 unit and 25 integration tests with zero skips, using SQL Server Express over its direct named pipe and a real local Mosquitto broker. The two opt-in load runs passed separately. Angular is outside the final scope.

## Reproduce the workload

Use a disposable database instance and an isolated broker. The workload registers synthetic cameras and zones over HTTP and publishes schema 2.0 JSON with QoS 1. It starts one stream per camera per second for 12 frames, records SQL and ACK completion, sends one duplicate and one malformed payload per run, and waits for the minute queue to drain. The integration test compares the persisted counter sum, minute aggregates and traffic API. A separate late-minute test reconciles a clean hour with its minute rows and source changes.

```powershell
$env:CAMERA_ANALYTICS_TEST_SQL = 'Server=.\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10'
.\samples\measure-mqtt.ps1 -BrokerHost 127.0.0.1 -Cameras 2 -Zones 3 -Seconds 12
.\samples\measure-mqtt.ps1 -BrokerHost 127.0.0.1 -Cameras 50 -Zones 3 -Seconds 12
```

The sample script emits a `LOAD_RESULT` JSON record. A test using the local SQL named pipe can set `Server=np:\\.\pipe\MSSQL$SQLEXPRESS\sql\query` if the default instance resolver is unavailable. The regression run uses `dotnet test --no-build --filter 'Category!=Load'`. The script runs the load case separately.

## Measured results

Runs used Windows, an Intel Xeon E5-2630 v3 with 16 logical processors and approximately 16 GiB RAM, local SQL Server Express 16.0.1000.6, and local Mosquitto on port 1883. The MQTT subscriber and its publisher both ran on the same machine. The post-change runs used the SQL named pipe. Measurements use nearest-rank percentiles. The short runs include cold operations and convey no production capacity guarantee.

| Configuration | Cameras × zones | Accepted | Messages/s through ACK drain | Source-to-commit p50 / p95 / p99 (ms) | Receipt-to-ACK p50 / p95 / p99 (ms) |
| --- | ---: | ---: | ---: | --- | --- |
| Before batching | 2 × 3 | 24 / 24 | 2.16 | 72.8 / 1,111.9 / 1,477.8 | 44.6 / 352.7 / 1,082.7 |
| Before batching | 50 × 3 | 600 / 600 | 29.62 | 8,389.7 / 9,175.5 / 9,269.5 | 25.4 / 68.8 / 151.0 |
| Batched keys | 2 × 3 | 24 / 24 | 2.16 | 62.3 / 1,198.4 / 1,271.7 | 33.0 / 191.8 / 1,172.3 |
| Batched keys | 50 × 3 | 600 / 600 | 32.03 | 5,529.7 / 7,282.7 / 7,584.4 | 27.2 / 40.5 / 129.7 |

| Batched keys | Receipt-to-commit p50 / p95 / p99 (ms) | Minute worker lag p50 / p95 / p99 (s) | Peak sampled dirty keys | Flow API p50 / p95 / p99 (ms) | Summary signals |
| --- | --- | --- | ---: | --- | ---: |
| 2 × 3 | 32.4 / 189.9 / 1,151.4 | 5.35 / 5.35 / 5.35 | 8 | 47.3 / 413.4 / 413.4 | 24 |
| 50 × 3 | 26.7 / 40.3 / 129.5 | 5.91 / 7.37 / 7.37 | 200 | 72.8 / 429.1 / 429.1 | 600 |

The synthetic source timestamp was generated before publication. Receipt-to-ACK starts when the API subscriber callback receives the message and includes its acknowledgment call. Minute lag measures elapsed time from the minute boundary to the worker pass. Flow API timing includes four calls per run. SQL CPU, I/O and SignalR delivery to external clients were unmeasured. The server emitted the summary signal totals shown above.

Both batched runs persisted a single rejection and observed one duplicate delivery. The source, minute and API arrival sums reconciled to 66 and 1,650 respectively. The 50-camera workload accumulated backlog and took 18.73 seconds through ACK drain. Batched repair keys reduced round trips without changing subscriber order or the SQL writer lock. This host did not sustain 50 messages per second. The before and after runs used different SQL connection routes, and the observed improvement combines the changes in code and connection setup.
