# Configuration

Use appsettings, environment overrides or .NET User Secrets. Sections are bound to validated options. Restart the API after changing startup settings.

| Section | Settings |
| --- | --- |
| ConnectionStrings | `DefaultConnection` is required for SQL Server. |
| Security | `Authority` and `Audience` configure JWT validation. `Enabled` defaults to true outside Development and Testing. |
| Database | `ApplyMigrationsOnStartup`, `CommandTimeoutSeconds`, `EnableSensitiveDataLogging`. |
| Cors | `Enabled`, `AllowedOrigins` and `AllowCredentials`. The Angular development origin defaults to `http://localhost:4200`. |
| Mqtt | `Enabled`, `Host`, `Port`, `ProtocolVersion`, `TopicFilter`, `ClientId`, `Qos`, `PersistentSession`, `SessionExpirySeconds`, `Username`, `Password`, `UseTls`, `KeepAliveSeconds`, `ConnectTimeoutSeconds`, `ReconnectDelaySeconds`, `MaxPayloadBytes`. |
| MQTT shape limits | `Mqtt:MaxSourceNameLength`, `Mqtt:MaxZonesPerMessage`, `Mqtt:MaxActiveVisitorsPerZone`. |
| SignalR | `Enabled`, `HubPath`, `KeepAliveIntervalSeconds`, `ClientTimeoutSeconds`, `Transport` (All, WebSockets, ServerSentEvents or LongPolling). |
| Analytics | `ExpectedMessageIntervalSeconds`, `StaleAfterSeconds`, `NoRecentDataAfterSeconds`, `TrackMissingGraceSeconds`, `MaximumSnapshotGapSeconds`, `DefaultWarningThresholdPercent`, `DefaultCriticalThresholdPercent`, `DefaultAggregationInterval`, `MaximumReportRangeDays`, `MaximumMinuteReportRangeDays`, `MinimumCoveragePercentForPeakRanking`, `ClockSkewToleranceSeconds`. |
| Aggregation | `Enabled`, `IntervalSeconds`, `BatchSize`. |
| MinuteAggregation | `Enabled`, `IntervalSeconds`, `BatchSize`, `FinalizationDelaySeconds`, `BackfillDays`. |
| Visualization | `MaxSeries`, `MaxPointsPerSeries`, `DefaultRefreshSeconds`, `MinimumRateCoveragePercent`. |
| Congestion | `MinObservedDurationSeconds`, `RecoveryObservedSeconds`, `MaxObservationGapSeconds`. |
| DwellAnalytics | `HistogramUpperBoundsSeconds`, `DefaultLongStaySeconds`. |
| FloorPlans | `MaxPolygonVertices`, `MaxLayoutsPerFloor`. |
| Alerts | `Enabled`, `EvaluateEverySeconds`, `MinimumCoveragePercent`, `DefaultCooldownSeconds`, `MaxRules`. |
| Forecast | `Enabled`, `MinHistoryDays`, `PreferredLookbackDays`, `MinComparableSlots`, `MinimumCoveragePercent`, `HorizonHours`. |
| Retention | `Enabled` (false by default), `RawSnapshotsDays`, `ProcessedMessagesDays`, `HourlyAggregatesDays`, `MinuteAggregatesDays`, `AlertHistoryDays`. |
| OpenApi / HealthChecks | `Enabled`. OpenApi also has `EnableSwaggerUi`. |
| Logging / ASP.NET Core | Set log levels with `Logging:LogLevel`. Configure bindings with launch profiles or `ASPNETCORE_URLS`. |

`/api/v1/health/live` indicates process availability. Readiness returns 503 when SQL is unreachable or unapplied migrations are present. Disconnected MQTT yields HTTP 200 with status `Degraded` so historical reporting stays available. Disabled dependencies have explicit status.

Optional retention prunes old closed-session live zone snapshots after hourly processing. Staff-source snapshots are retained through the aggregate horizon. Video sessions, visit facts, counter deltas and compact message identities are preserved. `ProcessedMessagesDays` controls rejection metadata retention. Older partial-hour and minute reports can show reduced occupancy coverage after raw retention.

When minute aggregation is active, zone pruning waits for relevant pending repairs and the initial backfill. `AlertHistoryDays` also governs closed congestion episodes. Detected insights are generated on demand and have no retention job. See [Aggregation and retention](aggregation-and-retention.md) for cursor behavior and evidence limits.

## PowerShell

```powershell
$env:Mqtt__Host = 'broker.internal'
$env:Mqtt__Port = '8883'
$env:Mqtt__UseTls = 'true'
$env:Mqtt__Username = 'analytics-reader'
$env:Cors__AllowedOrigins__0 = 'https://analytics.internal'
dotnet user-secrets set 'Mqtt:Password' '<your broker password>' --project src/CameraAnalytics.Api
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Server=.\SQLEXPRESS;Database=CameraAnalytics;Integrated Security=True;TrustServerCertificate=True' --project src/CameraAnalytics.Api
```

User Secrets are read in Development and stored outside the workspace. For service hosting supply credentials through the service environment. ASP.NET Core's double-underscore environment naming maps to nested settings. The frontend runtime configuration is a public JSON file and contains URLs, the hub switch and its refresh interval. Keep it free of credentials.

See [Security and deployment](security-and-deployment.md) for credential rotation, site roles and service hosting.
