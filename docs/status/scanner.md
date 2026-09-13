# Scanner Status

## Current state
- **S4 implemented and passing off-device; physical-device verification is
  pending.** Room height is now captured by deriving vertical wall planes from
  the S3 footprint, intersecting the center-screen ray with the wall the user
  selects, and validating the result against 2.0-4.0 m. A manual numeric
  fallback exists per the implementation plan's section 8.7. Do not treat this
  as fixed on hardware until the physical-device procedure in the S4 handoff
  has been run and passed — see "Next safe task" below.
- Delivered in S4: `HeightCaptureController`, `HeightCaptureHud`, the S4
  transition and real `heightM` on `ScanWorkflowController`'s snapshot, and the
  scene builder's wall-selection / capture / manual-entry UI. `VerifyScene()`
  now asserts the S4 wiring too.
- **S3 complete and verified on a physical iPhone.** Four-corner capture and
  closure verification work on device: corners are captured from the
  center-screen ray against the locked floor plane, every stored corner lands on
  GhostMap y = 0, markers stay put, the S2 frame does not move, and closure
  measured **0.031 m — Excellent** on the test room.
- Delivered in S3: `CornerCaptureController`, `CornerCaptureHud`, the S3
  transitions and snapshot contents on `ScanWorkflowController`, and the scene
  builder's corner-capture UI. `VerifyScene()` now asserts the S3 wiring as
  well.
- **S2 complete and verified on a physical iPhone.** Floor lock and the GhostMap
  coordinate frame work on device: the frame is established from a confirmed
  floor, it is right-handed in Unity's sense, it stays fixed while the user moves,
  and the floor normalizes to Ghost y = 0.
- Delivered in S2: `ArSpatialProvider` / `ISpatialProvider`,
  `FloorLockController`, `ScanWorkflowController`, `ScanPhase`, and the
  `FloorLockHud` crosshair / Lock Floor button / readout. The scene builder
  produces all of it, and `ScannerSceneBuilder.VerifyScene()` asserts the wiring
  so a null reference fails on a laptop rather than silently on a phone.
- **S1 complete and verified on a physical iPhone.** The scanner Unity project,
  AR smoke-test scene, two-step iOS build pipeline and on-device diagnostics are
  in place, and the Task S1 physical-device test passes.
- `apps/scanner/` is a Unity 6000.3.24f1 project: `Packages/manifest.json`
  (AR Foundation 6.3.1, Apple ARKit XR Plugin 6.3.1, `com.ghostmap.shared`),
  `ProjectSettings`, `Assets/XR` XR Plug-in Management configuration,
  `Assets/GhostMap/Scanner/Scanner.unity`, runtime bootstrap, editor build
  scripts, and EditMode tests.
- Two device failures were found and fixed along the way, each with a distinct
  root cause. Both are recorded below and in their handoffs.

## Last verified commit
- S4's implementation commit is not yet physically verified — see the S4
  handoff for the exact commit once it exists. Do not treat S4 as done on
  hardware until that verification is recorded here.
- `1e04d02` — S3, verified on a real iPhone. The commits that follow it change
  only documentation, so their scanner sources are byte-identical.
- `b0fe6f0` — S2, verified on a real iPhone. `4dd4c13` follows it and changed
  only a handoff document, so its scanner sources are byte-identical.
