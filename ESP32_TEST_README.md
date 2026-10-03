# ESP32 / RayNeo desktop test

The configured scene is `Assets/Scenes/ESP32_Test.unity`.

## Run the test

1. Open `ESP32_Test` and enter Play mode.
2. In PowerShell, from the project root, run:

   ```powershell
   .\Tools\Send-Esp32TestData.ps1
   ```

3. The overlay should show `CONNECTED`. IMU, pressure, and knee-angle values
   continuously follow a four-second waveform around their configured centres.
4. Click the Game view and use `W` to approach the active echo target.
5. At a horizontal distance of 0.3 metres or less, the target is collected only
   when the corresponding sensor conditions are also satisfied. The Console
   logs `Echo completed using head position and ESP32 data.`

Current collection thresholds:

- Right-side echo: `Pressure 1 > 60` and right knee angle `> 10 degrees`.
- Left-side echo: `Pressure 2 > 60` only.

Controls: `W/S` forward/back, `A/D` strafe, `E/Q` up/down, mouse to look,
Left Shift to move faster, and Escape to release the cursor.

## Test a failing sensor condition

```powershell
.\Tools\Send-Esp32TestData.ps1 -Pressure1 40 -Pressure1Amplitude 5 -Pressure2 40 -Pressure2Amplitude 5 -KneeAngle 5 -KneeAngleAmplitude 2
```

The target should remain visible even when the camera is in range.

## Real ESP32

Send UTF-8 JSON over UDP to the computer/headset IPv4 address on port `4210` at
10-50 Hz. JSON field names are case-sensitive and must match `Esp32SensorData`.
The configured connection timeout is 0.3 seconds.

If Windows asks for firewall access, allow Unity on private networks. On the
RayNeo Android build, disable `DesktopRayNeoSimulator`; headset pose should come
from the RayNeo/OpenXR camera instead.

To rebuild or repair scene references, use Unity's menu:
`ACL Rehab > Build or Repair ESP32 Test Scene`.
