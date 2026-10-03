param(
    [Parameter(Mandatory = $true)]
    [string]$CsvPath,

    [ValidateSet("Stationary", "Yaw", "Walk1m", "Plane", "Relocalization")]
    [string]$Mode = "Stationary"
)

$ErrorActionPreference = "Stop"
$culture = [System.Globalization.CultureInfo]::InvariantCulture

function Number([object]$value) {
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) {
        return 0.0
    }
    return [double]::Parse([string]$value, $culture)
}

function Distance3($a, $b, [string]$prefix) {
    $dx = (Number $a."${prefix}_px") - (Number $b."${prefix}_px")
    $dy = (Number $a."${prefix}_py") - (Number $b."${prefix}_py")
    $dz = (Number $a."${prefix}_pz") - (Number $b."${prefix}_pz")
    return [Math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
}

function QuaternionAngle($a, $b, [string]$prefix) {
    $ax = Number $a."${prefix}_qx"
    $ay = Number $a."${prefix}_qy"
    $az = Number $a."${prefix}_qz"
    $aw = Number $a."${prefix}_qw"
    $bx = Number $b."${prefix}_qx"
    $by = Number $b."${prefix}_qy"
    $bz = Number $b."${prefix}_qz"
    $bw = Number $b."${prefix}_qw"
    $aLength = [Math]::Sqrt($ax * $ax + $ay * $ay + $az * $az + $aw * $aw)
    $bLength = [Math]::Sqrt($bx * $bx + $by * $by + $bz * $bz + $bw * $bw)
    if ($aLength -le 0.000001 -or $bLength -le 0.000001) {
        return 0.0
    }
    $dot = ($ax * $bx + $ay * $by + $az * $bz + $aw * $bw) /
        ($aLength * $bLength)
    $dot = [Math]::Min(1.0, [Math]::Abs($dot))
    return 2.0 * [Math]::Acos($dot) * 180.0 / [Math]::PI
}

if (-not (Test-Path -LiteralPath $CsvPath)) {
    throw "CSV was not found: $CsvPath"
}

$rows = @(Import-Csv -LiteralPath $CsvPath)
if ($rows.Count -lt 2) {
    throw "CSV does not contain enough diagnostic rows."
}

$frames = @($rows | Group-Object frame | ForEach-Object { $_.Group[0] } |
    Sort-Object { Number $_.realtime_s })
$trackingFrames = @($frames | Where-Object {
    $_.slam -eq "FFVINS_TRACKING_SUCCESS"
})
$measurementFrames = if ($trackingFrames.Count -ge 2) {
    $trackingFrames
} else { $frames }
$planeRows = @($rows | Where-Object { [int]$_.plane_index -ge 0 })
$hasNativeColumns = $frames[0].PSObject.Properties.Name -contains "native_head_valid"
$nativeFrames = if ($hasNativeColumns) {
    @($frames | Where-Object { [int]$_.native_head_valid -eq 1 })
} else { @() }
$first = $measurementFrames[0]
$last = $measurementFrames[-1]
$duration = (Number $last.realtime_s) - (Number $first.realtime_s)
$headEndpoint = Distance3 $first $last "head_world"
$headReturnAngle = QuaternionAngle $first $last "head_world"
$maxHeadStep = ($measurementFrames | ForEach-Object { Number $_.head_world_step } |
    Measure-Object -Maximum).Maximum
$maxSdkStep = ($measurementFrames | ForEach-Object { Number $_.sdk_head_step } |
    Measure-Object -Maximum).Maximum
$maxPlaneStep = if ($planeRows.Count -gt 0) {
    ($planeRows | ForEach-Object { Number $_.raw_plane_step_m } |
        Measure-Object -Maximum).Maximum
} else { 0.0 }
$maxPlaneAngle = if ($planeRows.Count -gt 0) {
    ($planeRows | ForEach-Object { Number $_.raw_plane_angle_step_deg } |
        Measure-Object -Maximum).Maximum
} else { 0.0 }
$maxDistanceFromStart = ($measurementFrames | ForEach-Object {
    Distance3 $first $_ "head_world"
} | Measure-Object -Maximum).Maximum
$nativeReturnCodes = if ($nativeFrames.Count -gt 0) {
    ($frames.native_head_result | Sort-Object -Unique) -join ", "
} else { "unsupported/unavailable" }
$nativeEndpoint = 0.0
$nativeMaxDistanceFromStart = 0.0
$maxNativeStep = 0.0
if ($nativeFrames.Count -gt 0) {
    $nativeFirst = $nativeFrames[0]
    $nativeLast = $nativeFrames[-1]
    $nativeEndpoint = Distance3 $nativeFirst $nativeLast "native_head"
    $nativeMaxDistanceFromStart = ($nativeFrames | ForEach-Object {
        Distance3 $nativeFirst $_ "native_head"
    } | Measure-Object -Maximum).Maximum
    $maxNativeStep = ($nativeFrames | ForEach-Object {
        Number $_.native_head_step
    } | Measure-Object -Maximum).Maximum
}
$slamStates = ($frames.slam | Sort-Object -Unique) -join ", "
$eventSummary = $rows | Where-Object { $_.event -ne "SAMPLE" } |
    Group-Object event | Sort-Object Count -Descending
$timestampRegressions = 0
$previousTimestamp = $null
foreach ($row in ($planeRows | Sort-Object { Number $_.realtime_s })) {
    $current = [uint64]$row.plane_timestamp
    if ($null -ne $previousTimestamp -and $current -lt $previousTimestamp) {
        $timestampRegressions++
    }
    $previousTimestamp = $current
}

Write-Host "RayNeo spatial diagnostic summary"
Write-Host "  Mode:                    $Mode"
Write-Host ("  Duration:                {0:F2} s" -f $duration)
Write-Host "  Unique sampled frames:   $($frames.Count)"
Write-Host "  Tracking sample frames:  $($trackingFrames.Count)"
Write-Host "  SLAM states:             $slamStates"
Write-Host ("  Head endpoint distance:  {0:F4} m" -f $headEndpoint)
Write-Host ("  Head endpoint angle:     {0:F2} deg" -f $headReturnAngle)
Write-Host ("  Max head frame step:     {0:F4} m" -f $maxHeadStep)
Write-Host ("  Max SDK callback step:   {0:F4}" -f $maxSdkStep)
Write-Host ("  Max distance from start: {0:F4} m" -f $maxDistanceFromStart)
Write-Host "  Native return codes:     $nativeReturnCodes"
Write-Host "  Native valid frames:     $($nativeFrames.Count)/$($frames.Count)"
Write-Host ("  Native endpoint:         {0:F4} raw units" -f $nativeEndpoint)
Write-Host ("  Native max displacement: {0:F4} raw units" -f $nativeMaxDistanceFromStart)
Write-Host ("  Native max frame step:   {0:F4} raw units" -f $maxNativeStep)
Write-Host ("  Max raw plane step:      {0:F4} m" -f $maxPlaneStep)
Write-Host ("  Max raw plane angle:     {0:F2} deg" -f $maxPlaneAngle)
Write-Host "  Plane timestamp regressions: $timestampRegressions"
Write-Host "  Events:"
foreach ($event in $eventSummary) {
    Write-Host "    $($event.Name): $($event.Count)"
}

Write-Host ""
switch ($Mode) {
    "Stationary" {
        $pass = $trackingFrames.Count -ge 2 -and
                $headEndpoint -le 0.05 -and
                $maxHeadStep -le 0.15 -and
                $timestampRegressions -eq 0
        Write-Host "Stationary heuristic: $(if ($pass) { 'PASS' } else { 'INVESTIGATE' })"
        Write-Host "Target: continuous TRACKING_SUCCESS, endpoint drift <= 0.05 m, no >= 0.15 m jump."
    }
    "Yaw" {
        $pass = $headEndpoint -le 0.10 -and $timestampRegressions -eq 0
        Write-Host "Yaw heuristic: $(if ($pass) { 'PASS' } else { 'INVESTIGATE' })"
        Write-Host "Target: rotate in place and return; position endpoint <= 0.10 m."
    }
    "Walk1m" {
        $pass = $maxDistanceFromStart -ge 0.85 -and $maxDistanceFromStart -le 1.15
        Write-Host "One-metre heuristic: $(if ($pass) { 'PASS' } else { 'SCALE/DIRECTION INVESTIGATION' })"
        Write-Host "Target: maximum world displacement between 0.85 m and 1.15 m."
        if ($nativeFrames.Count -gt 0) {
            Write-Host ("Native interpretation: {0:F4} raw units maximum displacement." -f $nativeMaxDistanceFromStart)
            Write-Host "For a measured 1 m walk, about 1 suggests metres; about 1000 suggests millimetres."
        } else {
            Write-Host "Native interpretation: no valid native head-pose samples were recorded."
        }
    }
    "Plane" {
        Write-Host "Plane test requires the visual frozen/live comparison in addition to CSV."
        Write-Host "Investigate raw plane motion if max step > 0.10 m or max angle > 5 deg while stationary."
    }
    "Relocalization" {
        Write-Host "Expected: at most one recovery transition and one world correction."
        Write-Host "Repeated state transitions, timestamp regressions, or alternating jumps are a failure."
    }
}
