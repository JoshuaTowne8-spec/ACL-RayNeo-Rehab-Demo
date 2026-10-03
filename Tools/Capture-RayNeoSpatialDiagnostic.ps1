param(
    [string]$TestLabel = "SpatialDiagnostic",
    [switch]$Install,
    [string]$AdbPath = "C:\Program Files\Unity\Hub\Editor\2022.3.36f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$apkPath = Join-Path $projectRoot "Builds\RayNeoDiagnostics\RayNeo_SpatialPose_Diagnostic.apk"
$packageName = "com.aclrehab.rayneo.spatialdiagnostic"
$activityName = "com.rayneo.openxradapter.UnityOpenXrActivity"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$safeLabel = $TestLabel -replace '[^A-Za-z0-9_-]', '_'
$captureRoot = Join-Path $projectRoot "Logs\RayNeoSpatialDiagnostics\${timestamp}_${safeLabel}"

if (-not (Test-Path -LiteralPath $AdbPath)) {
    throw "ADB was not found: $AdbPath"
}

if ($Install -and -not (Test-Path -LiteralPath $apkPath)) {
    throw "Diagnostic APK was not found: $apkPath"
}

New-Item -ItemType Directory -Force -Path $captureRoot | Out-Null

& $AdbPath start-server | Out-Host
$deviceState = (& $AdbPath get-state 2>&1 | Out-String).Trim()
if ($deviceState -ne "device") {
    throw "RayNeo device is not ready. adb get-state returned: $deviceState"
}

if ($Install) {
    Write-Host "Installing diagnostic APK..."
    & $AdbPath install -r $apkPath | Out-Host
}

& $AdbPath shell pm grant $packageName android.permission.CAMERA 2>$null
& $AdbPath logcat -c
& $AdbPath shell am force-stop $packageName
& $AdbPath shell am start -n "$packageName/$activityName" | Out-Host

Write-Host ""
Write-Host "Diagnostic capture started: $TestLabel"
Write-Host "Perform exactly one test procedure now."
Write-Host "When the procedure is complete, return here and press Enter."
[void](Read-Host)

& $AdbPath logcat -d -v threadtime |
    Set-Content -LiteralPath (Join-Path $captureRoot "logcat.txt") -Encoding UTF8
& $AdbPath logcat -b crash -d -v threadtime |
    Set-Content -LiteralPath (Join-Path $captureRoot "crash.txt") -Encoding UTF8
& $AdbPath shell dumpsys package $packageName |
    Set-Content -LiteralPath (Join-Path $captureRoot "package.txt") -Encoding UTF8
& $AdbPath shell getprop |
    Set-Content -LiteralPath (Join-Path $captureRoot "device-properties.txt") -Encoding UTF8

$csvFolder = Join-Path $captureRoot "device-files"
New-Item -ItemType Directory -Force -Path $csvFolder | Out-Null
$remoteFolder = "/sdcard/Android/data/$packageName/files"
& $AdbPath pull $remoteFolder $csvFolder | Out-Host

& $AdbPath shell am force-stop $packageName
Write-Host "Capture complete: $captureRoot"
Write-Host "Use Analyze-RayNeoSpatialDiagnostic.ps1 on the newest CSV in device-files."

