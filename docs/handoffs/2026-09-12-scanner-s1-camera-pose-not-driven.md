# Handoff

## Branch
`scanner/s1-unity-project-ar-smoke-test`

## Base commit
`6b3b44c`

## Head commit
`<sha>`

## What changed

### The bug

After the ARKit loader fix, the second physical-iPhone test showed ARKitLoader
active and `ARWorldTrackingConfiguration` running, but neither the camera
position nor the camera rotation changed at all when the phone was moved or
rotated.

### Root cause

`apps/scanner/ProjectSettings/ProjectSettings.asset` had
`activeInputHandler: 0` — Active Input Handling = **Input Manager (Old)**. The
Input System backend was off, so `ENABLE_INPUT_SYSTEM` was never defined.

### The chain, traced end to end

Each link was checked against the scene asset and the failing build, not
assumed.

| # | Link | Result |
| --- | --- | --- |
| 1 | Transform used for the diagnostics | `ScannerBootstrap.arCamera = {fileID: 474861929}` — the `Camera` on **Main Camera**. `arCamera.transform` is the real tracked Transform, not a proxy. **OK** |
| 2 | Hierarchy | `XR Origin` (GO 665069327) → `Camera Offset` (GO 181133470) → `Main Camera` (GO 474861928), confirmed via `m_Father`/`m_Children`. All `m_IsActive: 1`. **OK** |
| 3 | `XROrigin.Camera` | `m_Camera: {fileID: 474861929}` = the actual Main Camera; `m_CameraFloorOffsetObject: {fileID: 181133470}` = Camera Offset. **OK** |
| 4 | Main Camera components | Transform, Camera, AudioListener, ARCameraManager, ARCameraBackground, TrackedPoseDriver — all `m_Enabled: 1`. **OK** |
| 5 | `ARCameraManager` | on Main Camera, enabled. **OK** |
| 6 | `ARCameraBackground` | on Main Camera, enabled. **OK** |
| 7 | Tracked-pose path | `UnityEngine.InputSystem.XR.TrackedPoseDriver`, enabled, `m_TrackingType: 0` (Rotation And Position), `m_UpdateType: 0` (Update And Before Render), bound to `<XRHMD>/centerEyePosition` + `<HandheldARInputDevice>/devicePosition` and the rotation equivalents. The empty `m_TrackingStateInput` binding is *not* a defect: `ReadTrackingState()` falls through to `ReadTrackingStateWithoutTrackingAction()`, which derives the state from whether the controls resolved. **OK** |
| 8 | **Input System backend** | **BROKEN.** `activeInputHandler: 0`. |

Proof that the backend was off in the shipped player, from its own IL2CPP
output — `InputSystemProvider`'s static constructor, whose only statement lives
inside `#if ENABLE_INPUT_SYSTEM`:

```cpp
IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void InputSystemProvider__cctor_...(...)
{
	{
		return;
	}
}
```

It was compiled out. After the fix the same function calls
`EventProvider.SetInputSystemProvider(...)`.

What that caused, following `TrackedPoseDriver`:

1. No Input System backend → the native layer reports no input devices, so the
   `HandheldARInputDevice` layout that `UnityEngine.XR.ARKit.InputLayoutLoader`
   registers is never matched to a device.
2. `hasResolvedPositionInputControl` and `hasResolvedRotationInputControl` are
   both false.
3. `ReadTrackingStateWithoutTrackingAction()` therefore sets
   `m_CurrentTrackingState = TrackingStates.None`.
4. In `SetLocalTransform`, `positionValid` and `rotationValid` are both false,
   so under `TrackingType.RotationAndPosition` **neither** branch runs and the
   Transform is never written.
5. Main Camera stays at its authored local pose — `(0,0,0)`, identity — forever,
   no matter how well ARKit tracks underneath.

This is upstream of items 1-7 and fully explains "no position change, no
rotation change" while ARKit itself is healthy.

### The fix

- New `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerInputSettings.cs`.
  `EnableInputSystemBackend()` sets Active Input Handling to **Both** (2) — the
  new backend is what AR Foundation needs, and keeping the legacy one leaves
  everything else untouched. `VerifyInputSystemBackendEnabled()` throws when it
  is off.
- `ScannerBuild.ConfigureXr` now also enables the input backend. Same two-step
  reason as the ARKit define: `ENABLE_INPUT_SYSTEM` is a built-in define that
  only reaches compiled code on the next script compilation.
- `ScannerBuild.BuildScanner`'s compile-time guard now covers both defines and
  names whichever is missing, so a build cannot silently ship a player whose AR
  camera tracking can never move.
- Two regression tests in `ScannerXrSettingsTests`.

No change to the scene asset, to `shared/**`, `fixtures/**`,
`docs/contracts/**`, or `apps/viewer/**`.

### Diagnostics added

`ScannerBootstrap` now prints each link separately, so the first wrong line is
the break:

