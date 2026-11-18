param(
    [Parameter(Mandatory)] [string] $BrokerHost,
    [ValidateRange(1, 65535)] [int] $BrokerPort = 1883,
    [ValidateRange(2, 100)] [int] $Cameras = 2,
    [ValidateRange(2, 20)] [int] $Zones = 3,
    [ValidateRange(2, 300)] [int] $Seconds = 12
)

$ErrorActionPreference = 'Stop'
if (-not $env:CAMERA_ANALYTICS_TEST_SQL) {
    throw 'Set CAMERA_ANALYTICS_TEST_SQL to a test SQL instance with isolated database create/drop rights.'
}
$settings = @{
    CAMERA_ANALYTICS_RUN_LOAD = 'true'
    CAMERA_ANALYTICS_TEST_MQTT_HOST = $BrokerHost
    CAMERA_ANALYTICS_TEST_MQTT_PORT = "$BrokerPort"
    CAMERA_ANALYTICS_LOAD_CAMERAS = "$Cameras"
    CAMERA_ANALYTICS_LOAD_ZONES = "$Zones"
    CAMERA_ANALYTICS_LOAD_SECONDS = "$Seconds"
}
$previous = @{}
try {
    foreach ($entry in $settings.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    Write-Host 'Publishing synthetic test-only telemetry. Use an isolated broker with no production subscribers.'
    $project = Join-Path $PSScriptRoot '..\tests\CameraAnalytics.IntegrationTests'
    dotnet test $project --filter 'Category=Load' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw "The MQTT workload exited with code $LASTEXITCODE." }
}
finally {
    foreach ($entry in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