- `80b5a48` — S1, verified on a real iPhone.
- S1 reached `main` as merge commit `150512d` (PR #1) and S2 as merge commit
  `a3f15f8` (PR #2). Both were merged with a merge commit so the original task
  SHAs stay reachable from the handoff documents that cite them.

## Physical-device verification — S3, passed
Observed on a real iPhone against the build produced from `1e04d02`:

- `Phase` reaches `CaptureCorners` after the floor lock
- all four corners capture successfully, in order
- every stored corner reads GhostMap `y = 0.00`
- markers stay fixed at the locations they were captured at
- `Frame O`, `Frame X` and `Frame Z` unchanged after the floor lock
- closure verification works
- **closure error 0.031 m, classified `Excellent`**
- a successful closure advances the phase to `CaptureHeight`
- `Session` stays `SessionTracking`
- `Handedness` stays `+1.000`
- GhostMap camera coordinates keep updating correctly as the user moves
- **Undo** removes the most recent corner correctly
- **Redo Corners** clears the corner set without changing the locked frame
- a deliberately too-close corner is refused by the spacing validation

That covers every item of the Task S3 device procedure in
`docs/handoffs/2026-09-12-scanner-s3-corner-capture-closure.md`, and confirms on
hardware what the automated tests assert off it: capture is anchored to the
locked floor plane, corners land on Ghost y = 0, the S2 frame does not move
during or after corner capture, and the closure bands behave as specified.

The 0.031 m closure is the strongest on-device evidence available that the frame
did not drift across the scan. A frame that had moved would have surfaced here as
accumulated error rather than as a clean re-aim onto the stored first corner.

## Task S4 — height capture (implemented, not yet device-verified)

### How a height is captured
```text
walls  = RoomGeometry.BuildWalls(room)          // from the S3 corners, in order
wall   = walls[selectedWallIndex]               // user-selected
plane  = WallGeometry.PlaneFor(wall)            // vertical, through wall.Start
ray    = provider.GetScreenRay(provider.CenterScreenPoint)   // AR world space
ghost  = frame.WorldRayToGhost(ray)             // Ghost-space ray
        -> intersect ghost ray with plane
        -> candidate = intersection.y
        -> reject if candidate < 2.0 or > 4.0
        -> RoomModel.heightM = candidate, revision++, republish snapshot
```

Exactly implementation plan section 8.7: the footprint is already exact, so a
vertical plane through two consecutive Ghost-space corners is the real wall,
without waiting on ARKit to detect a vertical plane or depending on LiDAR.
`HeightCaptureController` never raycasts against a detected plane — the same
reasoning S3's corner capture already established for the floor.

### Wall selection
Walls are derived, never serialized, from the same `RoomGeometry.BuildWalls`
S3 uses: wall 0 is corner 0 -> corner 1, and so on. The HUD auto-selects wall 0
the first time the phase reaches `CaptureHeight`, then lets the user cycle
through the rest with **Wall N/4**. A yellow world-space line is drawn along
the selected wall's floor edge, and a marker is drawn at the current aim
point — green if the candidate height would validate, red otherwise.

### Distinguishing ray rejections
`RayPlaneMath.TryIntersectPlane` collapses "parallel to the plane" and
"intersection behind the camera" into one `false`. Height capture needs to
tell them apart — the plan lists them as separate test cases — so
`HeightCaptureController` re-implements the same two-step check
(`RayPlaneMath.ParallelEpsilon`, then `Plane.Raycast`'s own distance) rather
than calling `TryIntersectPlane` and losing the distinction. This is not a
second implementation of a validation rule (the parity risk `docs/status/shared.md`
warns about); it is exposing which half of one existing check fired.

### Validation and the manual fallback
The 2.0-4.0 m range comes from `RoomValidator.MinRoomHeightM` /
`MaxRoomHeightM` directly — referenced, not restated — so the two can never
drift apart. A rejected candidate leaves `HeightM` and `HasCapturedHeight`
exactly as they were; nothing is clamped.

Per plan section 8.7, "a failed automatic height capture must never block the
demo": `TrySetManualHeight` runs the same validation and is always available
in `CaptureHeight`, independent of wall selection or aim. It is not written to
the wire — scene schema v1 has no manual-entry flag, and adding one is a
contract change this task does not need — so it is HUD-visible only, via
`HeightCaptureController.IsManualEntry`.

### The S2 frame and S3 footprint are read, never written
`HeightCaptureController` holds `ISpatialProvider`, `FloorLockController` and
`CornerCaptureController` only to read from them: `Frame`, `CopyCorners()` and
`IsComplete`. It has no path to move the frame or mutate a corner. Tests pin
the frame surviving by reference and the corners surviving by value across a
height capture.

### Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no viewer file was
touched. `RoomModel.heightM` already existed in scene schema v1 with exactly
this meaning; S4 is the first task to actually write a non-zero value into it.

## Task S3 — corner capture and closure

### How a corner is captured
```text
ray   = provider.GetScreenRay(provider.CenterScreenPoint)   // AR world space
world = RayPlaneMath.TryIntersectHorizontalPlane(ray, frame.FloorWorldY)
ghost = frame.WorldToGhost(world)
ghost.y = 0                                                 // forced, not assumed
        -> RoomValidator.ValidateNewCorner(existing, ghost)
        -> RoomValidator.ValidateRoom(existing + candidate)
        -> append with a GUID id, revision++, republish snapshot
```

A corner is found by arithmetic, **not** by an AR plane raycast. Plane extents
lag the room, stop at skirting boards and furniture, and are routinely absent at
exactly the corner being aimed at, so requiring a plane hit per corner would make
real rooms uncapturable. The locked floor plane is already known exactly. A test
asserts the plane raycaster is never consulted after the lock.

Validation runs twice on purpose. `ValidateNewCorner` sees one candidate against
its neighbours; it cannot see maximum wall length, footprint area or interior
angles, because those belong to the chain rather than to a corner. `ValidateRoom`
covers the partial chain before four corners and the whole footprint at four.

### Closure bands
`RoomValidator.ClassifyClosure`, boundaries inclusive:

| Error | Band | Result |
| --- | --- | --- |
| `<= 0.08 m` | `Excellent` | proceed to `CaptureHeight` |
| `> 0.08 m`, `<= 0.15 m` | `Acceptable` | proceed, warn |
| `> 0.15 m` | `Rejected` | stay in `VerifyClosure`; Redo Corners only |

### Two decisions recorded
- **Revision is monotonic, including across undo.** The plan's S3 list says
  "undo decrements count/revision". The count is decremented; the revision
  advances like any other mutation. Scene schema v1 has the viewer ignore any
  snapshot whose revision is not greater than the one it holds, so an undo that
  wound the counter back would publish a room the viewer must drop.
- **An accepted closure moves the phase to `CaptureHeight`**, per the plan's
  state machine and its "otherwise proceed". No height-capture behaviour exists;
  that is S4. The S3 HUD offers only Redo Corners there.

### The S2 frame is read, never written
`FloorLockController` refuses a second lock and `GhostCoordinateFrame` has no
setters, so nothing in S3 can move the frame. Five tests pin it: the same object
survives every capture and the closure check, its origin and axes are unchanged,
a stored corner does not shift when the camera moves, a re-lock attempt during
capture is refused, and `RedoCorners` keeps the frame while clearing the
footprint.

## Physical-device verification — S2, passed
Observed on a real iPhone against the build produced from `b0fe6f0`:

- `Phase` reaches `FloorLocked`
- `Session` is `SessionTracking`
- `Handedness: +1.000` — the frame is not mirrored
- `Cam G` exists and updates as the user moves
- stepping right increases GhostMap **x**
- walking forward increases GhostMap **z**
- crouching lowers GhostMap **y**
- `Frame O`, `Frame X` and `Frame Z` stay fixed after the lock
- floor points stay near GhostMap **y = 0**
- returning near the lock spot returns close to the GhostMap origin
- no mirrored-axis behavior observed

That covers every item of the Task S2 device procedure in
`docs/handoffs/2026-09-12-scanner-s2-floor-lock-coordinate-frame.md`, and in
particular confirms on hardware what the automated tests assert off it: the
frame is right-handed, immutable after lock, and immune to `CameraYOffset`.

## The GhostMap coordinate frame — S2
Built at the floor-lock instant, exactly as `GhostCoordinateFrame`'s own
documentation specifies:

```text
up      = Vector3.up
forward = ProjectOnPlane(camera.forward, up).normalized
right   = Cross(up, forward).normalized
origin  = floor raycast hit (world space)
```

`Cross(up, forward)` is +X in Unity's left-handed basis, so the axes satisfy
`Cross(right, up) == forward` — the same relationship Unity's world axes
satisfy. Reversing the cross would mirror every captured room and **nothing
downstream would notice**: area, wall length, closure error and interior angle
are all unchanged by a reflection. Handedness is therefore asserted directly and
printed on the device readout.

The frame is immutable once locked. `GhostCoordinateFrame` has no setters and
`FloorLockController` refuses a second lock.

## CameraYOffset — resolved, no compensation needed
This was deferred at S1 with "decide it when the floor is locked". Resolved: it
needs no compensation, and compensating for it would be a bug.

The AR camera is a child of Camera Offset, whose local Y is `CameraYOffset`
(1.1176) because ARKit reports `TrackingOriginModeFlags.Device`. Detected planes
hang off `XROrigin.TrackablesParent` instead, so it looks as though camera and
planes sit in two spaces 1.12 m apart. They do not. In
`com.unity.xr.core-utils@a8b9003/Runtime/XROrigin.cs`, `OnBeforeRender` (line
611) assigns `TrackablesParent` the pose from `GetCameraOriginPose()` (line 585),
which returns the camera's **parent** — Camera Offset itself. The same offset is
baked into both.

The frame's origin is the floor hit, so `WorldToGhost` subtracts the shared
offset away exactly: the floor lands on Ghost `y = 0` for any offset value and
the camera reads its true eye height above the floor.

**This holds only because every reading is taken in world space.** Mixing in a
*local* position — the camera's `localPosition`, printed as "Cam L" in the S1
diagnostics — would reintroduce the offset. Nothing in `ArSpatialProvider` reads
one. `m_CameraYOffset` is deliberately left at AR Foundation's default.

## Physical-device verification — S1, passed
Observed on a real iPhone against the build produced from `80b5a48`:

- `Active loader: ARKitLoader`
- live camera passthrough works
- `Session` reaches `SessionTracking`
- `Not tracking reason: None`
- `XRInput: running`
- `Cam L` and `Cam W` position change as the phone moves
- camera rotation changes as the phone rotates
- `Cam moved` counter increases
- detected plane count goes above 0
- `floor candidate: yes` when a floor plane is detected

This satisfies the Task S1 physical-device test in
`docs/plans/ghostmap-implementation-plan.md`: camera feed displays, the AR
session reaches tracking, the plane manager reports a floor candidate, and the
screen shows session state, notTrackingReason and camera pose.

## Tests run

### S4 — latest
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s4-final.xml -logFile /tmp/ghostmap-s4-final.log
```
**292 tests, 292 passed, 0 failed, 0 skipped.** Unity exit code 0.
`HeightCaptureControllerTests` 18 (new), `ScanWorkflowControllerTests` 39
(31 + 8 new S4 tests), `CornerCaptureControllerTests` 37, `GhostFrameTests` 20,
`FloorLockControllerTests` 17, `ScannerXrSettingsTests` 4, `ScannerSceneTests` 1,
`GhostMap.Shared.Tests` 156. S3 finished at 266.

Mutation-checked rather than merely observed passing:

| Mutation | Result |
| --- | --- |
| Skip `frame.WorldRayToGhost` and intersect the raw world ray | **14 failed** — every test that used the hostile fixture frame (floor 1.4 m below Unity's origin, 40° yaw) |
| Force the parallel-ray check to never fire (`if (false)` instead of the epsilon test) | **1 failed** — exactly `RayParallelToTheWallIsRejected`, and only that one |

The first mutation biting 14 of 18 controller tests (not all 18 — the manual-
height and wall-derivation tests never touch the ray) is itself evidence the
suite is not vacuously passing. The second mutation confirms the
parallel/behind-camera distinction is load-bearing rather than incidental:
exactly the one test written to catch it fails, nothing else.

`ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0, and
`xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported **BUILD SUCCEEDED**.

The shared package was also run standalone in its own host project, to confirm
S4 changed nothing under `shared/`:
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s4-shared.xml -logFile /tmp/ghostmap-s4-shared.log
```
**156 tests, 156 passed, 0 failed, 0 skipped.** Unity exit code 0 — unchanged
from S3.

**Physical-device verification has not been run yet.** Do not treat S4 as done
on hardware until it has — see the S4 handoff for the exact procedure.

### S3 — latest
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s3-editmode.xml -logFile /tmp/ghostmap-s3-tests.log
```
**266 tests, 266 passed, 0 failed, 0 skipped.** Unity exit code 0.
`CornerCaptureControllerTests` 37, `ScanWorkflowControllerTests` 31,
`GhostFrameTests` 20, `FloorLockControllerTests` 17, `ScannerXrSettingsTests` 4,
`ScannerSceneTests` 1, `GhostMap.Shared.Tests` 156. S2 finished at 212.

The new tests were mutation-checked rather than merely observed passing:

| Mutation | Result |
| --- | --- |
| Intersect at world `y = 0` instead of `frame.FloorWorldY` | 4 failed |
| Drop the whole-room `ValidateRoom` call | 3 failed — the wall-length, area and interior-angle tests |
| Stop forcing Ghost `y = 0` | **0 failed at first — the test was too weak** |

The third mutation survived the first attempt. At the fixture's scale — an eye
1.5 m up aiming a metre ahead — `origin + direction * t` rounds to exactly the
plane it was solved for, so the forcing changed nothing the assertions could see.
`GhostYIsForcedToZeroEvenWhenTheIntersectionCarriesResidue` aims from
progressively further out, where the roundings no longer cancel, and now fails
three of its four cases under that mutation with residues of `-5.96e-07`,
`6.08e-06` and `-2.44e-05` m.

`ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0, and
`xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported BUILD SUCCEEDED.

The shared package was also run standalone in its own host project, to confirm
S3 changed nothing under `shared/`:
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s3-final-shared.xml \
  -logFile /tmp/ghostmap-s3-final-shared.log
```
**156 tests, 156 passed, 0 failed, 0 skipped.** Unity exit code 0 — unchanged
from F4.

The S3 physical-device test was then run on a real iPhone and **passed** — see
the verification section above.

### S2 — previous
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s2-editmode.xml -logFile /tmp/ghostmap-s2-tests.log
```
**212 tests, 212 passed, 0 failed, 0 skipped.** Unity exit code 0.
`GhostFrameTests` 20, `FloorLockControllerTests` 17,
`ScanWorkflowControllerTests` 14, `ScannerSceneTests` 1,
`ScannerXrSettingsTests` 4, `GhostMap.Shared.Tests` 156.

The frame tests were mutation-checked rather than merely observed passing:
`right = Cross(forward, up)` fails 4 tests, and shifting the origin by
`-CameraYOffset` fails 8. The first attempt at the mirroring mutation caught only
2, because `RightMapsToPlusX` had built its expectation from `frame.Right` and
agreed with a wrong frame; it was rewritten to compute the expected world axis
independently and `FrameAxesMatchTheCameraYawTheLockWasTakenAt` was added.

`ScannerBuild.BuildScanner` succeeded, and
`xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported BUILD SUCCEEDED.

The S2 physical-device test was then run on a real iPhone and **passed** — see
the verification section above.

### S1
- `GhostMap.Scanner.EditModeTests` — 4/4 passed.
- `GhostMap.Shared.Tests` — 156/156 passed in the same run. 160 total, 0 failed,
  0 skipped.
- `ScannerBuild.ConfigureXr` + `ScannerBuild.BuildScanner` — clean build
  succeeded.
- `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO` — BUILD SUCCEEDED; the linked binary contains the
  ARKit native symbols.
- Physical-device smoke test on a real iPhone — **passed** (above).

## Root cause of the first device failure — no active XR loader
`UNITY_XR_ARKIT_LOADER_ENABLED` was never present in the iOS scripting define
symbols, so:

1. Nearly all of `com.unity.xr.arkit` compiled to stubs. `ARKitSessionSubsystem`
   and friends register their subsystem descriptors only after
   `Api.AtLeast11_0()`, which the stub build hard-codes to false, so
   `ARKitLoader.Initialize()` found no descriptors, returned false, and XR
   Management never promoted it to the active loader.
2. `ARKitBuildProcessor.loaderEnabled` stayed false, so its plugin-copy delegate
   excluded `libUnityARKit.a` and `UnityARKit.m` from the Xcode project
   entirely.

The XR settings themselves were always correct and always shipped — iOS
`XRGeneralSettings` with `InitManagerOnStart`, an `XRManagerSettings` with
`ARKitLoader` assigned, all reaching the player's preloaded assets. That is why
the failure looked like a loader that was configured but inert.

The define is normally added by the ARKit package itself from
`UnityEditor.XR.ARKit.LoaderEnabledCheck`, which returns immediately in batch
mode and, in the Editor, only runs on a polling coroutine some time after the
loader is assigned. Its batch-mode fallback inside `ARKitBuildProcessor` is
itself wrapped in `#if UNITY_XR_ARKIT_LOADER_ENABLED`, so it can never bootstrap
the define from nothing. The scanner's build script had been assigning the
loader from inside `BuildPlayer`, which is far too late regardless: a scripting
define only reaches compiled code on the next script compilation.

Fixed by making XR configuration its own step, `ScannerBuild.ConfigureXr`.

## Root cause of the second device failure — camera pose never written
`ProjectSettings.asset` had `activeInputHandler: 0` (Active Input Handling =
Input Manager (Old)), so `ENABLE_INPUT_SYSTEM` was never defined and the Input
System backend was absent from the player. AR Foundation drives the AR camera
with `UnityEngine.InputSystem.XR.TrackedPoseDriver` bound to
`<HandheldARInputDevice>/devicePosition` and `/deviceRotation`. With no backend
there is no such device, the driver's actions resolve to zero controls,
`ReadTrackingStateWithoutTrackingAction()` yields `TrackingStates.None`, and
`SetLocalTransform` writes neither position nor rotation — so the camera sits at
its authored local pose regardless of how well ARKit tracks.

Proven from the failing player's own IL2CPP output: `InputSystemProvider`'s
static constructor, whose only statement is inside `#if ENABLE_INPUT_SYSTEM`,
was compiled down to `{ return; }`.

Everything else in that chain was verified correct first — the Transform the
diagnostics read, the XR Origin → Camera Offset → Main Camera hierarchy,
`XROrigin.Camera`, and the ARCameraManager / ARCameraBackground /
TrackedPoseDriver components with their bindings. See
`docs/handoffs/2026-09-12-scanner-s1-camera-pose-not-driven.md` for the
link-by-link table.

Active Input Handling is now "Both", set by `ScannerBuild.ConfigureXr`, and
`BuildScanner` refuses to build without `ENABLE_INPUT_SYSTEM`.

## Xcode 26 link defect found behind the first fix
Once `libUnityARKit.a` was actually linked, the Xcode project failed to link with
undefined `__swift_FORCE_LOAD_$_swiftCompatibility*` symbols. The ARKit package
points the linker at the Swift compatibility shims via `$(TOOLCHAIN_DIR)`, but
Xcode 26.6 prepends the separately delivered Metal toolchain to `TOOLCHAINS`, so
that resolves to a toolchain with no Swift runtime. `ScannerIosPostBuild` now
adds the correct `XcodeDefault.xctoolchain` paths as `-L` flags on
`OTHER_LDFLAGS`. This had been invisible because the broken builds never linked
the ARKit library at all.

## Interfaces consumed
- Available now from the frozen shared package. The scanner will consume from
  `com.ghostmap.shared`: `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry`,
  `RoomValidator`, `OpeningValidator`, `FurnitureValidator`, the domain DTOs, and
  `ProtocolSerializer`.

## Known issues
All of the following are **non-blocking**. None of them prevented the S1
physical-device test from passing.

### Build process
- Building the scanner for iOS is a **two-step** process and cannot be collapsed
  into one: `ScannerBuild.ConfigureXr`, then `ScannerBuild.BuildScanner` in a
  separate Unity invocation. Both `UNITY_XR_ARKIT_LOADER_ENABLED` and
  `ENABLE_INPUT_SYSTEM` only reach compiled code on the next script compilation.
  In the interactive Editor, the Active Input Handling change from step 1 needs
  an Editor restart. `BuildScanner` fails loudly rather than shipping a broken
  player if this is skipped.
- The generated Xcode project has no signing team. It must be selected by hand
  before deploying to a device.
- `ScannerIosPostBuild` carries an Xcode 26 workaround for the Apple ARKit XR
  Plug-in's `$(TOOLCHAIN_DIR)`-based Swift library search paths. Re-check
  whether it is still needed on an ARKit package or Xcode upgrade; it is
  additive, so it is harmless if the upstream issue is fixed.

### Scene / runtime
- `XROrigin.m_CameraYOffset` stays at `1.1176`. No longer deferred — see the
  CameraYOffset section above. The frame is immune to it.
- `ScannerBootstrap`'s per-link diagnostics readout was built for S1 bring-up.
  It stays through S2-S6 device work and now shares the canvas with the S2
  floor-lock readout; replace both with the real capture UI when that exists.
- Together the two readouts fill much of the screen. This is bring-up
  instrumentation, not the capture UI.
- There is no reset. Establishing a new frame means relaunching the app. Reset
  starts a new AR session and session id, which the plan places later.
- `ISpatialProvider.TryGetFloorHit` returns a scanner-owned `FloorHit` rather
  than the `ARRaycastHit` the implementation plan sketches. `ARRaycastHit` needs
  a live `ARPlane` trackable and cannot be constructed in an EditMode test, which
  would have pushed every floor-lock rule onto a phone to verify.

### Scene / runtime — new in S3
- The screen is now crowded: three readouts and four buttons. Bring-up
  instrumentation, not the capture UI; Task S6 owns the real one.
- There is no AR marker for the aim point before capture, only the numeric
  `Aim G(...)` line. A live preview marker would help and belongs to S6; it was
  left out as a stretch feature.
- Corner markers are untextured spheres from `GameObject.CreatePrimitive` on the
  Built-in pipeline's default material. Legible, not designed.
- `ScanPhase.CaptureHeight` is reachable with nothing behind it. It is entered on
  an accepted closure because the plan's state machine says so; S4 fills it in.
- A corner can be undone but not edited. Fixing corner 2 means undoing 4 and 3
  first, or pressing Redo Corners. The plan asks for Undo, not editing.

### Scene / runtime — new in S4
- The screen is even more crowded now: four readouts, seven buttons and an
  input field. Bring-up instrumentation, not the capture UI; Task S6 owns the
  real one.
- Wall selection is text-and-line, not the "numbered walls in a simple
  top-down mini preview" implementation plan section 8.7 describes. A yellow
  world-space line on the selected wall plus the readout's `Wall N/4` cover
  the same need — which wall is selected, is unambiguous on device — without
  a rendered 2D floorplan. A real top-down preview is left for Task S6.
- The aim marker is an untextured sphere, matching S3's corner markers. It is
  green or red by validation state, not by whether the ray is on the physical
  wall versus past its edge; `withinWallSpan` is readout-only.
- The manual height fallback commits immediately on button press; there is no
  confirmation step. A mistyped value is only caught by the 2.0-4.0 m range
  check, not by asking the user to re-enter it.

### Not covered by the S4 EditMode tests, or by any device test yet
None of Task S4 has been run on a phone. Everything below is covered by
EditMode tests only:

- the real ray/plane math against a live AR camera and detected corners,
  rather than the fixture's synthetic rays;
- whether a phone-held aim genuinely lands within a wall's span in practice,
  versus the fixture's exact geometric placements;
- the manual-height fallback's on-screen keyboard behavior (the legacy
  `InputField`'s `DecimalNumber` content type has not been exercised on iOS
  hardware);
- the wall-selection cycle button under real touch input;
- a non-rectangular room's wall planes (only the S3 rectangular fixture was
  used).

### Not covered by the S3 device test
The S3 hardware run exercised a rectangular room with a good scan. These paths
are covered by EditMode tests but have **not** been seen on a phone:

- the `Acceptable` closure band (0.08-0.15 m) and the `Rejected` band
  (> 0.15 m), including the rejected path's refusal to advance and its
  Redo-Corners-only exit. Only `Excellent` (0.031 m) was observed;
- the area, wall-length, interior-angle and self-intersection refusals. Only the
  minimum-spacing refusal was provoked on device;
- capture blocked by degraded tracking (`TrackingNotGood`);
- `RayMissedFloor`, which needs the crosshair aimed at or above the horizon;
- a non-rectangular four-corner room.

None of these is suspected broken — each has a passing test, and two of the
three validation groups were mutation-checked. They are recorded because a
passing test is not the same evidence as a passing phone.

### Toolchain warnings, stock noise
- `ld: warning: search path '.../Metal.xctoolchain/usr/lib/swift*/iphoneos' not
  found` (twice per link). These are the ARKit package's own `$(TOOLCHAIN_DIR)`
  entries that the `OTHER_LDFLAGS` workaround above compensates for. The link
  succeeds.
- Unity reports `Packages/com.unity.xr.arkit/Tests/Editor/Assets/TestReferenceImageLibrary.asset`
  as "unexpectedly altered" in an immutable package. That is the ARKit package's
  own test asset in the package cache, not anything in this repository.
- Unity logs `The application 'build-server' does not exist. / No .NET SDKs were
  found.` at the end of batch-mode runs. No system .NET SDK is installed; builds
  and tests succeed regardless.
- The usual Xcode deprecation and `has no symbols` warnings from Unity's own
  trampoline sources.

## Next safe task
- **Physically verify S4 on a real iPhone.** The implementation, EditMode
  tests, mutation checks and iOS build are all done and passing — see "Tests
  run" and the S4 handoff's device procedure — but per `AGENTS.md` rule 12,
  real-device behavior must never be declared fixed until verified on
  hardware. **S5 must not start until that verification passes and this
  section is updated to reflect it**, exactly as S1-S3 required before the
  workstream moved on.
- Once S4 is verified: **S5** — doors, windows, furniture, per
  `docs/plans/ghostmap-implementation-plan.md`.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/viewer/**` — owned by the Viewer workstream.
