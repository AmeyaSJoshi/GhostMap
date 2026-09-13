# Handoff

## Branch
`scanner/s5-openings-furniture`

## Base commit
`9690151` (merge of PR #4, `scanner/s4-room-height-capture` into `main`)

## Head commit
`<filled in by the follow-up "docs(handoffs): record S5 implementation commit sha" commit>`

## What changed
- `Runtime/Capture/OpeningCaptureController.cs` (new): captures a door or
  window on one of the four S3/S4-derived walls by intersecting the
  center-screen ray with that wall's plane at a lower-left then an
  upper-right point, converting both to wall-local `u/v`, deriving
  offset/width/sill/height per implementation plan section 16, and
  validating through the shared `OpeningValidator`.
- `Runtime/Capture/ObjectPlacementController.cs` (new): places parametric
  furniture on the locked floor plane by intersecting the center-screen ray
  with it, applying `FurnitureValidator`'s MVP default dimensions, and
  letting width/depth/height/yaw be adjusted on the most recently placed
  object — every candidate and adjustment re-validated through the shared
  `FurnitureValidator`.
- `Runtime/UI/OpeningCaptureHud.cs`, `Runtime/UI/ObjectPlacementHud.cs`
  (new): the scene-facing shells — wall/type selection, two-point capture,
  furniture placement and stepper-based adjustment, plus world-space
  markers. Bring-up instrumentation, not the capture UI; Task S6 owns that.
- `Runtime/Workflow/ScanWorkflowController.cs`: holds the two new
  controllers, exposes them as `Openings`/`Objects`, adds the S5 API
  (`SelectOpeningWall`, `SetOpeningType`, `TryCaptureOpeningStartPoint`,
  `TryCaptureOpeningEndPoint`, `TryUndoLastOpening`, `FinishAddingOpenings`,
  `SetObjectType`, `TryPlaceObject`, `TryUndoLastObject`,
  `TrySetObjectWidth`/`Depth`/`Height`/`Yaw`, `FinishAddingObjects`), and
  writes real `openings`/`objects` arrays into every published snapshot
  instead of the placeholder empty arrays S2-S4 used.
- `Runtime/UI/FloorLockHud.cs`: constructs `OpeningCaptureController` and
  `ObjectPlacementController` alongside the existing controllers (it remains
  the scene's composition root) and passes them into
  `ScanWorkflowController`'s now five-argument constructor.
- `Editor/ScannerSceneBuilder.cs`: builds the S5 UI (wall/type/capture/undo/
  finish buttons and a readout for openings; type/place/undo/eight stepper
  buttons/finish and a readout for furniture) and extends `VerifyScene()` to
  assert `OpeningCaptureHud` and `ObjectPlacementHud` are present and fully
  wired.
- `Assets/GhostMap/Scanner/Scanner.unity`: regenerated via
  `GhostMap/Build Scanner Scene` to include the S5 GameObjects.
- New tests: `Tests/EditMode/OpeningCaptureControllerTests.cs` (20),
  `Tests/EditMode/ObjectPlacementControllerTests.cs` (17). Extended
  `Tests/EditMode/ScanWorkflowControllerTests.cs` (+10, constructor updated
  for the two new controllers) and `Tests/EditMode/ScannerSceneTests.cs`
  (renamed test method to mention S5).
- `docs/status/scanner.md`: S5 status, design notes, known issues, and the
  physical-device test procedure below.

## Contract impact
- None. No file under `shared/**`, `fixtures/**`, `tools/**`,
  `docs/contracts/**` or `docs/decisions/**` was touched, and no
  `apps/viewer/**` file was touched. `OpeningModel` and `SceneObjectModel`
  already existed in scene schema v1 with exactly this meaning; S5 is the
  first task to write non-empty `openings`/`objects` arrays into a live
  snapshot. `RoomModel.heightM` (S4's contribution) is what makes the
  opening ceiling rule (`sillHeightM + heightM <= room.heightM`) exercisable
  for the first time.

## How to test

### Automated (already run — see Test results)
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s5-final.xml -logFile /tmp/ghostmap-s5-final.log
```

### Physical device (NOT YET run — see `docs/status/scanner.md`'s
"Physical-device test procedure for S5" section for the full script)
1. Build with `ScannerBuild.ConfigureXr` then `ScannerBuild.BuildScanner`
   (two separate Unity invocations), deploy to a real iPhone.
2. Run S1-S4 as before through a captured height; `Phase` should read
   `AddOpenings`.
3. Capture at least one door and one window on two different walls; confirm
   the readout's offset/width/sill/height values look physically correct.
4. `Finish Openings` -> `Phase` becomes `AddObjects`. Place at least one
   piece of furniture, adjust its width/depth/height/yaw with the stepper
   buttons, and confirm the S2 frame readout and S3 corner markers do not
   move.
5. `Finish Objects` -> `Phase` becomes `ReadyToFinalize`, and
   `SceneSnapshot.finalized` stays `false`.
6. Deliberately test: an opening whose two points fall outside the selected
   wall, a window sill aimed below the floor, an opening above the captured
   ceiling, two overlapping openings on the same wall, and undoing the most
   recently placed object.

## Test results
- **339 tests, 339 passed, 0 failed, 0 skipped.** Unity exit code 0.
  Breakdown: `OpeningCaptureControllerTests` 20, `ObjectPlacementControllerTests`
  17, `ScanWorkflowControllerTests` 49 (39 + 10 new), plus all 253
  pre-existing Scanner/Shared tests unchanged. S4 finished at 292.
- Mutation-checked: flipping the door-height formula's `Max` to `Min` broke
  9 tests (everything built on an accepted door fixture); disabling
  `ObjectPlacementController.TryAdjust`'s validator call broke exactly the
  one test written to catch it.
- `ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0.
  `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO` reported **BUILD SUCCEEDED**.
- The shared package, run standalone in `shared/TestProject`: **156/156**,
  unchanged from S4 — confirms S5 touched nothing under `shared/`.
- **The physical-device test has NOT been run.** All of the above is
  Editor/EditMode evidence only. Per `AGENTS.md` rule 12, S5 must not be
  declared fixed or complete until it is verified on a real iPhone.

## Known failures
- None found. See `docs/status/scanner.md`'s "Scene / runtime — new in S5"
  for known UX/instrumentation limitations (screen crowding, step-based
  adjustment instead of typed/slider input, undo scoped to the most recent
  item only) — all deliberate MVP/bring-up trade-offs, not bugs.

## Files most important to read next
- `docs/status/scanner.md` — "Physical-device test procedure for S5" for the
  exact device-test script, and "Task S5 — openings and furniture" for the
  full design rationale.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/OpeningCaptureController.cs`
  and `.../ObjectPlacementController.cs` — the actual capture/validation
  logic.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Workflow/ScanWorkflowController.cs`
  — the S5 state-machine wiring.

## Next task
- Run the S5 physical-device test procedure on a real iPhone and report the
  results. Only then update `docs/status/scanner.md`'s S5 section to
  "complete and verified," add a "Physical-device verification — S5, passed"
  section, and close this branch.
- Do not begin Task S6 (scanner TCP client and the real capture UI) before
  that.
