# Handoff

## Branch
`scanner/s1-unity-project-ar-smoke-test`

## Base commit
`4ece19e`

## Head commit
`2c01928`

## What changed

### The bug

S1's first physical-iPhone test failed. The device showed:

```text
XR loader: none
Session: None
Not tracking reason: None
```

and Xcode logged no active `XRRaycastSubsystem` and no active
`XRInputSubsystem`. A previous session guessed this was a stale incremental iOS
export; a physical retest after a clean rebuild disproved that.

### Root cause

`UNITY_XR_ARKIT_LOADER_ENABLED` was never present in the iOS scripting define
symbols. `apps/scanner/ProjectSettings/ProjectSettings.asset` had
`scriptingDefineSymbols: {}`.

Everything else in the XR chain was already correct and was verified directly
against the failing artifact:

| Checked | Result |
| --- | --- |
| iOS `XRGeneralSettings` exists | yes — `Keys: 04000000` (`BuildTargetGroup.iOS`) in `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` |
| `XRManagerSettings` exists | yes — "iPhone Providers" |
| Initialize XR on Startup | yes — `m_InitManagerOnStart: 1` |
| Configured loader list contains ARKitLoader | yes — loader guid `78fbf8860867d46a491ccfe99ec7944f` matches `Assets/XR/Loaders/ARKitLoader.asset.meta` |
| ARKitLoader asset reference valid | yes |
| Registered with XR Management | yes — `com.unity.xr.management.loader_settings` in `ProjectSettings/EditorBuildSettings.asset` points at the settings asset |
| The build actually contained the XR config | yes — "iPhone Settings", "iPhone Providers" and `ARKitLoader` were all present in `Builds/iOS/Data/globalgamemanagers.assets` |
| AR Foundation / ARKit versions compatible | yes — both 6.3.1, ARKit 6.3.1 requires AR Foundation 6.3.1 |
| Xcode version | Xcode 26.6, above the ARKit 6.3.1 minimum of 26 |

Without the define, two things happened:

1. **The managed plug-in shipped as stubs.** The failing build's IL2CPP output
   contained `NativeApi_UnityARKit_Session_IsSupported(...) { return (bool)0; }`.
   Each `ARKit*Subsystem.RegisterDescriptor` returns early unless
   `Api.AtLeast11_0()`, which the stub build hard-codes to false, so no
   descriptor was ever registered. `ARKitLoader.Initialize()` therefore created
   no session subsystem, returned false, and XR Management never promoted it
   from *configured* to *active*.
2. **The native library was excluded from the Xcode project.**
   `ARKitBuildProcessor.loaderEnabled` stayed false, so its
   `ShouldIncludeRuntimePluginsInBuild` delegate returned false. The failing
   `Builds/iOS` contained no `libUnityARKit.a` and no `UnityARKit.m`, and the
   `.pbxproj` referenced neither.

So the loader was configured and shipped, and was simply incapable of
initializing. That is exactly the "`[ARKitLoader]`, active loader: none" case.

Why the define was missing: the ARKit package adds it from
`UnityEditor.XR.ARKit.LoaderEnabledCheck`, whose static constructor returns
immediately when `Application.isBatchMode`, and which otherwise only runs on a
0.25s editor coroutine — i.e. some time *after* the loader is assigned. Its
batch-mode fallback lives inside `ARKitBuildProcessor.Preprocessor.PreprocessBuild`,
which is itself wrapped in `#if UNITY_XR_ARKIT_LOADER_ENABLED`, so it can never
bootstrap the define from nothing. On top of that, `ScannerBuild.BuildScanner`
was calling `ScannerXrSettings.EnableArKitLoaderForIos()` from inside
`BuildPlayer`. A scripting define only reaches compiled code on the next script
compilation, so configuring XR during a build can never affect the assemblies
that build is producing — the build would have been wrong even if the package's
own hook had fired.

### The fix

- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerXrSettings.cs`
  - `EnableArKitLoaderForIos()` now also writes `UNITY_XR_ARKIT_LOADER_ENABLED`
    into the iOS scripting define symbols rather than waiting for the package's
    interactive-only coroutine, sets `InitManagerOnStart` explicitly, and
    returns whether the defines changed.
  - New `VerifyIosArKitConfiguration()` throws a `BuildFailedException` naming
    the exact problem when the iOS settings, manager, loader list, loader asset
    reference, `automaticLoading`, `InitManagerOnStart`, or the define are wrong.
  - New `HasArKitLoaderDefine()`.
- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerBuild.cs`
  - Split into two entry points. `ConfigureXr` (menu:
    `GhostMap/Configure Scanner XR (iOS)`) switches the active build target to
    iOS and enables the loader. `BuildScanner` builds.
  - `BuildScanner` refuses to run when `UNITY_XR_ARKIT_LOADER_ENABLED` was not
    defined at compile time — that branch surviving compilation is itself the
    proof — and when the active build target is not iOS.
  - After a successful build it asserts `libUnityARKit.a` is present in the
    generated Xcode project, because the package decides that from its own
    internal state and a wrong decision is otherwise invisible until a device
    test.
  - Sets `PlayerSettings.iOS.cameraUsageDescription` (the ARKit build processor
    fails the build without it, once it is no longer compiled out) and pins the
    iOS architecture to ARM64.
- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerIosPostBuild.cs`
  - `CameraUsageDescription` is now `internal` so `ScannerBuild` reuses the same
    string. Corrected a stale comment about the ARKit post-processor.
  - Adds `-L$(DT_TOOLCHAIN_DIR)/usr/lib/swift/$(PLATFORM_NAME)` and the
    `swift-5.0` variant to `OTHER_LDFLAGS` on both Xcode targets. See the second
    defect below.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Bootstrap/ScannerBootstrap.cs`
  - The on-device readout now separates the three failure modes that all
    previously read as `XR loader: none`:

    ```text
    XR settings: present (init on start: yes, init complete: yes)
    Configured loaders: [ARKitLoader]
    Active loader: ARKitLoader
    ARKit native: compiled in
    ```

    `XR settings: not in build`, `Configured loaders: []`, and
    `ARKit native: MISSING (UNITY_XR_ARKIT_LOADER_ENABLED not defined)` are now
    each distinguishable at a glance.
- `apps/scanner/Assets/GhostMap/Scanner/Tests/EditMode/ScannerXrSettingsTests.cs`
  and its asmdef — regression tests asserting the committed iOS XR configuration
  is buildable and that the define is set.

### Second defect, uncovered by the first fix

Once `libUnityARKit.a` was actually being linked, the Xcode project failed to
link:

```text
Undefined symbols for architecture arm64:
  "__swift_FORCE_LOAD_$_swiftCompatibility51", referenced from:
      __swift_FORCE_LOAD_$_swiftCompatibility51_$_UnityARKit in libUnityARKit.a[49](RoomCaptureSessionWrapper.o)
  ... swiftCompatibility56, swiftCompatibilityConcurrency
```

`libUnityARKit.a` contains Swift (`RoomCaptureSessionWrapper`), so it pulls in
the Swift compatibility shims. The ARKit package's post-processor points the
linker at them with `$(TOOLCHAIN_DIR)/usr/lib/swift/$(PLATFORM_NAME)`. Under
Xcode 26.6 the Metal toolchain is delivered separately and Xcode prepends it to
`TOOLCHAINS`:

```text
TOOLCHAINS=com.apple.dt.toolchain.Metal.32023.883 com.apple.dt.toolchain.XcodeDefault
```

so `$(TOOLCHAIN_DIR)` resolves to the Metal toolchain cryptex, which ships no
Swift runtime, and the search path does not exist. The archives are in
`$(DT_TOOLCHAIN_DIR)/usr/lib/swift/$(PLATFORM_NAME)`, i.e. under
`XcodeDefault.xctoolchain`.

Xcode rejects `DT_TOOLCHAIN_DIR` inside `LIBRARY_SEARCH_PATHS`
(`error: DT_TOOLCHAIN_DIR cannot be used to evaluate LIBRARY_SEARCH_PATHS, use
TOOLCHAIN_DIR instead`), so the paths are added as plain `-L` flags on
`OTHER_LDFLAGS` instead. They are added alongside the package's own entries, not
in place of them, so a duplicate `-L` is a harmless no-op if a later Xcode or
ARKit release resolves `$(TOOLCHAIN_DIR)` correctly.

