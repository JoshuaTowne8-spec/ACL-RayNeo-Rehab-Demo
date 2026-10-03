# RayNeo X3 Pro OpenXR diagnostic results

Date: 2026-09-28 (Asia/Shanghai)

## Confirmed from project files

- Unity version: 2022.3.36f1.
- RayNeo OpenXR ARDK: 1.1.2.
- The project previously pinned `com.unity.xr.openxr` to 1.11.0.
- RayNeo ARDK 1.1.2 declares `com.unity.xr.openxr` 1.7.0.
- The previous custom Android manifest declared CAMERA but did not declare
  `android.permission.HIGH_SAMPLING_RATE_SENSORS`.
- RayNeo ARDK 1.1.2 defines these real startup modes:
  - `DOF3 = 0x1001`
  - `SLAM = 0x2001`
- The existing `ESP32_Test` build uses
  `com.rayneo.openxradapter.UnityOpenXrActivity` and the official
  `XR Plugin.prefab`.
- The original native crash stack reached
  `ffalcon::XRConfiguration::GetRenderExtrinsicParameters(FXREye)` from
  `xrLocateViews`, before the ESP32/gameplay systems could run.

The project evidence confirms the OpenXR mismatch and missing sensor permission
as real risks. The project files cannot prove that the device factory calibration
file is missing or corrupt; that remains an unconfirmed device-side hypothesis.

## Changes applied

- Aligned `com.unity.xr.openxr` to 1.7.0 in `Packages/manifest.json`.
- Unity resolved and locked OpenXR 1.7.0 in `Packages/packages-lock.json`.
- Added `android.permission.HIGH_SAMPLING_RATE_SENSORS` to the custom manifest.
- Preserved `RunningPrototype_Debug.unity` and the existing `ESP32_Test.unity`.
- Added a minimal 3DOF scene using the official `XR Plugin.prefab`.
- Imported the official ARDK 1.1.2 Hello RayNeo Sample for the SLAM test.
- Added an editor build pipeline that produces three independent APKs and restores
  the previous package/product/OpenXR settings after each build.

## APK validation

All APKs use min SDK 30, target SDK 32, OpenGLES/ARM64, the RayNeo OpenXR
activity, and contain CAMERA plus HIGH_SAMPLING_RATE_SENSORS permissions.

| APK | Package | Size | Content |
| --- | --- | ---: | --- |
| `RayNeo_Diagnostic_3DOF.apk` | `com.aclrehab.rayneo.diagnostic3dof` | 39,797,371 bytes | Official XR Plugin, DOF3, no SLAM/gameplay |
| `RayNeo_Diagnostic_SLAM.apk` | `com.aclrehab.rayneo.diagnosticslam` | 39,815,931 bytes | Official Plane Detection Sample, SLAM |
| `ESP32_Test_OpenXR_1.7.0.apk` | `com.aclrehab.rayneo.demo` | 66,578,166 bytes | Full corrected ESP32_Test with right-temple confirmation |

## On-device results

Connected device:

- Model property: `ARGF20` (RayNeo X3 Pro)
- Android: 12 / API 32
- Build display property: `SKQ1.250204.001 release-keys`
- RayNeo Runtime reported in log: `1.1.6.7.20250605`

Test sequence and result:

1. Minimal 3DOF APK installed, CAMERA granted, and launched.
   - Alive after 10 seconds (PID 7220 at observation time).
   - No SIGSEGV, `GetRenderExtrinsicParameters`, or Unity exception.
2. Official Plane Detection/SLAM APK installed, CAMERA granted, and launched.
   - Alive after 12 seconds (PID 7293 at observation time).
   - No SIGSEGV, `GetRenderExtrinsicParameters`, or sensor-permission failure.
3. Corrected full ESP32_Test APK installed over the previous application and launched.
   - Alive after at least 35 seconds (PID 7359 at observation time).
   - Foreground/top application state confirmed.
   - No new SIGSEGV, `xrLocateViews`, Unity C# exception, null reference, or
     HIGH_SAMPLING_RATE_SENSORS permission failure.

The short launch test did not create a new crash record. A later, longer session
did reproduce the native crash described below, so the short survival result must
not be treated as proof that the issue is fixed.

## Reproduced long-session crash

At 2026-09-28 22:12:18, `com.aclrehab.rayneo.demo` (PID 7359) crashed with:

```text
Fatal signal 11 (SIGSEGV), fault addr 0x60
ffalcon::XRConfiguration::GetRenderExtrinsicParameters(FXREye)
Session::locateViews(...)
RayNeo_xrLocateViews
libUnityOpenXR.so
```

The process had been running for approximately 47 minutes. Device logs recorded
`resumeFromSleepEarly` around 22:11:53, approximately 25 seconds before the crash.
Subsequent launches crashed more quickly. No Unity C# exception preceded the
native failure.

