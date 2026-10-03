# RayNeo X3 Pro 6DoF spatial diagnostic

## Purpose

This diagnostic isolates RayNeo SLAM head tracking, Unity/OpenXR pose delivery,
plane coordinates, and relocalization from all rehabilitation gameplay.

It does not load or reference ESP32 input, track generation, echo collection,
`GroundPlaneSelector`, `TrackOriginCalibrator`, or the running-session controller.
The official `HeadTrackedPoseDriver` output is observed but never scaled,
integrated, smoothed, recentered, or overwritten.

## Files

- Scene: `Assets/Scenes/RayNeoDiagnostics/RayNeo_SpatialPose_Diagnostic.unity`
- APK: `Builds/RayNeoDiagnostics/RayNeo_SpatialPose_Diagnostic.apk`
- Package: `com.aclrehab.rayneo.spatialdiagnostic`
- Capture: `Tools/Capture-RayNeoSpatialDiagnostic.ps1`
- Analysis: `Tools/Analyze-RayNeoSpatialDiagnostic.ps1`

The APK is an Android ARM64/OpenGLES3 Development Build using SLAM/6DoF.

## Visual legend

- White/pink/cyan cubes: objects placed once in Unity world space after SLAM succeeds.
- Red/green/blue lines: fixed world X/up/forward reference near the estimated floor.
- Green plane: RayNeo official sample interpretation. Converted plane pose is applied
  directly to a root-level object.
- Blue plane: converted plane pose is kept as a child-local pose under the official
  `XROrigin` referenced by `HeadTrackedPoseDriver`.
- Yellow plane: the same pose is explicitly converted through the `XROrigin` matrix
  and written to a root-level object at each plane poll.
- Right-temple single tap: clone all current plane visuals into root-level frozen
  snapshots. Frozen objects never receive another SDK plane update.

If all three live planes overlap, the three coordinate interpretations are equivalent
for the current XR Origin pose. A visible separation is useful diagnostic evidence.

## Install

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\2022.3.36f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$apk = "C:\Users\thejo\Desktop\studio\ACL rehab\ACL_RayNeo_Demo\Builds\RayNeoDiagnostics\RayNeo_SpatialPose_Diagnostic.apk"

& $adb devices -l
& $adb install -r $apk
& $adb shell pm grant com.aclrehab.rayneo.spatialdiagnostic android.permission.CAMERA
```

Do not run the full rehabilitation APK at the same time.

## Capture one test

Run one physical procedure per capture so its statistics remain interpretable:

```powershell
cd "C:\Users\thejo\Desktop\studio\ACL rehab\ACL_RayNeo_Demo"

powershell -ExecutionPolicy Bypass -File .\Tools\Capture-RayNeoSpatialDiagnostic.ps1 `
  -TestLabel Stationary60s -Install
```

After the diagnostic launches, perform the test, return to PowerShell, and press
Enter. The script captures logcat, crash logs, device properties, package details,
and device CSV files under `Logs/RayNeoSpatialDiagnostics/<timestamp>_<label>`.

Only use `-Install` for the first capture or after rebuilding the APK.

## Test 1: stationary baseline

Preparation:

1. Reboot the glasses.
2. Use a bright, textured, non-reflective area first.
3. Stand on a marked point and face a marked direction.
4. Launch the capture and wait 15 seconds for initialization.
5. Keep head and body as still as practical for 60 seconds.
6. Do not tap the temple during this run.

Pass target:

- SLAM remains `FFVINS_TRACKING_SUCCESS`.
- World reference cubes do not travel in one direction and snap back.
- No single `head_world_step` reaches 0.15 m while stationary.
- Start-to-end `head_world` drift is at most 0.05 m.
- When returning to exactly the marked viewing direction, orientation error should
  be at most about 2 degrees.
- Plane timestamps do not move backwards.

Small millimetre-to-centimetre motion is expected from visual-inertial tracking.
Repeated 10-15 cm jumps, periodic return to an old pose, or continuous rotation of
the whole world around the head is not acceptable.

Analysis:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Analyze-RayNeoSpatialDiagnostic.ps1 `
  -CsvPath "<captured csv>" -Mode Stationary
```

## Test 2: yaw-only rotation

1. Stand on the same marked point.
2. Face forward for five seconds.
3. Slowly rotate the head about 45 degrees left, return to centre, then 45 degrees
   right and return to centre. Do not step or lean.
4. Repeat twice over 30-45 seconds.

Pass target:

- World reference cubes stay at the same real locations instead of following the
  head or orbiting around it.