This was never visible before, because the failing builds did not link
`libUnityARKit.a` at all.

No change to `shared/**`, `fixtures/**`, `docs/contracts/**`, or `apps/viewer/**`.

## Contract impact
- None.

## How to build

Two Unity invocations. They cannot be collapsed into one: the first sets a
scripting define, and a scripting define only takes effect on the next script
compilation.

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

In the Editor, the same two steps are `GhostMap > Configure Scanner XR (iOS)`
(wait for the recompile to finish) then `GhostMap > Build Scanner (iOS)`.

`BuildScanner` deletes `apps/scanner/Builds/iOS` first, so every build is a full
export.

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

### Physical device — S1 smoke test, still outstanding

1. Open `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`.
2. Select a signing team on the `Unity-iPhone` target.
3. Run on a real iPhone.
4. Confirm on screen:
   - `Configured loaders: [ARKitLoader]`
   - `Active loader: ARKitLoader`
   - `ARKit native: compiled in`
   - `Session:` reaches `SessionTracking`
   - `Not tracking reason: None` once tracking
   - camera feed visible behind the readout
   - camera pos/rot change as the phone moves
   - `Planes:` climbs above 0 with `floor candidate: yes` after sweeping the floor
5. Confirm the Xcode console no longer reports a missing `XRRaycastSubsystem` or
   `XRInputSubsystem`.

## Test results
- `GhostMap.Scanner.EditModeTests` — 2/2 passed
  (`ArKitLoaderDefineIsSetForIos`, `IosArKitConfigurationIsBuildable`).
- `GhostMap.Shared.Tests` — 156/156 passed in the same run. 158 total, 0 failed.
- `ScannerBuild.ConfigureXr` — succeeded; `ProjectSettings.asset` now has
  `scriptingDefineSymbols: { iPhone: UNITY_XR_ARKIT_LOADER_ENABLED }`.
- `ScannerBuild.BuildScanner` — succeeded, including the new
  `libUnityARKit.a` assertion.
- Artifact checks on the new `Builds/iOS`:
  - `Libraries/com.unity.xr.arkit/Runtime/iOS/Xcode2600/libUnityARKit.a` and
    `Libraries/com.unity.xr.arkit/Runtime/iOS/UnityARKit.m` present, arm64,
    exporting `UnityARKit_Session_IsSupported` and `UnityARKit_Raycast_Construct`,
    and referenced from the `.pbxproj`. All were absent from the failing build.
  - IL2CPP output now emits a real P/Invoke to `UnityARKit_Session_IsSupported`
    instead of `return (bool)0`.
  - `Info.plist` has `UIRequiredDeviceCapabilities = (arm64, metal, arkit)`,
    `NSCameraUsageDescription`, `NSLocalNetworkUsageDescription`.
- `xcodebuild -project Unity-iPhone.xcodeproj -target Unity-iPhone -configuration
  Release -sdk iphoneos CODE_SIGNING_ALLOWED=NO` — **BUILD SUCCEEDED**. The
  linked `UnityFramework` contains 1046 `UnityARKit_*` symbols and links
  `/System/Library/Frameworks/ARKit.framework/ARKit`; `GhostMapScanner.app` is
  arm64. The same command against the pre-fix project failed to link. The
  unsigned `Builds/iOS/build/` output from this check was deleted afterwards.
- **No physical-device test has been run against this fix.**

## Known failures
- S1 is **not complete**. The physical-iPhone smoke test has not been run
  against the fixed build. Nothing here may be recorded as working on device
  until it is.
- The generated Xcode project has no signing team; it must be selected by hand
  before deploying.

## Files most important to read next
- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerXrSettings.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerBuild.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Bootstrap/ScannerBootstrap.cs`
- `docs/plans/ghostmap-implementation-plan.md` — Task S1
- `docs/status/scanner.md`

## Next task
- Run the S1 physical-device smoke test on a real iPhone. If it passes, mark S1
  complete in `docs/status/scanner.md`, record the verified commit, and start S2.
  If it fails, capture the new on-screen readout — it now names which link in
  the XR chain broke — before changing anything.