This does not prove the sleep/resume transition is the sole cause, but it makes
OpenXR Runtime lifecycle handling and render-extrinsic restoration after wake the
highest-priority test. The failure still occurs below gameplay code in the RayNeo
Runtime. ESP32 parsing, collection rules, route generation, stickers, and origin
calibration are not implicated by this stack.

## Remaining observations and risks

- RayNeo client logs `java.io.FileNotFoundException: config file do not exist`
  from `FFalconXRClient.loadProfile`, then successfully establishes the XR client
  connection. It did not crash any of the three corrected APKs, but should be
  included in a RayNeo support report if tracking/rendering remains abnormal.
- A process staying alive for a short launch test does not prove the crash is
  resolved. It also does not prove plane quality, world tracking accuracy,
  correct floor selection, temple input, or ESP32 networking.
- Do not launch or modify `com.ffalcon.calibration` through ADB.

## Current input change

- Origin confirmation now uses the real ARDK 1.1.2 input action
  `RayNeoInput.SimpleTouch.Tap` (right-temple single tap).
- Keyboard `R` remains available for Unity Editor testing.
- The existing ring heavy-click long hold remains available only for resetting an
  already calibrated origin. No collection, running, route, or ESP32 rule changed.

## Controlled crash test matrix

Reboot the glasses before every row. Keep the glasses awake for ten minutes first,
then deliberately perform exactly one sleep/wake cycle and observe for two minutes.

| Test | APK | Before sleep/wake |
| --- | --- | --- |
| A | Minimal 3DOF | Leave running; no interaction |
| B | Official SLAM diagnostic | Slowly scan a textured floor |
| C | Full ESP32_Test | Do not confirm the origin |
| D | Full ESP32_Test | Confirm origin with one right-temple tap |
| E | Full ESP32_Test | Confirm origin, then connect ESP32 |

Use `Tools/Capture-RayNeoCrash.ps1` for each full-application run. It clears old
logcat data, launches the application, watches the process, and writes logcat,
crash-buffer, exit-info, device, and package evidence into a timestamped directory
under `Logs/RayNeoCrashCaptures`.

Examples:

```powershell
# Full ESP32_Test application
powershell -ExecutionPolicy Bypass -File .\Tools\Capture-RayNeoCrash.ps1 `
  -TestLabel Full_Unconfirmed -TimeoutMinutes 30

# Minimal 3DOF diagnostic
powershell -ExecutionPolicy Bypass -File .\Tools\Capture-RayNeoCrash.ps1 `
  -TestLabel Diagnostic_3DOF `
  -TargetPackage com.aclrehab.rayneo.diagnostic3dof `
  -TimeoutMinutes 30

# Official SLAM diagnostic
powershell -ExecutionPolicy Bypass -File .\Tools\Capture-RayNeoCrash.ps1 `
  -TestLabel Diagnostic_SLAM `
  -TargetPackage com.aclrehab.rayneo.diagnosticslam `
  -TimeoutMinutes 30
```

- If 3DOF also crashes after wake, treat this as a device Runtime/firmware or
  factory-calibration problem.
- If 3DOF survives but the official SLAM diagnostic crashes, isolate the issue to
  SLAM/Runtime lifecycle handling.
- If both diagnostics survive but test C crashes, inspect full-scene startup and
  lifecycle integration before touching gameplay rules.
- If C survives but D fails, inspect plane/origin session transitions.
- If D survives but E fails, only then investigate ESP32 integration.

## Latest baseline after the input change

On 2026-09-29 at 16:13 (Asia/Shanghai), the updated full APK was reinstalled on
device `BC942403CF11512`. CAMERA and HIGH_SAMPLING_RATE_SENSORS were both granted.
The application remained alive and resumed in the foreground for the complete
64.5-second no-interaction capture (PID 3503). That capture contains no SIGSEGV,
`GetRenderExtrinsicParameters`, `RayNeo_xrLocateViews`, Unity exception, or
high-sampling-rate sensor permission failure. This is only the cold-launch baseline;
the physical sleep/wake cases in the matrix remain required.

## Required physical verification

1. Wear the glasses in a bright, textured, open area.
2. Run `RayNeo Diagnostic SLAM` and slowly scan the floor. Confirm that detected
   planes appear and remain spatially stable while moving the head.
3. Run `ACL RayNeo Rehab Demo`.
4. Stand at the start, face the desired runway direction, and single-tap the right
   temple. Confirm the purple route stays fixed in the real world.
5. Walk slowly before running. Verify the route advances and the 90-degree turn is
   spatially stable.
6. Put the ESP32 and glasses on the same Wi-Fi, send UDP JSON to port 4210, and verify:
   - left echo: Pressure 2 >= 60 and distance <= 0.3 m;
   - right echo: Pressure 1 >= 60, right knee angle >= 60 degrees, and distance <= 0.3 m.

If a native crash returns, capture a fresh log immediately and compare its timestamp
with the historical exit records before changing gameplay code.
