# Handoff

## Branch
`integration/sweep-and-furniture`

## Base commit
`f5a7d30` (merge of PR #13), via both feature branches.

## Head commit
`74888a6`

## What changed

**Combines `ADR-0005` wall sweeping and `ADR-0006` furniture detection + glTF
export into one branch**, so there is one compile, one scene regeneration and
one device session instead of three.

This handoff covers only the **merge and the integration seam**. The two
features are documented in full in their own handoffs, which remain accurate:

- `docs/handoffs/2026-10-03-scanner-sweep-wall-capture.md`
- `docs/handoffs/2026-10-03-integration-furniture-detect-and-export.md`

Read both. Nothing in them is superseded.

## The integration seam, and why it needed its own tests

Detection's "is this surface inside the room" gate reads the **corner store**,
via `RoomGeometry.ContainsPointXZ` over `CornerCaptureController.Corners`, and
requires `corners.IsComplete`.

On the swept path, corners are **never captured directly**. They are derived by
intersecting four fitted wall lines and installed through
`CornerCaptureController.TryAdoptDerivedCorners`. So detection depends on a
store that the two capture paths fill by completely different routes, and
neither feature branch's own tests exercised that combination.

It works because both features were deliberately built around the **same single
corner store and the same single object store** — that was the central design
choice in both ADRs, and this is where it pays off. But until this branch it
was a design intention rather than an asserted fact.

`ScanWorkflowSweepAndDetectionTests` (10 tests) now asserts it:

| Test | What it pins down |
| --- | --- |
| `SweptRoomAndDetectedFurniture_CompleteOneScanTogether` | The full no-typing scan: room captured by turning, furniture found by measuring, finalized, and the result passes `RoomValidator` and `FurnitureValidator`. Not one corner or dimension entered by hand. |
| `DetectionInRoomGate_WorksAgainstASweptFootprint` | The gate that could have silently broken on merge: a surface outside a **derived** footprint is rejected, one inside is kept. |
| `SweptCorners_PopulateTheStoreDetectionReads` | Asserts the plumbing directly rather than inferring it from the gate. |
| `Detection_IsRefusedWhileStillSweeping` | No footprint yet, so no detection — and the phase gate agrees. |
| `WalkedFallback_AlsoReachesWorkingDetection` | `ADR-0005`'s fallback arrives at detection in the same state the swept path does, so it is a whole fallback and not half of one. |
| `Revision_IsStrictlyMonotonicAcrossSweepingAndDetecting` | Two features now publish into one counter, and protocol v1 obliges the viewer to drop anything non-monotonic. Also asserts a detection refresh publishes nothing. |
| `RedoingSweeps_LeavesDetectionWithoutAStaleFootprint` | `RedoWallSweeps` clears the corner store; detection must refuse rather than use a footprint that no longer exists. |
| `ReSweptRoom_GatesDetectionAgainstTheNewFootprint` | A point inside the original 4x3 room but outside a re-swept 2x2 one is rejected. |
| `MergedWorkflow_ExposesBothNewControllers` | Both new collaborators are live instances sharing one frame, not nulls that would only fail on a phone. |

## How the conflicts were resolved

Seven, all by taking both sides:

| File | Resolution |
| --- | --- |
| `ScanWorkflowController.cs` | Both usings needed — `UnityEngine` for the sweep's `Vector3[]`, `UnityEngine.XR.ARSubsystems` for detection's `TrackableId`. Constructor now takes seven collaborators. Transition table is the sweep branch's, unchanged, because detection added no phase. |
| `FloorLockHud.cs` | **The load-bearing one.** The composition root builds both controllers, and `furnitureDetection` is constructed against `cornerCapture` — the same store the swept path adopts into. The seam works by construction here. |
| `ScannerSceneBuilder.cs` | Both readout factories were inserted at the same point, so git saw one where two were needed. Both kept. Both HUDs built, both asserted by `VerifyScene`. |
| `ScanWorkflowControllerTests.cs`, `ScannerSnapshotPublisherTests.cs` | Both factory locals and both constructor arguments. |
| `docs/handoffs/README.md`, `docs/status/scanner.md` | Both rows / both entries, with branch names unified. |

### The screen does not collide

Worth stating because both features claimed to reuse an existing row:

- sweep controls → S3 button row (`240`) and the corner readout area (`690`);
- detection controls → S5 openings rows (`1330`, `1440`) and the openings
  readout area (`1500`).

`SweepWalls`/`CaptureCorners` are mutually exclusive, and
`AddOpenings`/`AddObjects` are mutually exclusive, and every one of these HUDs
blanks itself outside its own phase. So in any single phase exactly one of each
pair is visible and nothing overlaps. During `AddObjects` the detection row
(`1330`/`1440`) and the object row (`1710`+) are both live, which fits, but it
is the fullest the screen gets.

## Contract impact

**None beyond what the two ADRs already recorded.** No schema change, no
protocol change, no viewer contract change. One new `ScanPhase` value
(`SweepWalls`, from `ADR-0005`); detection added none. Two additive shared
geometry additions (`WallFitting`, `RoomGeometry.ContainsPointXZ`).

## How to test

**Steps 1 and 2 first.** Both scene builders changed on this branch.

1. Regenerate the scanner scene: **GhostMap → Build Scanner Scene**. Commit it.
2. Regenerate the viewer scene via `ViewerSceneBuilder`. Commit it.
3. Shared suite: expect 156 + 32 (`WallFitting`) + 9 (`ContainsPointXZ`) = **197**.
4. Scanner suite: 361 existing + 57 sweep + 42 detection + 10 integration.
   Reconcile against the XML rather than trusting arithmetic — the scanner count
   includes the embedded shared tests, so it moves when the shared package does.
5. Viewer suite: 475 + 26 = **501**.
6. `ScannerBuild.ConfigureXr`, then `ScannerBuild.BuildScanner`, then
   `xcodebuild`.
7. Device-test both features. Each feature's own handoff has its procedure and
   its "what to report" list; run them in one session now that they are one
   build.

### The one combined thing to check on device

Sweep the room, derive it, then in `AddObjects` **look at a desk that sits
against a wall**. The in-room gate is the piece that only exists at the seam,
and a swept footprint is derived by extrapolating fitted lines — so if the
sweeps came out slightly small, a real desk against a wall can fall just
*outside* the derived polygon and never be offered. If that happens, report it:
it is a genuine interaction between the two features' error characteristics, and
nothing off-device can predict it.

## Test results

**None. Nothing was compiled and nothing was run.** Linux, no Unity, no
`xcodebuild`, no iPhone. 87 tests across this branch were written and have
**never executed**.

What was verified for the merge specifically:

- no conflict markers remain anywhere in the tree;
- the merged constructor takes all seven collaborators, and both test factories
  and the composition root pass all seven in the matching order;
- `furnitureDetection` is constructed against the same `cornerCapture` instance
  the sweep path adopts corners into — checked by reading `BuildWorkflow`;
- both HUDs are constructed in `BuildScene` and required by `VerifyScene`;
- `planeManager` appearing twice in the scene builder is two different
  components (`ArSpatialProvider` and `ScannerBootstrap`), not a duplicate;
- the transition table is the sweep branch's, unchanged;
- brace, parenthesis and bracket balance on every merged file.

## Known failures

- **`ScannerSceneTests` and the Viewer scene test fail** until steps 1 and 2.
  Both committed scenes are stale: the scanner scene has neither `WallSweepHud`
  nor `FurnitureDetectionHud` nor `planeManager` on `ArSpatialProvider`, and the
  viewer scene has no `ExportAssetsButton`.
- Everything else is unknown, because nothing ran.

## Known limitations

Both features' limitation lists still apply in full and are not repeated here.
The merge adds two:

- **Two HUDs now depend on phase exclusivity for their screen position.** If a
  future change makes `SweepWalls` and `CaptureCorners`, or `AddOpenings` and
  `AddObjects`, simultaneously visible, controls will overlap rather than fail
  loudly.
- **A dismissed detection surface stays dismissed across a re-sweep.**
  `RedoWallSweeps` clears corners and walls but not
  `FurnitureDetectionController`'s resolved-surface set. Arguably right — the
  user dismissed it deliberately — but it is not obvious, and
  `ClearResolved()` exists and is not wired to anything.

## Files most important to read next

- `apps/scanner/.../Tests/EditMode/ScanWorkflowSweepAndDetectionTests.cs` — the
  seam, and the clearest statement of the combined intended behavior.
- `apps/scanner/.../Runtime/UI/FloorLockHud.cs` — `BuildWorkflow`, where the
  seam is made.
- `docs/decisions/ADR-0005-sweep-wall-capture.md` and
  `docs/decisions/ADR-0006-furniture-detection-and-asset-export.md`.

## Next task

1. Regenerate and commit both scenes.
2. Run all three suites; fix what does not compile. Expect this to take a pass —
   nothing here has ever been through a compiler.
3. Device-test both features in one session, and **tape-measure everything**:
   wall lengths for `ADR-0005`, furniture dimensions for `ADR-0006`. Both
   features' real accuracy is unknown and neither can be established
   off-device.
4. Open one exported `.glb` in Blender before trusting the writer.
5. `I1` is still not started. Plan section 25 still says this stretch work
   should have waited for it; `ADR-0006` records that it did not.
