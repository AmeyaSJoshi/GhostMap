# Handoff

## Branch
`scanner/sweep-wall-capture`

## Base commit
`f5a7d30` (merge of PR #13; Scanner S1-S6 and Viewer V1-V6 complete, Integration not started)

## Head commit
`423bd29`

## What changed

Replaces walking to four corners with **standing still and turning**. The user
sweeps the center-screen ray along each wall's floor junction; each sweep is
fitted to a line, and the four corners are derived by intersecting consecutive
wall lines. Recorded as `ADR-0005`.

Three commits:

- `f13873c` `feat(shared)` — `ADR-0005`, `WallFitting` (total-least-squares line
  fit in XZ, line/line intersection, corner derivation from four ordered walls),
  32 shared tests, `docs/architecture/overview.md` updated.
- `9c9aa82` `feat(scanner)` — `WallSweepController`,
  `CornerCaptureController.TryAdoptDerivedCorners`, `ScanPhase.SweepWalls`, the
  workflow's sweep transitions and methods, 57 scanner tests.
- `423bd29` `feat(scanner)` — `WallSweepHud`, `ScannerSceneBuilder` wiring,
  `VerifyScene` requirement, `CornerCaptureHud` releasing the `FloorLocked` slot.

### Why it works without walking

Implementation plan section 8.4 already stated the primitive and its
consequence: corner capture is the center-screen ray intersected with the
**locked floor plane**, so "the user only needs to aim at the floor/wall
boundary." Nothing in that arithmetic requires standing at a corner. The floor
junction is a line lying entirely in the locked floor plane, so every point
along it is recoverable by the ray intersection that is already device-verified.

Corners then come from `corner[i] = intersect(wall[i - 1], wall[i])`, which is
the convention `RoomGeometry.BuildWalls` already uses, so a room built from
these corners re-derives the walls that produced them.

**No ARKit vertical-plane detection is used.** `ADR-0004` and plan section 1.2
are untouched.

### Single source of corners

The swept path does **not** keep its own corner store. It computes corners and
hands them to `CornerCaptureController.TryAdoptDerivedCorners`, which runs the
unchanged shared `RoomValidator.ValidateRoom` and installs them. Everything
downstream — closure verification, height capture's wall derivation, opening
placement, `BuildSnapshot` — therefore works identically whichever path the user
took, and never has to ask which one it was.

### `VerifyClosure` is kept and is now a better check

Derived corners are self-consistent, so closure measured against them would be
`0.0` for any scan however bad — which would claim `ClosureQuality.Excellent`
through `SceneSnapshot.closureErrorM`. So `VerifyClosure` is retained unchanged
in meaning: the user re-aims at one physical corner and the error is the distance
to the **derived** corner. That compares an independent observation against a
value derived from different measurements, rather than a tap against an earlier
tap. Bands and `ClassifyClosure` unchanged.

### Walked path retained

`ScanPhase.CaptureCorners` and `CornerCaptureController` are intact, now entered
from the sweep phase via **Walk Corners Instead**. Sweeping has a real accuracy
ceiling (below) and no device verification yet; the walked path has both. The
transition-table change is **additive**, so every Task S3 route still exists and
the existing 55 `ScanWorkflowControllerTests` keep their paths unchanged.

## Contract impact

**None. Additive only.**

| Contract | Impact |
| --- | --- |
| `SceneSnapshot`, `RoomModel.corners` | None. Still `schemaVersion` 1, still exactly four ordered Ghost-space corners at `y = 0`. |
| Walls | Still derived, still never serialized. |
| `closureErrorM` | Meaning, units and bands preserved. |
| `scanPhase` | One new value, `SweepWalls`. Free-form string; nothing parses or validates it. |
| Protocol v1 | None. |
| `scene-schema-v1.md`, `protocol-v1.md` | No change required. |
| Viewer | **No source changes.** `scanPhase` is display-only (`ViewerHudController.cs:282`); a swept room is indistinguishable from a walked one on the wire. |

`docs/architecture/overview.md` was updated, which `ADR-0005` authorizes per that
document's own freeze note.

## How to test

**Run step 1 before anything else.** The scene was authored on a machine without
Unity, so the committed `Scanner.unity` does not yet contain `WallSweepHud`.

1. Regenerate the scene: Unity menu **GhostMap → Build Scanner Scene**, or
   `ScannerSceneBuilder.BuildScene`. Commit the resulting `Scanner.unity`.
2. Shared suite:
   ```bash
   /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
     -batchmode -nographics -projectPath shared/TestProject \
     -runTests -testPlatform EditMode -testResults /tmp/sweep-shared.xml \
     -logFile /tmp/sweep-shared.log
   ```
   Expect 156 existing + 32 new = **188**.
3. Scanner suite:
   ```bash
   /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
     -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
     -runTests -testPlatform EditMode -testResults /tmp/sweep-scanner.xml \
     -logFile /tmp/sweep-scanner.log
   ```
   Expect 361 existing + 57 new = **418**. Note the 361 already includes the 156
   embedded shared tests, so the 32 new shared tests are counted here too —
   expect **450** if that holds, and reconcile against the XML rather than
   trusting this arithmetic.
4. Viewer suite, to confirm nothing crossed over: expect **475**, unchanged.
5. Build: `ScannerBuild.ConfigureXr`, then `ScannerBuild.BuildScanner` in a
   separate Unity invocation, then `xcodebuild`.
6. **Physical-device test** — see below. Required before this is considered
   working.

### Physical-device procedure

1. Launch on the iPhone, grant camera and local-network permission.
2. Lock the floor as usual.
3. Tap **Start Walls**. Phase should read `SweepWalls`.
4. Stand roughly in the middle of the room. Tap **Sweep Wall 1/4**.
5. Pan the crosshair along wall 1's floor/wall join. The yellow aim marker
   should track the join. Watch `pts` and `span` climb in the readout; the
   button changes from "Finish Wall 1 (keep sweeping)" to "Finish Wall 1" once
   both minimums are met.
6. Tap **Finish Wall 1**. Two blue markers should appear at the ends of the
   swept segment, and the readout should show `w1 span … rms … @…m`.
7. Repeat for walls 2, 3, 4, **turning in place** rather than walking. Sweep
   them in order around the room — order sets the winding.
8. Tap **Build Room**. Phase should become `VerifyClosure` and the green/blue
   corner markers from `CornerCaptureHud` should appear **at the real room
   corners**, including any the sweeps never reached.
9. Re-aim at the first physical corner and tap **Verify First Corner**. Record
   the closure number — this is the accuracy figure that matters.
10. Continue through height, openings, objects, finalize as normal.

### What to report

- the four `rms` and `@` (range) values from the readout;
- the closure error at step 9;
- whether the derived corner markers landed on the real corners, and by roughly
  how much they missed if not;
- **tape-measured wall lengths versus the viewer's**, which is the only thing
  that can detect a systematic aim bias;
- whether sweeping a wall that is partly hidden by furniture worked.

### Worth deliberately testing

- a wall with a wardrobe or bed against it, so only part of the join is visible;
- a very short sweep (should be refused: "Swept less than 0.40 m of wall");
- deliberately wandering off the join mid-sweep (should be refused on residual);
- sweeping from far away in the largest room available, and comparing accuracy
  against a sweep of the same wall from close up;
- **Walk Corners Instead**, to confirm the S3 path still works end to end.

## Test results

**None. Nothing was compiled and nothing was run.**

This machine is Linux with no Unity, no `xcodebuild` and no iPhone. The 89 new
tests were written but have **never been executed**, and the production code has
**never been compiled**. Expect to fix compile errors before the suites run.

What was actually verified here, and it is not much:

- brace, parenthesis and bracket balance on every new and modified file;
- every `WallSweepRejection` member referenced is declared;
- every `[SerializeField]` name in `WallSweepHud` matches the names used in
  `AssignSerializedReferences` and `RequireAssigned`;
- the public APIs the new code calls (`Vec3Dto.FromVector3`,
  `WallDefinition.Tangent`, `ValidationResult.IsValid`,
  `HeightCaptureController.Walls`, `RoomValidator` constants) exist with the
  signatures used;
- `ScanPhase` has no ordinal dependency — no `SerializeField`, no int cast, no
  occurrence in `Scanner.unity` — so inserting `SweepWalls` mid-enum is safe;
- the arithmetic in the numeric test expectations was worked through by hand
  (the eigen decomposition for the three fit orientations, the 0.02 m RMS
  fixture, and the 0.149 m residual of the zig-zag rejection case).

## Known failures

- **`ScannerSceneTests` will fail** until step 1 of "How to test" is done. It
  asserts the committed `Scanner.unity`, which has no `WallSweepHud` yet. The
  `VerifyScene` requirement is deliberately hard rather than conditional: a null
  serialized reference presents on the phone as "nothing happens", which is
  exactly what both S1 device failures looked like.
- Everything else is unknown, because nothing ran.

## Known limitations

- **Accuracy falls off with the square of aim distance.** For camera height `h`
  and aim distance `d`, a pitch error `delta` displaces the floor point by about
  `delta * (h + d^2 / h)`. At `h = 1.5 m`:

  | `d` | `delta = 0.5 deg` | `delta = 1.0 deg` |
  | --- | --- | --- |
  | 1.5 m | 0.03 m | 0.05 m |
  | 3.0 m | 0.07 m | 0.13 m |
  | 5.0 m | 0.16 m | 0.32 m |

  Walking to a corner keeps `d` near 1.5 m. Mid-room in a 4x3 m bedroom gives
  `d` of roughly 1.5-2 m and stays inside the plan's 0.12 m median target; a
  6x5 m room pushes against it. The HUD colours a wall's end markers orange and
  warns when a sweep exceeded 4 m, which flags the risk but cannot remove it.

- **Random aim jitter averages out over a sweep; systematic bias does not.** A
  user who consistently aims slightly above the join pushes every wall outward
  by the full table amount, producing a uniformly oversized room with a low RMS
  residual and an entirely clean-looking scan. Fit residual cannot detect this.
  Only Task `I2`'s tape measure can, which is why it is in "What to report".

- Sweep order sets the winding. Sweeping the walls out of order produces a
  self-intersecting footprint, which `RoomValidator` refuses — the failure is
  safe but the message will talk about self-intersection rather than order.

- A wall needs a swept span of at least 0.40 m. That is below the 0.50 m minimum
  wall length on purpose, because a sweep only sees the visible part of a join,
  but a very short visible segment still yields a confident-looking fit with a
  poorly determined angle.

- `MinSampleSpacingM` is 0.01 m, so a sweep from far away — where a degree of
  pan moves the floor point a long way — banks samples faster per degree than a
  close one. Not wrong, but sample count is not comparable between near and far
  sweeps.

- Two capture paths now exist for the same four corners. Deliberate until device
  numbers justify removing one.

- `ActiveSpanM` is maintained incrementally by keeping the two furthest-apart
  samples. That is exact for samples lying along a wall, which is the only case
  it runs on, but it is not a general point-set diameter.

- Furniture is untouched, as asked. The swept path ends at `VerifyClosure` and
  the rest of the scan is unchanged.

## Files most important to read next

- `docs/decisions/ADR-0005-sweep-wall-capture.md` — the decision, the
  compatibility table and the accuracy analysis.
- `shared/com.ghostmap.shared/Runtime/Geometry/WallFitting.cs` — the fit and the
  corner derivation.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/WallSweepController.cs` —
  sweep lifecycle and the accept gates.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/CornerCaptureController.cs` —
  `TryAdoptDerivedCorners`, the seam between the two paths.
- `apps/scanner/Assets/GhostMap/Scanner/Tests/EditMode/ScanWorkflowSweepTests.cs` —
  the clearest statement of the intended end-to-end behavior.
- `docs/architecture/overview.md` sections 4 and 8.

## Next task

1. **Regenerate `Scanner.unity`** and commit it.
2. Run the three suites; fix whatever does not compile.
3. **Physical-device test** per the procedure above. `AGENTS.md` rule 12: this
   is not working until it has run on a real iPhone.
4. Tape-measure the room and compare, before trusting any of it. If the
   systematic-bias failure mode shows up, say so — it is the one failure the
   automated tests and the on-screen residual both cannot see.
5. Only then decide whether to keep or remove the walked fallback.
6. The automatic 360-degree sweep (one button, azimuth-binned farthest-hit
   extraction, RANSAC segmentation) was deliberately deferred to a second branch
   so this one could be measured first. It is not started.
