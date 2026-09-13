# Scanner Status

## Current state
- **S1 in progress — NOT complete.** The scanner Unity project, scene, build
  pipeline and device diagnostics exist, but the physical-device smoke test has
  not yet passed.
- `apps/scanner/` is a real Unity 6000.3.24f1 project: `Packages/manifest.json`
  (AR Foundation 6.3.1, Apple ARKit XR Plugin 6.3.1, `com.ghostmap.shared`),
  `ProjectSettings`, `Assets/XR` XR Plug-in Management configuration,
  `Assets/GhostMap/Scanner/Scanner.unity`, runtime bootstrap, editor build
  scripts, and EditMode tests.
- Two physical-iPhone tests have **failed**, each with a distinct root cause,
  both found and fixed. The project has been rebuilt cleanly after each, but
  neither fix is **verified on hardware**.
  1. `XR loader: none`, no active `XRRaycastSubsystem` / `XRInputSubsystem` —
     the ARKit loader define (below).
  2. ARKitLoader active and `ARWorldTrackingConfiguration` running, but the
     camera pose never changed when the phone moved or rotated — the Input
     System backend (below).

## Last verified commit
- None. No scanner work has been verified on a physical iPhone yet.

## Tests run
- `GhostMap.Scanner.EditModeTests` — 4 tests, all passing (see the S1 handoffs
  for the exact command and output).
- Editor iOS build via `ScannerBuild.ConfigureXr` + `ScannerBuild.BuildScanner`
  — succeeded. `xcodebuild` on the generated project reports BUILD SUCCEEDED and
  the linked binary contains the ARKit native symbols.
- **No passing physical-device test.**

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
- S1's physical-device smoke test has not passed. Nothing in this workstream may
  be declared working on device until it actually runs on a real iPhone.
- Building the scanner for iOS is a **two-step** process and cannot be collapsed
  into one. See "How to build" in the S1 handoffs. In the interactive Editor,
  the Active Input Handling change from step 1 needs an Editor restart.
- `XROrigin.m_CameraYOffset` is `1.1176` (AR Foundation's own factory default),
  which ARKit's Device tracking-origin mode applies to Camera Offset. Expect
  `Offset W` to read about `y = 1.12` on device. Not changed, not a new failure
  — decide it when S3 locks the floor.

## Next safe task
- **Run the S1 physical-device smoke test on a real iPhone.** Open
  `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`, select a signing team, run
  on device, and confirm the on-screen readout. Only then may S1 be marked
  complete and S2 started.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/viewer/**` — owned by the Viewer workstream.
