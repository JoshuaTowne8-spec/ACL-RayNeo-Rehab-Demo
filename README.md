# ACL RayNeo Rehabilitation Demo

An augmented-reality running rehabilitation prototype for the RayNeo X3 Pro.
The application detects a usable floor with RayNeo SLAM, lets the user define a
real-world track origin, renders a bounded AR running route, and collects
alternating foot-placement targets using live pressure and knee-angle data from
an ESP32 sensor system.

## Current platform

- Unity: 2022.3.36f1
- Render pipeline: Built-in Render Pipeline
- Target: Android / ARM64 / OpenGLES3
- Device: RayNeo X3 Pro
- RayNeo SDK: OpenXR Unity ARDK 1.1.2
- OpenXR package: 1.7.0
- Tracking mode: SLAM / 6DoF
- Main scene: `Assets/Scenes/ESP32_Test.unity`
- ESP32 transport: UDP JSON on port `4210`

The project does not use ARCore or ARKit.

## Prototype behavior

1. The headset initializes RayNeo SLAM and detects horizontal floor planes.
2. The user stands at the intended start point, faces the running direction,
   and confirms the origin with the right temple control.
3. The application creates a 1.2 m-wide track with 7 m segments and 90-degree
   turns. The generated route remains inside a 20 m by 20 m area.
4. Only the current and next track segments are visible. The track uses a light
   purple `#7333FF` material at approximately 65% opacity.
5. Echo targets appear alternately on the left and right at running-step
   intervals, near the route centerline.

### Collection rules

An echo must be within 0.3 m of the tracked user before sensor input can collect
it.

- Right-side echo / affected leg: `Pressure 1 > 60` and right knee angle
  `> 10 degrees`.
- Left-side echo: `Pressure 2 > 60` only.

The comparisons are strict. A value equal to 60 or an angle equal to 10 degrees
does not satisfy the rule.

## Capture controls

After the origin is confirmed, the right temple control supports:

- Single click: show or hide the in-headset diagnostics HUD.
- Double click: start or stop video recording.
- Long press for approximately 0.9 seconds: take a screenshot. Screenshots are
  also available before origin confirmation.

Each capture is exported as two separate files:

- `reality_*`: RGB camera content without Unity graphics.
- `unity_*`: Unity route and echo content on a black background, without the
  large capture HUD.

## Demonstration videos

The videos are stored in [`TestVideos`](TestVideos/README.md) and are managed by
Git LFS.

- [TestVideo01.mp4](TestVideos/TestVideo01.mp4) — first-person outdoor AR effect
  demonstration in a real test area.
- [TestVideo02.mp4](TestVideos/TestVideo02.mp4) — successful echo collection.
  It demonstrates that an echo is collected when the user performs the intended
  cushioned running-landing action and the sensor conditions are satisfied.
- [TestVideo03.mp4](TestVideos/TestVideo03.mp4) — third-person outdoor view of
  the prototype being used.

## Project layout

| Path | Purpose |
| --- | --- |
| `Assets/Art`, `Assets/Textures`, `Assets/Models` | Echo artwork and visual assets |
| `Assets/Materials`, `Assets/Shaders` | Track, ground-preview, and AR rendering |
| `Assets/Prefabs` | Route, origin, echo, and gameplay prefabs |
| `Assets/Scenes` | Main prototype, desktop debug, and diagnostic scenes |
| `Assets/Scripts` | ESP32 input, gameplay, SLAM, plane selection, capture, and diagnostics |
| `Assets/Editor` | Scene setup and Android build utilities |
| `Assets/Plugins/Android` | RayNeo-compatible Android manifest |
| `Assets/XR` | OpenXR settings and loader configuration |
| `Packages` | Unity dependencies and the embedded RayNeo ARDK package |
| `ProjectSettings` | Unity project and Android player settings |
| `Tools` | ESP32 simulation and RayNeo diagnostic PowerShell scripts |
| `ESP32Firmware` | ESP32 sensor firmware, wiring, and upload instructions |
| `TestVideos` | Real-device demonstration videos |

Generated Unity folders such as `Library`, `Logs`, `UserSettings`, and `Builds`
are intentionally excluded from version control.

## Open and run in Unity

1. Install Unity 2022.3.36f1 with Android Build Support, the Android SDK/NDK,
   and OpenJDK.
2. Clone this repository using Git LFS:

   ```powershell
   git lfs install
   git clone <repository-url>
   ```

3. Open the repository folder as a Unity project.
4. Open `Assets/Scenes/ESP32_Test.unity`.
5. For desktop interaction testing, open
   `Assets/Scenes/RunningPrototype_Debug.unity` instead.

## ESP32 data

The headset and ESP32 must be on the same network. Send UTF-8 JSON packets to
the headset IPv4 address on UDP port `4210` at approximately 10-50 Hz. Relevant
fields are:

```json
{
  "seq": 1,
  "timeMs": 1000,
  "pressure1": 80,
  "pressure2": 0,
  "kneeAngle": 20.0
}
```

The complete accepted schema is defined by `Esp32SensorData` in
`Assets/Scripts/Esp32UdpReceiver.cs`. Field names are case-sensitive.

For a computer-generated test stream, see
[`ESP32_TEST_README.md`](ESP32_TEST_README.md) and
`Tools/Send-Esp32TestData.ps1`.

The real sensor firmware is available in
[`ESP32Firmware/ACL_RayNeo_Sensor`](ESP32Firmware/ACL_RayNeo_Sensor/README.md).
Copy `secrets.example.h` to `secrets.h`, enter the local Wi-Fi credentials and
headset IPv4 address, and then upload the sketch to the ESP32.

## Build for RayNeo X3 Pro

In Unity, select:

`Tools > ACL Rehab > Build RayNeo X3 Verification APK`

The development APK is generated at:

`Builds/ACL_RayNeo_X3_Spatial_Verification.apk`

Install it with ADB:

```powershell
adb install -r Builds/ACL_RayNeo_X3_Spatial_Verification.apk
```

The Android entry activity is
`com.rayneo.openxradapter.UnityOpenXrActivity`. Camera and high-sampling-rate
sensor permissions are declared in the custom Android manifest.

## Additional technical notes

- [`RAYNEO_DIAGNOSTIC_README.md`](RAYNEO_DIAGNOSTIC_README.md)
- [`RAYNEO_DIAGNOSTIC_RESULTS.md`](RAYNEO_DIAGNOSTIC_RESULTS.md)
- [`RAYNEO_GROUND_DETECTION_REVIEW.md`](RAYNEO_GROUND_DETECTION_REVIEW.md)
- [`RAYNEO_SPATIAL_DIAGNOSTIC_GUIDE.md`](RAYNEO_SPATIAL_DIAGNOSTIC_GUIDE.md)

## Safety and prototype status

This is a research and prototype application, not a certified medical device.
Use it only in a clear, supervised test area. Confirm the complete physical
route is free of obstacles before beginning a running trial.
