param(
    [string]$Address = "127.0.0.1",
    [ValidateRange(1, 65535)]
    [int]$Port = 4210,
    [ValidateRange(1, 3600)]
    [int]$DurationSeconds = 30,
    [ValidateRange(10, 5000)]
    [int]$IntervalMilliseconds = 100,
    [int]$Pressure1 = 900,
    [int]$Pressure2 = 200,
    [float]$KneeAngle = 30.0,
    [ValidateRange(0.1, 60.0)]
    [double]$WavePeriodSeconds = 4.0,
    [ValidateRange(0, 4095)]
    [int]$Pressure1Amplitude = 500,
    [ValidateRange(0, 4095)]
    [int]$Pressure2Amplitude = 150,
    [ValidateRange(0, 180)]
    [float]$KneeAngleAmplitude = 20.0
)

$udpClient = [System.Net.Sockets.UdpClient]::new()
$encoding = [System.Text.Encoding]::UTF8
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$sequence = 0

Write-Host "Sending ESP32 test data to ${Address}:${Port} for ${DurationSeconds}s..."

try {
    while ($stopwatch.Elapsed.TotalSeconds -lt $DurationSeconds) {
        $sequence++
        $elapsedSeconds = $stopwatch.Elapsed.TotalSeconds
        $phase = 2.0 * [Math]::PI * $elapsedSeconds / $WavePeriodSeconds

        $imu1Roll = [Math]::Round(20.0 * [Math]::Sin($phase), 2)
        $imu1Pitch = [Math]::Round(15.0 * [Math]::Sin($phase + 0.4), 2)
        $imu2Roll = [Math]::Round(25.0 * [Math]::Sin($phase + 1.1), 2)
        $imu2Pitch = [Math]::Round(18.0 * [Math]::Sin($phase + 0.8), 2)

        $pressure1Value = [Math]::Max(
            0,
            [Math]::Round($Pressure1 + $Pressure1Amplitude * [Math]::Sin($phase))
        )
        $pressure2Value = [Math]::Max(
            0,
            [Math]::Round($Pressure2 + $Pressure2Amplitude * [Math]::Sin($phase + [Math]::PI))
        )
        $kneeAngleValue = [Math]::Max(
            0.0,
            [Math]::Round($KneeAngle + $KneeAngleAmplitude * [Math]::Sin($phase + [Math]::PI / 3.0), 2)
        )

        $packet = [ordered]@{
            seq = $sequence
            timeMs = [int]$stopwatch.ElapsedMilliseconds
            imu1Ax = [Math]::Round(1.5 * [Math]::Sin($phase), 3)
            imu1Ay = [Math]::Round(1.0 * [Math]::Cos($phase), 3)
            imu1Az = [Math]::Round(9.81 + 0.3 * [Math]::Sin($phase * 2.0), 3)
            imu1Gx = [Math]::Round(0.4 * [Math]::Cos($phase), 3)
            imu1Gy = [Math]::Round(0.3 * [Math]::Sin($phase), 3)
            imu1Gz = [Math]::Round(0.2 * [Math]::Sin($phase + 0.5), 3)
            imu1Roll = $imu1Roll
            imu1Pitch = $imu1Pitch
            imu2Ax = [Math]::Round(1.2 * [Math]::Sin($phase + 0.7), 3)
            imu2Ay = [Math]::Round(0.8 * [Math]::Cos($phase + 0.7), 3)
            imu2Az = [Math]::Round(9.81 + 0.4 * [Math]::Sin($phase * 2.0 + 0.7), 3)
            imu2Gx = [Math]::Round(0.5 * [Math]::Cos($phase + 0.7), 3)
            imu2Gy = [Math]::Round(0.35 * [Math]::Sin($phase + 0.7), 3)
            imu2Gz = [Math]::Round(0.25 * [Math]::Sin($phase + 1.2), 3)
            imu2Roll = $imu2Roll
            imu2Pitch = $imu2Pitch
            pressure1 = [int]$pressure1Value
            pressure2 = [int]$pressure2Value
            kneeAngle = $kneeAngleValue
        }

        $json = $packet | ConvertTo-Json -Compress
        $bytes = $encoding.GetBytes($json)
        [void]$udpClient.Send($bytes, $bytes.Length, $Address, $Port)
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
}
finally {
    $udpClient.Dispose()
}

Write-Host "Finished. Sent $sequence packets."
