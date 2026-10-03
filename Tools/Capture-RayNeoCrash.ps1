param(
    [string]$TestLabel = "ESP32_Test",
    [string]$TargetPackage = "com.aclrehab.rayneo.demo",
    [string]$TargetActivity = "com.rayneo.openxradapter.UnityOpenXrActivity",
    [ValidateRange(1, 180)]
    [int]$TimeoutMinutes = 60,
    [ValidateRange(1, 10)]
    [int]$PollSeconds = 2
)

$ErrorActionPreference = "Stop"

$adb = "C:\Program Files\Unity\Hub\Editor\2022.3.36f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$packageName = $TargetPackage
$activityName = $TargetActivity
$projectRoot = Split-Path -Parent $PSScriptRoot
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$safeLabel = $TestLabel -replace '[^A-Za-z0-9_-]', '_'
$captureDirectory = Join-Path $projectRoot "Logs\RayNeoCrashCaptures\${timestamp}_${safeLabel}"
$logcatPath = Join-Path $captureDirectory "logcat_threadtime.txt"
$logcatErrorPath = Join-Path $captureDirectory "logcat_stderr.txt"

if (-not (Test-Path -LiteralPath $adb)) {
    throw "ADB was not found at: $adb"
}

New-Item -ItemType Directory -Path $captureDirectory -Force | Out-Null

[string[]]$deviceLines = @(& $adb devices -l) |
    Where-Object { $_ -match '^\S+\s+device(?:\s|$)' }
if ($deviceLines.Count -ne 1) {
    throw "Expected exactly one authorized RayNeo device, but found $($deviceLines.Count). Run 'adb devices -l' and resolve the connection first."
}

$serial = ($deviceLines[0] -split '\s+')[0]
$startedAt = Get-Date
$logcatProcess = $null
$result = "unknown"

@(
    "Test label: $TestLabel"
    "Started: $($startedAt.ToString('o'))"
    "Device: $($deviceLines[0])"
    "Package: $packageName"
) | Set-Content -LiteralPath (Join-Path $captureDirectory "session.txt") -Encoding utf8

& $adb -s $serial shell getprop | Set-Content -LiteralPath (Join-Path $captureDirectory "device_getprop.txt") -Encoding utf8
& $adb -s $serial shell dumpsys package $packageName | Set-Content -LiteralPath (Join-Path $captureDirectory "package.txt") -Encoding utf8
& $adb -s $serial logcat -c
& $adb -s $serial shell am force-stop $packageName

try {
    $logcatProcess = Start-Process -FilePath $adb `
        -ArgumentList @('-s', $serial, 'logcat', '-v', 'threadtime') `
        -RedirectStandardOutput $logcatPath `
        -RedirectStandardError $logcatErrorPath `
        -WindowStyle Hidden `
        -PassThru

    & $adb -s $serial shell am start -W -n "$packageName/$activityName" |
        Set-Content -LiteralPath (Join-Path $captureDirectory "launch.txt") -Encoding utf8

    $startupDeadline = (Get-Date).AddSeconds(30)
    $pidValue = ""
    while ((Get-Date) -lt $startupDeadline -and [string]::IsNullOrWhiteSpace($pidValue)) {
        $pidValue = ((& $adb -s $serial shell pidof $packageName) -join '').Trim()
        if ([string]::IsNullOrWhiteSpace($pidValue)) {
            Start-Sleep -Seconds 1
        }
    }

    if ([string]::IsNullOrWhiteSpace($pidValue)) {
        $result = "process-never-started"
        throw "The application process did not appear within 30 seconds."
    }

    Write-Host "Capture started in: $captureDirectory"
    Write-Host "The app PID is $pidValue. Perform the selected test on the glasses."
    Write-Host "The capture stops automatically when the app exits/crashes or after $TimeoutMinutes minute(s)."

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds $PollSeconds
        $currentPid = ((& $adb -s $serial shell pidof $packageName) -join '').Trim()
        if ([string]::IsNullOrWhiteSpace($currentPid)) {
            $result = "process-exited"
            Write-Host "The app process exited. Collecting crash evidence..."
            Start-Sleep -Seconds 3
            break
        }
    }

    if ($result -eq "unknown") {
        $result = "timeout-app-still-running"
        Write-Host "Timeout reached while the app was still running."
    }
}
finally {
    if ($logcatProcess -and -not $logcatProcess.HasExited) {
        Stop-Process -Id $logcatProcess.Id -Force -ErrorAction SilentlyContinue
        $logcatProcess.WaitForExit(5000) | Out-Null
    }

    $crashBuffer = @(& $adb -s $serial logcat -d -b crash -v threadtime)
    Set-Content -LiteralPath (Join-Path $captureDirectory "logcat_crash_buffer.txt") `
        -Value $crashBuffer -Encoding utf8
    & $adb -s $serial shell dumpsys activity exit-info $packageName |
        Set-Content -LiteralPath (Join-Path $captureDirectory "exit_info.txt") -Encoding utf8

    $endedAt = Get-Date
    @(
        "Result: $result"
        "Ended: $($endedAt.ToString('o'))"
        "Elapsed: $([math]::Round(($endedAt - $startedAt).TotalSeconds, 1)) seconds"
    ) | Add-Content -LiteralPath (Join-Path $captureDirectory "session.txt") -Encoding utf8

    $patterns = 'Fatal signal|SIGSEGV|GetRenderExtrinsicParameters|RayNeo_xrLocateViews|FATAL EXCEPTION|Unity.*Exception|NullReferenceException|resumeFromSleep|onResume|onPause'
    if (Test-Path -LiteralPath $logcatPath) {
        $importantLines = @(Select-String -LiteralPath $logcatPath -Pattern $patterns -CaseSensitive:$false |
            ForEach-Object { $_.Line })
        Set-Content -LiteralPath (Join-Path $captureDirectory "important_lines.txt") `
            -Value $importantLines -Encoding utf8
    }

    Write-Host "Capture complete: $captureDirectory"
}
