# Handoff

## Branch
`scanner/s1-unity-project-ar-smoke-test`

## Base commit
`4ece19e`

## Head commit
`<sha>`

## What changed

**Task S1 is complete and verified on a physical iPhone.** This handoff closes
S1. It supersedes nothing — the two earlier scanner handoffs record how each
device failure was diagnosed and remain the reference for those:

- `2026-09-12-scanner-s1-arkit-loader-root-cause.md`
- `2026-09-12-scanner-s1-camera-pose-not-driven.md`

### Delivered

- `apps/scanner/` — Unity 6000.3.24f1 project: `Packages/manifest.json`
  (AR Foundation 6.3.1, Apple ARKit XR Plugin 6.3.1, `com.ghostmap.shared`),
  `ProjectSettings`, and `Assets/XR` XR Plug-in Management configuration with
  the ARKit loader enabled for iOS.
- `Assets/GhostMap/Scanner/Scanner.unity` — the Task S1 scene:
  `AR Session` (ARSession + ARInputManager), `XR Origin` → `Camera Offset` →
  `Main Camera` (Camera, AudioListener, ARCameraManager, ARCameraBackground,
  TrackedPoseDriver), a diagnostics `Canvas`, and `ScannerBootstrap`.
  `ARPlaneManager` detection mode is Horizontal | Vertical; `ARRaycastManager`
  is present.
- `Assets/GhostMap/Scanner/Editor/` — `ScannerSceneBuilder` (regenerates the
  scene), `ScannerBuild` (two-step iOS build), `ScannerXrSettings`,
  `ScannerInputSettings`, `ScannerIosPostBuild`.
- `Assets/GhostMap/Scanner/Runtime/Bootstrap/ScannerBootstrap.cs` — the
  on-device readout.
- `Assets/GhostMap/Scanner/Tests/EditMode/` — 4 regression tests covering both
  device failures.

### Two device failures, both fixed

1. **No active XR loader.** `UNITY_XR_ARKIT_LOADER_ENABLED` was never in the iOS
   scripting defines, so the ARKit plug-in compiled to stubs that register no
   subsystem descriptors and `libUnityARKit.a` was excluded from the Xcode
   project. Fixed by making XR configuration its own build step.
2. **Camera pose never written.** Active Input Handling was "Input Manager
   (Old)", so `ENABLE_INPUT_SYSTEM` was undefined, no `<HandheldARInputDevice>`
   existed, and `TrackedPoseDriver.SetLocalTransform` wrote neither position nor
   rotation. Fixed by switching Active Input Handling to "Both".

A third defect surfaced behind the first: `libUnityARKit.a` contains Swift, and
the ARKit package locates the Swift compatibility shims via `$(TOOLCHAIN_DIR)`,
which Xcode 26.6 resolves to the separately delivered Metal toolchain. Fixed in
`ScannerIosPostBuild` with `-L$(DT_TOOLCHAIN_DIR)/...` on `OTHER_LDFLAGS`.

Each fix is guarded: `BuildScanner` refuses to build when either define is
missing from the compiled domain, verifies the whole iOS XR chain, and asserts
`libUnityARKit.a` reached the generated Xcode project.

## Contract impact
- None. S1 consumed no shared interfaces and changed none.

## How to build

Two Unity invocations. They cannot be collapsed into one: step 1 sets scripting
defines, and a define only takes effect on the next script compilation.

```bash
cd "<repo root>"

/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath apps/scanner \
  -buildTarget iOS \
  -executeMethod GhostMap.Scanner.Editor.ScannerBuild.ConfigureXr \
  -logFile /tmp/ghostmap-configure-xr.log

/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath apps/scanner \
  -buildTarget iOS \
  -executeMethod GhostMap.Scanner.Editor.ScannerBuild.BuildScanner \
  -logFile /tmp/ghostmap-build-scanner.log
```

In the Editor: `GhostMap > Configure Scanner XR (iOS)`, **restart the Editor**
(Active Input Handling requires it), then `GhostMap > Build Scanner (iOS)`.

`BuildScanner` deletes `apps/scanner/Builds/iOS` first, so every build is a full
export. Then open `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`, select a
signing team, and run on the device.

## How to test

### Automated

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath apps/scanner \
  -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-scanner-editmode.xml \
  -logFile /tmp/ghostmap-scanner-tests.log
```

### Physical device

Run on a real iPhone and read the on-screen readout top to bottom. The first
line that looks wrong names the broken link in the chain from ARKit to the
camera Transform.

## Test results

### Automated — run against this commit
- `GhostMap.Scanner.EditModeTests` — 4/4 passed:
  `ArKitLoaderDefineIsSetForIos`, `IosArKitConfigurationIsBuildable`,
  `InputSystemBackendIsEnabledForTheProject`, `InputSystemBackendIsCompiledIn`.
- `GhostMap.Shared.Tests` — 156/156 passed in the same run.
- Totals: 160 tests, 160 passed, 0 failed, 0 skipped.
- `xcodebuild -project Unity-iPhone.xcodeproj -target Unity-iPhone
  -configuration Release -sdk iphoneos CODE_SIGNING_ALLOWED=NO` — BUILD
  SUCCEEDED. The linked `UnityFramework` contains 1046 `UnityARKit_*` symbols
  and links `/System/Library/Frameworks/ARKit.framework/ARKit`.

### Physical device — PASSED
Verified on a real iPhone against the build produced from `80b5a48`, whose
scanner sources are byte-identical to this commit's:

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

That covers every item of the Task S1 physical-device test in
`docs/plans/ghostmap-implementation-plan.md`.

## Known failures
- None. S1 passes.

## Known issues — all non-blocking
See `docs/status/scanner.md` for the full list with detail. In short:

- The iOS build is a two-step process and the Editor needs a restart after
  step 1. `BuildScanner` fails loudly if this is skipped.
- The generated Xcode project has no signing team.
- `ScannerIosPostBuild` carries an Xcode 26 Swift-search-path workaround for the
  ARKit package; re-check it on an ARKit or Xcode upgrade.
- `XROrigin.m_CameraYOffset` is `1.1176` (AR Foundation's factory default) and
  ARKit's Device tracking-origin mode applies it to Camera Offset, so camera
  world Y carries a ~1.12 m offset. Left alone deliberately — **decide it when
  S3 locks the floor.**
- `ScannerBootstrap`'s diagnostics readout is bring-up instrumentation. Keep it
  through the S2-S6 device work, replace it when the real capture UI exists.
- Stock toolchain noise: two `ld: warning: search path '.../Metal.xctoolchain/...'
  not found` per link (the entries the workaround compensates for), Unity's
  "unexpectedly altered" warning about the ARKit package's own test asset, a
  missing-.NET-SDK `build-server` message at the end of batch runs, and the
  usual Xcode deprecation warnings from Unity's trampoline.

## Files most important to read next
- `docs/plans/ghostmap-implementation-plan.md` — Task S2
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Bootstrap/ScannerBootstrap.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerBuild.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Scanner.unity`
- `docs/status/scanner.md`

## Next task
- **S2.** Do not start it in this handoff's scope — S1 is closed here.
