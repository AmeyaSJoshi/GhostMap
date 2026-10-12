# Handoff

## Branch
`scanner/r9-hardening`, stacked on `shared/r5-detector-label`, which is on
`integration/r11-object-photos` (PR #35) and the rest of the stack.

## Base commit
`3a4001b` (R5 docs)

## Head commit
The commit that adds this file.

## What changed
Task `R9`, the parts an agent can do. Row by row in
`docs/tasks/R9-hardening.md`; in short:

- **Failure cases.** Poor tracking refuses every capture, now also adding a
  detected surface and Add All (bug: they did not check). After the app is
  backgrounded, `TrackingResumeGate` (in `ArSpatialProvider`) keeps tracking
  "lost" until ARKit has tracked well for 1 s, and the guide says "Welcome
  back...". A full turn with 0-2 walls offers Help GhostMap after 6 s with
  "Found 2 of 4 walls...". With a detector, the furniture step asks you to look
  around, then after 10 s points to placing by hand. Storage-full and
  share-cancel were already handled; now tested further.
- **Polish.** `S6 diag` removed; S1 diagnostics only in developer mode (Editor,
  Development builds, or five quick taps on Details). Six steps (`StepCount`,
  `StepNames`). Reticle 108 px with a dark halo (scene regenerated). One light
  haptic tap per frame in which the scan changed (`CaptureHaptics`,
  `IHaptics`, plugin `Assets/Plugins/iOS/GhostMapHaptics/GhostMapHaptics.mm`).
- **Demo mode works.** It reported no AR planes, so its automatic scan never
  finished. `SimulatedPlanes` (sharing `SimulatedLayout` with the demo room's
  meshes) reports walls, door and window frames, ceiling, floor and furniture
  tops, each once looked at.
- **Bugs found by running the demo in the Simulator, all fixed with
  regression tests:**
  1. Detected furniture was a quarter turn off (since `R1`): the yaw was the
     bearing of the plane's width axis, but a `SceneObjectModel`'s width runs a
     quarter turn from its yaw. **This affects real devices.**
  2. Detected doors and windows were dropped when ARKit did not see the
     ceiling; they are now added once the height is measured or typed.
  3. The Simulator (Debug) build did not link Core ML and Vision;
     `ScannerIosPostBuild` adds the plugin frameworks explicitly.

## Contract impact
None.

## How to test
1. `./tools/run_unity_tests.sh scanner`
2. Simulator demo: `ScannerSimulatorBuild.ConfigureForSimulator`, then
   `BuildForSimulator` (each with `-executeMethod`), `xcodebuild` the
   `Builds/iOSSimulator` project for an iPhone simulator, install, run. Lock
   Floor, Scan Room, drag round once, type 2.5, Next, look at the bed, Add
   All, Next, Finish. **Afterwards restore** `ProjectSettings/ProjectSettings.asset`
   and `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` (the Simulator
   configuration turns ARKit off).
3. iPhone (`R2`): every failure row in the brief.

## Test results
Owner's Mac, 2026-10-11: scanner EditMode **849/849** (shared 303 and viewer
580 unchanged). The quarter-turn and openings regression tests each fail with
their fix removed. The demo ran from Lock Floor to Finish in the Simulator
(iPhone 17); its zip passes `inspect_snapshot.py` and `room.html` shows the
4.20 × 3.60 m room and the labelled bed in Chrome.

## Known failures
None in the suites.

## Known limitations
- A second Simulator run with every fix reached step 3, then the Simulator
  stopped delivering taps to buttons (drags still worked), so the corrected
  bed and the door and window were not seen on screen. Both fixes are covered
  by regression tests that fail without them.
- Simulator taps sometimes need a second try right after a screen change.
- Haptics and the background gate only matter on a device; both are untested
  there.

## Files most important to read next
- `docs/tasks/R9-hardening.md`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/FurnitureDetectionController.cs` (`YawFromSurface`)
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/AR/SimulatedPlanes.cs`

## Next task
Owner: `R2` on the iPhone (the demo build is a good rehearsal). Agent: fixes
from `R2`.
