[CmdletBinding()]
param(
    [string]$BrokerHost,
    [ValidateRange(1, 65535)][int]$Port,
    [string]$CameraId = '1',
    [string[]]$ZoneIds = @('zone_01', 'zone_02', 'zone_03'),
    [ValidateSet('video_file', 'live')][string]$SourceType = 'video_file',
    [ValidateRange(2, 36000)][int]$Messages = 18,
    [ValidateRange(100, 60000)][int]$IntervalMilliseconds = 1000,
    [string]$SessionId = [guid]::NewGuid().ToString('N'),
    [switch]$ExerciseEdgeCases
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command mosquitto_pub -ErrorAction SilentlyContinue)) {
    throw 'Install Mosquitto and add mosquitto_pub to PATH.'
}
$settingsPath = Join-Path $PSScriptRoot '../src/CameraAnalytics.Api/appsettings.json'
$settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
if (-not $BrokerHost) { $BrokerHost = $settings.Mqtt.Host }
if (-not $Port) { $Port = $settings.Mqtt.Port }
$topic = "cameras/$CameraId/analytics"
function Publish([string]$targetTopic, [string]$json) {
    $json | & mosquitto_pub -h $BrokerHost -p $Port -t $targetTopic -q 1 -s
    if ($LASTEXITCODE -ne 0) { throw "mosquitto_pub exited with code $LASTEXITCODE." }
}
function New-Message([int]$sequence, [string]$status, [string]$session) {
    $zones = for ($index = 0; $index -lt $ZoneIds.Count; $index++) {
        $step = $sequence + $index
        $cycle = $step % 16
        $trackVisible = $cycle -lt 6 -and $status -eq 'running'
        $visitors = if ($trackVisible) { @{ track_id = [long]($index + 1); dwell_seconds = $cycle + 1.0 } }
        @{
            zone_id = $ZoneIds[$index]
            occupancy = if ($trackVisible) { 2 + $index } else { 0 }
            zone_arrivals = [long]([Math]::Floor($step / 4) + 1)
            zone_departures = [long]([Math]::Floor($step / 5))
            entry = [long]([Math]::Floor($step / 4))
            exit = [long]([Math]::Floor($step / 6))
            wrong_way_in = [long]([Math]::Floor($step / 9))
            wrong_way_out = [long]([Math]::Floor($step / 14))
            average_dwell_seconds = [Math]::Round([Math]::Min($step, 50) / 2.0, 1)
            active_visitors = @($visitors | Where-Object { $null -ne $_ })
        }
    }
    @{
        schema_version = '2.0'
        camera_id = $CameraId
        source_type = $SourceType
        source_name = if ($SourceType -eq 'video_file') { 'demo.mp4' } else { "live-$CameraId" }
        session_id = $session
        source_time_ms = if ($SourceType -eq 'video_file') { [long]($sequence - 1) * $IntervalMilliseconds } else { $null }
        processing_fps = 8 + ($sequence % 3)
        zones = @($zones)
        staff_count = $sequence % 4
        status = $status
        sequence = $sequence
        timestamp = [DateTimeOffset]::UtcNow.ToString('o')
    }
}
Write-Host "Publishing $Messages schema v2.0 messages to ${BrokerHost}:${Port} ($topic), session $SessionId"
for ($sequence = 1; $sequence -le $Messages; $sequence++) {
    $status = if ($sequence -eq $Messages) { 'ended' } else { 'running' }
    $message = New-Message $sequence $status $SessionId
    if ($ExerciseEdgeCases -and $sequence -eq 5 -and $message.zones.Count -gt 0) { $message.zones[0].zone_arrivals = 0 }
    $json = $message | ConvertTo-Json -Compress -Depth 8
    Publish $topic $json
    if ($ExerciseEdgeCases -and $sequence -eq 2) {
        Publish $topic $json
        $old = New-Message 0 'running' $SessionId
        if ($SourceType -eq 'video_file') { $old.source_time_ms = 0 }
        Publish $topic ($old | ConvertTo-Json -Compress -Depth 8)
    }
    if ($sequence -lt $Messages) { Start-Sleep -Milliseconds $IntervalMilliseconds }
}
if ($ExerciseEdgeCases) {
    Publish $topic '{malformed'
    $unknownCamera = New-Message 1 'running' ([guid]::NewGuid().ToString('N'))
    $unknownCamera.camera_id = "unknown-$CameraId"
    Publish "cameras/$($unknownCamera.camera_id)/analytics" ($unknownCamera | ConvertTo-Json -Compress -Depth 8)
    $unknownZone = New-Message 1 'running' ([guid]::NewGuid().ToString('N'))
    if ($unknownZone.zones.Count -gt 0) {
        $unknownZone.zones[0].zone_id = 'unregistered-demo-zone'
        Publish $topic ($unknownZone | ConvertTo-Json -Compress -Depth 8)
    }
    $newSession = [guid]::NewGuid().ToString('N')
    Publish $topic ((New-Message 1 'running' $newSession) | ConvertTo-Json -Compress -Depth 8)
    Publish $topic ((New-Message 2 'ended' $newSession) | ConvertTo-Json -Compress -Depth 8)
    Publish $topic ((New-Message 3 'running' $SessionId) | ConvertTo-Json -Compress -Depth 8)
}