- Head position returns within 0.10 m of its start position.
- After returning to the physical forward mark, orientation returns within 3 degrees.
- A large rotational change is expected; large translation synchronized with yaw is not.

If objects rotate with the head while the SDK, Head Transform, and XR device rotations
all change normally, inspect parenting and coordinate-space use. If all world objects
move while Head world position/rotation jumps, the fault is below gameplay code.

## Test 3: measured one-metre translation

1. Mark a straight one-metre line on the floor.
2. Start at the first mark and keep the same facing direction.
3. Wait five seconds, walk slowly to the second mark, then remain there for ten seconds.
4. End the capture without walking back.

Pass target:

- Maximum `head_world` displacement from the start is 0.85-1.15 m.
- The displacement direction matches the physical walking direction.
- SDK callback, Head Transform, and Unity XR device position streams have consistent
  direction and proportional magnitude.
- Fixed world cubes remain in their original physical locations while the user moves.

Analysis:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Analyze-RayNeoSpatialDiagnostic.ps1 `
  -CsvPath "<captured csv>" -Mode Walk1m
```

If `sdk_head` changes by a very large amount while `head_world` and `xr_device` report
approximately one metre, the SDK callback must not be scaled and reused as a replacement
Head pose. If all three streams have the same wrong scale, investigate Runtime output.

## Test 4: live plane versus frozen snapshot

1. Look down and slowly scan left, centre, and right for 10-15 seconds.
2. When a live plane is visible, single-tap the right temple once.
3. Confirm `SNAPSHOT 1` on the HUD.
4. Slowly turn the head, move 20-30 cm left/right, and return.
5. Observe each coloured live plane against its same-colour brighter frozen clone.

Interpretation:

- Live planes move but frozen planes and world cubes stay fixed: plane detector
  refinement/identity is unstable; 6DoF world tracking is comparatively healthy.
- Frozen planes and world cubes move together: camera/SLAM world tracking or
  relocalization is unstable.
- Green is correct while blue/yellow are wrong: use the official direct plane pose;
  converting through XR Origin is incorrect for this Runtime.
- Blue/yellow are correct while green is wrong: the plane pose is local to XR Origin.
- Blue follows XR Origin continuously while yellow holds between 0.2-second polls:
  XR Origin itself is changing and parented content inherits that motion.
- All three have the same wrong tilt: the returned plane rotation/type or Runtime plane
  estimate is wrong; selector smoothing cannot correct the underlying orientation.
- All live planes repeatedly alternate between two positions and plane timestamps also
  regress or alternate: the Runtime is likely returning stale/relocalized results.

For a stationary plane run, investigate raw plane movement greater than 0.10 m or
rotation greater than 5 degrees between 0.2-second polls.

## Test 5: controlled relocalization

Only run this after Tests 1-4.

1. Create a frozen snapshot.
2. Look briefly at a low-texture surface or cover the tracking cameras for about two
   seconds; do not put the glasses to sleep.
3. Restore the original view and remain still for 30 seconds.

Acceptable behavior is one tracking-loss/recovery sequence and at most one world
correction. Repeated `SUCCESS -> FAIL/INITIALIZING -> SUCCESS` loops, repeated snapback,
or oscillation between coordinate origins is a failure.

## Reflective-floor comparison

Repeat Tests 1 and 4 first on a matte textured floor and then on the reflective wooden
floor. Keep lighting and motion similar.

- Only the reflective floor degrades live planes while fixed cubes stay stable:
  environment/plane detection is the main limitation.
- Both floors move fixed cubes: not merely a reflective-floor problem.
- Fixed cubes are stable but all planes are tilted on both floors: plane pose coordinate
  or Runtime plane-orientation issue.

## CSV fields used for diagnosis

- `sdk_head_*`: unmodified pose received from `HeadTrackedPoseDriver.OnPostUpdate`.
- `head_local_*`, `head_world_*`: actual Head Transform after Unity updates.
- `xr_device_*`: standard Unity XR centre-eye pose when available.
- `raw_plane_*`: `Algorithm.ConvertPlanePosition/Rotation` output.
- `official_*`: root-level official-sample interpretation.
- `origin_child_*`: current world pose of the plane parented under XR Origin.
- `explicit_*`: root-level world pose explicitly calculated from XR Origin at poll time.
- `plane_timestamp`: SDK `XRPlaneInfo.pose.timestamp`, which the gameplay snapshot
  currently does not retain.
- Event rows identify app pause/resume, SLAM transitions, snapshots, and detected jumps.

The numerical thresholds above are prototype acceptance limits, not RayNeo-published
hardware specifications. They are intended to decide which software layer to inspect
next and must be evaluated together with physical visual alignment.
