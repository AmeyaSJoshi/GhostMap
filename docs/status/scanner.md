# Scanner Status

## Current state
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
- `b0fe6f0` — S2, verified on a real iPhone. `4dd4c13` follows it and changed
  only a handoff document, so its scanner sources are byte-identical.
- `80b5a48` — S1, verified on a real iPhone.
- S1 reached `main` as merge commit `150512d` (PR #1), merged with a merge
  commit so the original S1 SHAs stay reachable.

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

### S2 — latest
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
- **S3** — four-corner capture and closure verification. S2 is complete and
  verified on hardware, so the scanner workstream may proceed to the next task in
  `docs/plans/ghostmap-implementation-plan.md`. S3 consumes the locked frame and
  `RayPlaneMath.TryIntersectHorizontalPlane`; both are in place and tested.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/viewer/**` — owned by the Viewer workstream.
