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
- A first physical-iPhone test **failed**: the app reported `XR loader: none`,
  `Session: None`, and Xcode logged no active `XRRaycastSubsystem` and no active
  `XRInputSubsystem`. The root cause has been found and fixed (below), and the
  project has been rebuilt cleanly, but the fix is **not yet verified on
  hardware**.

## Last verified commit
- None. No scanner work has been verified on a physical iPhone yet.

## Tests run
- `GhostMap.Scanner.EditModeTests` — 2 tests, both passing (see the S1 handoff
  for the exact command and output).
- Editor iOS build via `ScannerBuild.ConfigureXr` + `ScannerBuild.BuildScanner`
  — succeeded. `xcodebuild` on the generated project reports BUILD SUCCEEDED and
  the linked binary contains the ARKit native symbols.
- **No passing physical-device test.**

## Root cause of the first device failure
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

## Second defect found behind the first
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
  into one. See "How to build" in the S1 handoff.

## Next safe task
- **Run the S1 physical-device smoke test on a real iPhone.** Open
  `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`, select a signing team, run
  on device, and confirm the on-screen readout. Only then may S1 be marked
  complete and S2 started.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/viewer/**` — owned by the Viewer workstream.
