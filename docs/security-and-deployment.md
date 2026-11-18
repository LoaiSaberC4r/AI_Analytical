# Security and deployment

## Rotate the exposed database credential

An earlier public revision of `src/CameraAnalytics.Api/appsettings.json` contained a SQL password. Revoke or rotate it immediately on the SQL instance, rotate it anywhere it was reused, update authorized deployments, and review SQL sign-in logs for unexpected use. Earlier commits, clones, forks and caches can still contain the credential. Coordinate any separately authorized history cleanup with repository administrators after rotation.

Configure the API with `ConnectionStrings__DefaultConnection` in the service environment, or set `ConnectionStrings:DefaultConnection` with .NET User Secrets on a Development workstation. The README contains an integrated-authentication example. Keep sensitive EF logging disabled. Use a certificate trusted by the API host for networked SQL Server. Give the runtime login only the table access and stored procedure permissions it needs, including the ability to acquire the application's SQL transaction locks. Run migrations separately with a deployment principal and set `Database__ApplyMigrationsOnStartup=false` for the runtime.

## API and SignalR

The launch profile binds an unauthenticated Development demo to localhost. Testing can also disable authentication. In other environments authentication is required and explicitly disabling it fails startup. Configure `Security__Authority` with the HTTPS OpenID Connect authority and `Security__Audience` with the API audience. Configure the issuer to place roles in `role` and include one `site_id` claim per authorized site, with its registry GUID.

- `AnalyticsAdministrator` can use the existing cross-site catalog, reporting and monitoring operations. The administrator role can access any site subscription.
- `AnalyticsReader` can subscribe to sites and their cameras only when the token lists the site. `AnalyticsOperator` has the same subscription scope and is eligible for site mutations wherever an endpoint permits it.
- Liveness and readiness remain available to network-restricted probes. Restrict them and Swagger at the reverse proxy if the deployment exposes the API externally. Disable OpenAPI with `OpenApi__Enabled=false` when it is unnecessary.

Send a bearer token with API requests and use SignalR's access-token factory. Browser WebSockets and Server-Sent Events may send the token as `access_token` on the configured hub route. Keep query-string logging disabled at IIS, the Linux reverse proxy and any upstream access logs for that route. Use HTTPS, including when a proxy terminates TLS. The bundled Angular demo has no identity-provider sign-in flow. Its public configuration file holds endpoints and contains no tokens or credentials.

Run the API as a dedicated service account on Windows, IIS, or systemd on Linux. Configure explicit allowed origins when the frontend has a separate origin. Authorization uses roles and scope claims. For production MQTT use a trusted broker certificate and configure TLS, broker authentication, and ACLs limiting the ingestion identity to subscriptions and producer identities to their assigned camera topics. Keep a stable, deployment-specific subscriber Client ID.
