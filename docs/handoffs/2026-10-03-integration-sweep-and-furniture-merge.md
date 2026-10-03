# Handoff

## Branch
`integration/sweep-and-furniture`

## Base commit
`f5a7d30` (merge of PR #13), via both feature branches.

## Head commit
`fb2320c` (was `74888a6` when this branch had never been compiled).

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

`ScanWorkflowSweepAndDetectionTests` (9 tests) now asserts it:

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

**Steps 1-5 are done and committed on this branch; they are kept here so the
numbers can be reproduced.** Start at step 6.

1. ~~Regenerate the scanner scene~~ — done, committed. (`--rebuild-scenes
   scanner`, or **GhostMap → Build Scanner Scene** from the GUI.)
2. ~~Regenerate the viewer scene~~ — done, committed. (**GhostMap → Build
   Viewer Scene**.)
3. Shared suite: **197**. Confirmed.
4. Scanner suite: **510**. Confirmed.
5. Viewer suite: **542**, not the 501 originally estimated here. The viewer
   project re-runs the whole shared suite via `"testables"`, so the 41 new
   shared tests are counted in the viewer total as well; the estimate added the
   26 new viewer tests but not those. Reconcile against the XML, never
   arithmetic — this is the second count in this handoff that arithmetic got
   wrong.
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

**All three suites pass on Linux with Unity 6000.3.24f1.**

| Suite | Result |
| --- | --- |
| shared | 197 tests, 197 passed |
| viewer | 542 tests, 542 passed (345 viewer + 197 shared) |
| scanner | 510 tests, 510 passed (313 scanner + 197 shared) |

All 149 tests new to this branch pass. Run with `./tools/run_unity_tests.sh`.

Two defects were found on first execution and are fixed in this branch:

1. **The scanner did not compile.** `ScanWorkflowSweepTests` and
   `ScanWorkflowDetectionTests` each still constructed the six-argument
   `ScanWorkflowController` from their own feature branch. Both files were
   *new* on their branch, so git merged them with no conflict and the
   resolution pass below — which inspected only conflicted files — never looked
   at them. **This is the gap in how the merge was reviewed**: two branches that
   both widen one constructor produce a clean merge and a broken build, and the
   "no conflict markers remain" check cannot see it. The integration tests did
   not catch it either, because `ScanWorkflowSweepAndDetectionTests` has its own
   factory and that one was correct.
2. **`ScannerSceneTests` failed** on `ArSpatialProvider.planeManager is not
   assigned`, as predicted below. Both scenes are regenerated and committed.

Still untested: anything requiring a phone or Xcode, and no exported `.glb` has
been opened in an external tool.

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

- **None in the automated suites.** Both scenes have been regenerated and
  committed, and all three suites pass.
- **There is no Viewer scene test.** The prediction above that "the Viewer scene
  test fails" was wrong — no such test exists. Only the scanner has one
  (`ScannerSceneTests`), which is what caught the stale scanner scene. The
  viewer scene was equally stale and the suite would never have said so. Worth
  adding; a green viewer suite is currently not evidence that `Viewer.unity`
  matches `ViewerSceneBuilder`.
- Everything requiring a phone, Xcode, or a tape measure remains unknown.

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

Both scenes are regenerated and committed, and all three suites pass. What is
left is everything that needs hardware.

1. **`ScannerBuild.ConfigureXr`, `ScannerBuild.BuildScanner`, then
   `xcodebuild`** — on a Mac. Nothing about the iOS build has been exercised;
   the Linux suite deliberately runs without `-buildTarget iOS`.
2. **Device-test both features in one session, and tape-measure everything**:
   wall lengths for `ADR-0005`, furniture dimensions for `ADR-0006`. Both
   features' real accuracy is still completely unknown — passing EditMode tests
   say the math is self-consistent, not that it measures a room correctly.
   `ADR-0005` records that systematic aim bias neither averages out nor shows up
   in the fit residual, so a wrong wall can look confident.
3. **Validate one exported `.glb` before trusting the writer** — Khronos
   `npx gltf-validator <file>.glb` for spec conformance, then Blender for
   whether it is actually usable. The container is asserted byte by byte by
   tests that pass, which is not the same thing.
4. **Consider adding a Viewer scene test.** The scanner has one and it earned
   its keep this pass; the viewer has none, so viewer scene staleness is
   invisible to a green suite.
5. **Capture a real snapshot off the wire and commit it as a fixture.** The
   three fixtures are hand-authored and suspiciously tidy — exact integer
   corners, furniture at exactly the schema's catalog dimensions and exactly
   90/0/180 degrees of yaw. The viewer renders fixture and phone data through
   the same `ViewerSceneStore.TryApplyScannerSnapshot`, so a captured real
   snapshot would let the viewer be developed against realistic geometry
   without a phone in the room.
6. `I1` is still not started. Plan section 25 still says this stretch work
   should have waited for it; `ADR-0006` records that it did not.
