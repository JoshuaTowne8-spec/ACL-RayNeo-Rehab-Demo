# RayNeo ground detection optimization review

Date: 2026-09-30 (Asia/Shanghai)

## Findings

- `PLANES 1` was the real count returned by `Algorithm.GetPlaneInfo` for the
  current frame. The previous `maxPlaneCount = 16` was only the managed buffer
  capacity and could not force the native RayNeo Runtime to return 16 planes.
- `AREA 16.0` was not a HUD constant. It was calculated from the polygon in
  `XRPlaneInfo.local_polygon`. A constant value means the Runtime kept returning
  a polygon with the same area; it does not prove that the measured floor really
  covers exactly 16 square metres.
- The displayed `H 2.0` came from the measured head-to-plane distance. It is now
  labelled `GROUND H` to avoid confusing it with a mesh or route height.
- The old selector accepted a plane when either its SDK type was horizontal or
  its converted normal looked horizontal. That allowed a plane tagged as
  horizontal to pass even when its visible orientation was nearly vertical.
- The old selected-plane mesh was rebuilt and repositioned every plane poll,
  which made small Runtime pose changes more visible.
- The old route renderer created a centre perspective guide and repeated distance
  crossbars after calibration. Those route-stage guides were separate from the
  floor-detection preview.

## Applied changes

- A floor must now pass all of these checks:
  - RayNeo property is `PLANE_HORIZONTAL_UP`;
  - absolute plane tilt is at most 15 degrees;
  - polygon area is at least the configured minimum;
  - head-to-plane distance is between 1.1 and 2.2 metres;
  - coordinate resolution is valid and within the maximum distance.
- Plane buffer capacity was increased from 16 to 32. This prevents truncation if
  the Runtime supplies more planes, but it still does not manufacture results.
- Up to six distinct valid horizontal candidates are retained for eight seconds.
  Approximate spatial matching merges repeated observations of the same plane.
- Candidate poses are smoothed and selection has hysteresis. Head gaze influences
  which candidate is selected. The selected candidate must remain stable for the
  existing sample count before confirmation is accepted.
- During scanning:
  - the selected plane is a translucent surface with a perspective grid;
  - the plane X direction is red;
  - the plane Y direction is blue (implemented on Unity's local Z coordinate);
  - other cached valid candidates appear as amber outlines.
- After origin confirmation, all plane-reference graphics are hidden as before.
- The route centre guide and repeated route distance crossbars were removed.
- Stability checks now compare the smoothed world-space candidate pose and plane
  normal, rather than the raw plane pose and its irrelevant in-plane yaw.

## HUD meanings

- `RAW`: planes returned by the native RayNeo API in the current poll.
- `VALID`: current raw planes that passed all ground checks.
- `CAND`: valid distinct candidates retained during the recent scan.
- `SEL`: identifier of the currently selected candidate.
- `POLY AREA`: area calculated from the SDK polygon, not a guaranteed physical
  floor measurement.
- `TILT`: angle between the candidate plane and a level horizontal surface.
- `GROUND H`: measured perpendicular head-to-candidate distance.

## Physical test

1. Use a bright, textured room and keep the floor in view.
2. Launch `ACL RayNeo Rehab Demo` and slowly scan left, centre, and right floor
   regions for at least eight seconds.
3. Confirm that wall-like planes show `WRONG_TYPE` or `TILT_TOO_HIGH` and do not
   receive the full grid.
4. Compare `RAW`, `VALID`, and `CAND`. `RAW` can remain one while `CAND` grows as
   distinct valid observations are retained over time.
5. Look toward the intended floor candidate. It should become the full grid while
   the other valid candidates remain amber outlines.
6. Wait until the HUD reports the required stable samples, stand at the start,
   face forward, and single-tap the right temple.
7. Confirm that the grid disappears, no route perspective/crossbar guides remain,
   and only the normal translucent route plus echoes are visible.
8. Stand still for 20 seconds, then walk slowly. Record whether the route moves
   continuously, jumps once during relocalization, or repeatedly disappears.

## Build and launch verification

- Unity 2022.3.36f1 script compilation: passed.
- Android ARM64/OpenGLES build: passed.
- APK: `Builds/RayNeoDiagnostics/ESP32_Test_OpenXR_1.7.0.apk`.
- Final APK size: 66,695,833 bytes.
- Installed successfully on device `BC942403CF11512`.
- Final no-interaction baseline: process remained alive for 65.4 seconds with no
  SIGSEGV, `GetRenderExtrinsicParameters`, Unity exception, or missing-reference
  error.