```text
XR settings: present (init on start: yes, init complete: yes)
Configured loaders: [ARKitLoader]
Active loader: ARKitLoader
ARKit native: in | InputSys: ON
Session: SessionTracking | reason: None
XRInput: running
Head node: tracked p(0.03,-0.01,0.12)
Origin W: p(0.00,0.00,0.00) r(0,0,0)
Offset W: p(0.00,1.12,0.00) r(0,0,0)
Cam L: p(0.03,-0.01,0.12) r(12,87,2)
Cam W: p(0.03,1.11,0.12) r(12,87,2)
Cam moved: 431 frames (max dp 0.041m, dr 3.2deg)
Planes: 2 (floor candidate: yes)
```

Two of these are deliberately redundant with each other, and that redundancy is
the point:

- **Head node** is read through `InputTracking.GetNodeStates()`, straight from
  the XRInputSubsystem, bypassing the Input System package entirely. A tracked
  head node with a changing pose *next to* a frozen `Cam L` would have isolated
  the failure to the Input System layer immediately.
- **Cam moved** counts the frames in which the camera's local pose actually
  changed and keeps the largest position and rotation deltas seen. This answers
  "is the Transform really changing?" as a measurement rather than as something
  to be eyeballed from a formatted string. A frozen camera reports
  `0 frames (max dp 0.000m, dr 0.0deg)` however hard the phone is waved.

## Contract impact
- None.

## How to build

Unchanged, and still two Unity invocations — see
`2026-09-12-scanner-s1-arkit-loader-root-cause.md`. `ConfigureXr` now also flips
Active Input Handling. **In the interactive Editor, that setting needs an Editor
restart to take effect**; in batch mode the second invocation is a fresh process,
so nothing extra is needed.

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

1. Open `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`, select a signing team,
   run on a real iPhone.
2. Confirm, reading the readout top to bottom:
   - `Active loader: ARKitLoader`, `ARKit native: in`, `InputSys: ON`
   - `Session: SessionTracking`, `reason: None`
   - `XRInput: running`
   - `Head node: tracked`, and its numbers change as the phone moves
   - `Cam L` changes when the phone is moved **and** when it is rotated
   - `Cam moved:` climbs and the max deltas are non-zero
   - camera feed visible behind the readout
   - `Planes:` climbs above 0 with `floor candidate: yes` after sweeping the floor

If it fails again, the first line that looks wrong names the broken link —
capture the whole readout before changing anything.

## Test results
- `GhostMap.Scanner.EditModeTests` — 4/4 passed
  (`ArKitLoaderDefineIsSetForIos`, `IosArKitConfigurationIsBuildable`,
  `InputSystemBackendIsEnabledForTheProject`, `InputSystemBackendIsCompiledIn`).
- `GhostMap.Shared.Tests` — 156/156 passed. 160 total, 0 failed.
- `ScannerBuild.ConfigureXr` — succeeded; `ProjectSettings.asset` now has
  `activeInputHandler: 2`.
- `ScannerBuild.BuildScanner` — clean rebuild succeeded.
- Artifact checks on the new `Builds/iOS`:
  - `InputSystemProvider__cctor` now calls
    `EventProvider.SetInputSystemProvider(...)`; it was `{ return; }` before.
  - `UnityEngine.XR.ARKit.InputLayoutLoader.RegisterLayouts` is in
    `Data/RuntimeInitializeOnLoads.json` and its IL2CPP body registers the
    `HandheldARInputDevice` layout.
  - `TrackedPoseDriver.SetLocalTransform` is compiled in, not stripped.
  - `libUnityARKit.a` still present.
- `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO` — **BUILD SUCCEEDED**. The unsigned
  `Builds/iOS/build/` output was deleted afterwards.
- **No physical-device test has been run against this fix.**

## Known failures
- S1 is **not complete**. The physical-iPhone smoke test has not been run
  against this build.
- The generated Xcode project has no signing team; it must be selected by hand.

## Open question for the retest, not a defect
`XROrigin.m_CameraYOffset` is `1.1176` with
`m_RequestedTrackingOriginMode: 0` (NotSpecified). ARKit reports
`TrackingOriginModeFlags.Device`, and `XROrigin.MoveOffsetHeight()` applies
`m_CameraYOffset` to Camera Offset in Device mode, so `Offset W` should read
about `y = 1.12` and every camera world Y will carry that offset.

This was **not** changed: it is exactly what AR Foundation's own
`XROriginCreateUtil.CreateXROriginWithParent` produces — that factory never sets
`CameraYOffset`, so the `XROrigin` default stands — and changing it is unrelated
to the frozen-pose fix. The diagnostics report `Offset W` separately so the
value is visible rather than folded silently into `Cam W`. Decide it when S3
locks the floor; if `Offset W` reads `1.12` on device, that is expected here and
not a new failure.

## Files most important to read next
- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerInputSettings.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Bootstrap/ScannerBootstrap.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Editor/ScannerBuild.cs`
- `docs/handoffs/2026-09-12-scanner-s1-arkit-loader-root-cause.md`
- `docs/status/scanner.md`

## Next task
- Run the S1 physical-device smoke test on a real iPhone. If it passes, mark S1
  complete in `docs/status/scanner.md`, record the verified commit, and start S2.
