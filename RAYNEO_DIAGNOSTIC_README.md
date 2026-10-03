# RayNeo X3 Pro OpenXR diagnostic builds

This project keeps the existing desktop test scene and `ESP32_Test` scene intact.
The diagnostic build pipeline creates three independent APKs in this order:

1. `RayNeo_Diagnostic_3DOF.apk`
   - Official `XR Plugin.prefab`
   - `CameraAttitudeType = DOF3 (0x1001)`
   - No SLAM, plane detection, ESP32, track, echo, or rehabilitation scripts
2. `RayNeo_Diagnostic_SLAM.apk`
   - Official ARDK 1.1.2 `Hello RayNeo/PlaneDetection` sample
   - `CameraAttitudeType = SLAM (0x2001)`
   - No ESP32, track, echo, or rehabilitation scripts
3. `ESP32_Test_OpenXR_1.7.0.apk`
   - Existing full `ESP32_Test` scene
   - OpenXR 1.7.0 and the high-sampling-rate sensor permission

## Unity menu

Run these commands after package resolution and script compilation finish:

1. `Tools > ACL Rehab > RayNeo Diagnostics > 1 - Prepare Diagnostic Scenes`
2. Wait for Unity to finish importing/compiling the official sample.
3. `Tools > ACL Rehab > RayNeo Diagnostics > 2 - Build All Diagnostic APKs`

Individual build menu commands are also available under the same menu.

## Test interpretation

- 3DOF fails in `GetRenderExtrinsicParameters`: device Runtime/render calibration is the primary suspect.
- 3DOF works, SLAM fails: SLAM mode, sensor access, Glass OS, or Runtime compatibility is the primary suspect.
- Both diagnostics work, full ESP32 test fails: investigate scene startup order and application SLAM initialization.

Do not launch or modify `com.ffalcon.calibration` through ADB. It is a factory/service tool.
