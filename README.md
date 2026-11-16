# Camera Analytics

An on-premises dashboard for AI camera metadata. Register sites, cameras and zones, ingest schema 2.0 over MQTT, monitor persisted readings through SignalR, and report on occupancy, traffic, wrong-way movement, staff and estimated dwell. Video-file reports use source positions within each processing session.

## Prerequisites

- .NET 10 SDK with the latest servicing update, SQL Server 2022 or later (Express works), and a SQL login or integrated account with access to the analytics database.
- Node.js 22.22.3 or later in the 22.x line, 24.15.0 or later in the 24.x line, or 26.x. The frontend pins Angular 22 and TypeScript 6.0. [Angular compatibility](https://angular.dev/reference/versions)
- Eclipse Mosquitto and `mosquitto_pub` on PATH for demo publishing. The broker hostname and port are configurable.

## Start the backend

From the solution directory in Windows PowerShell:

```powershell
dotnet tool restore
$env:ConnectionStrings__DefaultConnection = 'Server=.\SQLEXPRESS;Database=CameraAnalytics;Integrated Security=True;TrustServerCertificate=True'
$env:Mqtt__Host = '127.0.0.1'   # Use this override for a broker on the same machine
dotnet run --project src/CameraAnalytics.Api
```

Open [Swagger UI](http://localhost:5080/swagger/index.html) or [readiness](http://localhost:5080/api/v1/health/ready). The launch profile binds the API to localhost. The default broker in appsettings is `192.168.1.8:1883`. Set `Mqtt__Enabled=false` to run REST with ingestion disabled.

Startup applies the checked-in migrations when `Database__ApplyMigrationsOnStartup=true`. For a database account without schema-change rights, apply the migrations separately using an account with those rights:

```powershell
dotnet tool run dotnet-ef database update --project src/CameraAnalytics.Infrastructure --startup-project src/CameraAnalytics.Api
$env:Database__ApplyMigrationsOnStartup = 'false'
```

Use environment variables or [User Secrets](docs/configuration.md) for credentials. The example trusts the SQL Server certificate for a local workstation. Use a valid, trusted certificate for a networked SQL Server.

## Start Angular

From a second PowerShell window:

```powershell
Set-Location frontend/camera-analytics-web
npm ci
npm start
```

On a Windows workstation with an older Node installation, these commands use Node 22.22.3 for the frontend without changing the global installation:

```powershell
$npmCli = Join-Path (Split-Path (Get-Command npm.cmd).Source) 'node_modules/npm/bin/npm-cli.js'
npm.cmd exec --yes --package=node@22.22.3 -- node $npmCli ci
npm.cmd exec --yes --package=node@22.22.3 -- node ./node_modules/@angular/cli/bin/ng.js serve --host localhost
```

Open [Camera Analytics](http://localhost:4200). Update `frontend/camera-analytics-web/public/config.json` for another API URL or hub path. With a different frontend origin, update the API's allowed-origin list. For a distributable build run `npm run build`, then serve `dist/camera-analytics-web/browser` with SPA fallback to `index.html`.

## Run the demo

1. In **Sites**, add **Main Mall** with code `MALL-001` and time zone `Africa/Cairo`.
2. In **Cameras & zones**, add **Main Entrance Camera** with external ID `1` at **Main Mall**. The API derives `cameras/1/analytics`.
3. Open the camera's **Zones** link. Register IDs `zone_01`, `zone_02`, `zone_03`. Add display names and optional capacities.
4. Start Mosquitto or use the LAN broker. For a local broker process, `mosquitto -p 1883` starts a loopback listener. Run the following commands from the solution directory, one at a time.

```powershell
.\samples\publish-mqtt.ps1 -BrokerHost 127.0.0.1 -Messages 20
.\samples\publish-mqtt.ps1 -BrokerHost 127.0.0.1 -SourceType live -Messages 20
.\samples\publish-mqtt.ps1 -BrokerHost 127.0.0.1 -Messages 20 -ExerciseEdgeCases
```

**Live monitoring** shows camera and zone updates during publishing. **Sessions** opens the video report for a publishing run. **Analytics** shows live-source reports and quality metadata. Set the report end time after the publish when using a previously opened form. Peak-hour eligibility depends on configured coverage and the requested time range.

The edge-case run publishes duplicate and late sequences, malformed JSON, unregistered IDs, a reset, a new session and messages after session end. See [MQTT integration](docs/mqtt-integration.md) for broker configuration, acceptance checks and failure recovery.

## Tests

```powershell
dotnet build
dotnet test
$env:CAMERA_ANALYTICS_TEST_SQL = 'Server=.\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5'
$env:CAMERA_ANALYTICS_TEST_MQTT_HOST = '127.0.0.1'
dotnet test
Set-Location frontend/camera-analytics-web
npm test
```

SQL tests create and remove uniquely named databases on the configured instance. The broker test uses an isolated topic and client ID on a test broker. Set `CAMERA_ANALYTICS_TEST_MQTT_PORT` if the test broker uses a different port. Tests skip the external suites when their dependencies are unconfigured.

The demo was verified with SQL Server Express, Mosquitto at `192.168.1.8:1883`, and the PowerShell publisher in Chrome on desktop and mobile viewports. The external AI publisher and the authenticated TLS listener remain unverified.

## Engineering notes

- [Architecture and persistence](docs/architecture.md)
- [REST and SignalR contracts](docs/api-contract.md)
- [Analytics formulas, time bases and data quality](docs/analytics-definitions.md)
- [MQTT delivery, TLS and diagnostics](docs/mqtt-integration.md)
- [Configuration and secrets](docs/configuration.md)

The LAN demo has no user authentication. Bind server ports to trusted interfaces and use a private broker or authenticated broker ACLs. All demo data come from registered MQTT sources and SQL Server.
