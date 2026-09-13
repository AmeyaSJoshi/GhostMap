# Handoff

## Branch
`scanner/s3-corner-capture-closure`

## Base commit
`a3f15f8` — merge of Task S2 (PR #2) into `main`. S1 and S2 are both in history.

## Head commit
`<implementation commit sha — recorded in the follow-up commit>`

## Status
**S3 implementation is complete and green off-device. S3 is NOT complete:
the Task S3 physical-device test has not been run.** Nothing in this handoff
may be read as device-verified.

---

## What changed

### New scanner runtime
- `Runtime/Capture/CornerCaptureController.cs` — the whole of S3's logic: the
  floor-ray capture, every validation gate, undo, clear, and closure
  measurement. Plain C#, no MonoBehaviour, so all of it is testable off-device.
- `Runtime/UI/CornerCaptureHud.cs` — scene shell. One context-sensitive primary
  button, Undo, Redo Corners, a readout, and world-space markers on captured
  corners.

### Changed scanner runtime
- `Runtime/Workflow/ScanWorkflowController.cs` — now composes the corner
  controller and owns the S3 phase transitions, revision counter and snapshot
  contents. Constructor is now `(FloorLockController, CornerCaptureController)`.
- `Runtime/UI/FloorLockHud.cs` — builds the corner controller. It is the scene's
  single composition root; `CornerCaptureHud` reads the workflow from it rather
  than constructing a second one.
- `Editor/ScannerSceneBuilder.cs` — creates and wires the S3 UI, and
  `VerifyScene()` now asserts the S3 wiring too. The floor-lock readout moved up
  to `y = 380` to clear the new button row at `y = 240`.
- `Scanner.unity` — rebuilt from the builder.

### Tests
- `Tests/EditMode/CornerCaptureControllerTests.cs` — new, 37 tests.
- `Tests/EditMode/ScanWorkflowControllerTests.cs` — 17 S3 tests added (14 → 31).
- `Tests/EditMode/FakeSpatialProvider.cs` — added `FloorHitRequestCount`,
  `ScreenRayRequestCount` and `LastScreenRayPoint`, so "corner capture must not
  need an AR plane raycast" is an assertion rather than a claim.
- `Tests/EditMode/ScannerSceneTests.cs` — renamed the test to cover both tasks.

---

## Contract impact

**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no viewer file was
touched. Scene schema v1 and protocol v1 are unchanged.

S3 needed no contract change because the shared package already carried every
rule S3 requires. `RoomValidator.ValidateNewCorner`, `RoomValidator.ValidateRoom`
and `RoomValidator.ClassifyClosure` were delivered in F2 with the spacing, area,
wall-length, interior-angle, self-intersection and closure-band rules already
implemented and tested. The scanner orchestrates them; it re-implements nothing.

Walls are absent from the wire by construction: `RoomModel` has no `walls`
field, and a test asserts no `"walls"` key appears in the four-corner snapshot.

---

## How the capture works

```text
ray   = provider.GetScreenRay(provider.CenterScreenPoint)   // AR world space
world = RayPlaneMath.TryIntersectHorizontalPlane(ray, frame.FloorWorldY)
ghost = frame.WorldToGhost(world)
ghost.y = 0                                                 // forced, not assumed
        -> RoomValidator.ValidateNewCorner(existing, ghost)
        -> RoomValidator.ValidateRoom(existing + candidate)
        -> append with a GUID id, revision++, republish snapshot
```

### No AR raycast per corner
A corner is found by arithmetic, not by `ARRaycastManager`. Plane extents lag the
room, stop at skirting boards and furniture, and are routinely absent at exactly
the corner being aimed at, so requiring a plane hit per corner would make real
rooms uncapturable. The floor plane is already known exactly — that is what
locking it bought. `CaptureDoesNotUseAnArPlaneRaycast` asserts the plane
raycaster is never consulted after the lock.

### Why validation runs twice
`ValidateNewCorner` sees one candidate against its neighbours: floor plane,
spacing from the previous corner, separation from non-neighbours, and
self-intersection once the candidate closes the polygon. It cannot see maximum
wall length, footprint area or interior angles, because those are properties of
the chain. So the candidate room is also run through `ValidateRoom`, which
handles the partial chain before four corners and the full footprint at four.
Dropping the second call leaves the over-long wall, under-area and extreme-angle
cases undetected — confirmed by mutation, below.

### Closure
```text
closureErrorM = Vector2.Distance(
    new Vector2(first.x, first.z),
    new Vector2(verify.x, verify.z))
```
Classified by the shared `RoomValidator.ClassifyClosure`, bands inclusive:

| Error | Band | Result |
| --- | --- | --- |
| `<= 0.08 m` | `Excellent` | proceed |
| `> 0.08 m`, `<= 0.15 m` | `Acceptable` | proceed, warn |
| `> 0.15 m` | `Rejected` | do not proceed; Redo Corners |

A measurement in the reject band is still a **successful measurement** —
`TryMeasureClosure` returns `true` and reports the band — because the exact
number is what the user needs to see. Whether the scan may continue is
`IsClosureAccepted`, and acting on it is the workflow's decision.

### Frame immutability
The frame is read and never written. `FloorLockController` refuses a second lock
and `GhostCoordinateFrame` has no setters, so nothing in S3 can move it. Five
tests assert it: the same object survives every capture and the closure check,
its origin and axes are unchanged, a stored corner does not shift when the camera
moves between corners, a re-lock attempt during capture is refused, and
`RedoCorners` keeps the frame while discarding the footprint.

---

## Two decisions worth knowing about

### 1. Revision is monotonic, including across undo
The implementation plan's S3 test list says "undo decrements count/revision
correctly". The **count** is decremented. The **revision is not** — it advances,
like every other mutation.

Scene schema v1 states the viewer ignores any snapshot whose revision is not
greater than the latest it has accepted for that session. An undo that wound the
counter back would therefore publish a room the viewer is contractually obliged
to drop, and the two would silently disagree from then on. The plan's phrasing is
read as "undo adjusts count and revision correctly"; the schema decides what
correct means. `UndoRemovesACornerAndStillAdvancesTheRevision` pins it.

### 2. An accepted closure moves the phase to `CaptureHeight`
The plan's state machine has `VerifyClosure -> CaptureHeight`, and its S3 text
says "otherwise proceed". So a closure in the Excellent or Acceptable band
transitions the phase. **No height-capture behaviour was implemented** — that is
Task S4, and it is untouched. The phase is a label the HUD reports; the S3 UI
simply stops offering actions there, apart from Redo Corners.

A rejected closure does **not** transition. The phase stays at `VerifyClosure`,
the exact error stays on screen and on the snapshot, and the only way forward is
Redo Corners, which clears the footprint and returns to `CaptureCorners`.

---

## How to test

### EditMode tests
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s3-editmode.xml -logFile /tmp/ghostmap-s3-tests.log
```

### iOS build — still two steps, still cannot be collapsed
```bash
U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity

$U -batchmode -nographics -projectPath apps/scanner \
   -executeMethod GhostMap.Scanner.Editor.ScannerBuild.ConfigureXr -quit \
   -logFile /tmp/ghostmap-s3-configurexr.log

$U -batchmode -nographics -projectPath apps/scanner \
   -executeMethod GhostMap.Scanner.Editor.ScannerBuild.BuildScanner -quit \
   -logFile /tmp/ghostmap-s3-build.log
```
Note the **fully qualified** method names. `-executeMethod ScannerBuild.ConfigureXr`
fails with `executeMethod class 'ScannerBuild' could not be found`, because the
class is in namespace `GhostMap.Scanner.Editor`.

```bash
cd apps/scanner/Builds/iOS
xcodebuild -project Unity-iPhone.xcodeproj -target Unity-iPhone \
  -configuration Release -sdk iphoneos CODE_SIGNING_ALLOWED=NO
```

### Rebuilding the scene after a UI change
```bash
$U -batchmode -nographics -projectPath apps/scanner \
   -executeMethod GhostMap.Scanner.Editor.ScannerSceneBuilder.BuildScene -quit \
   -logFile /tmp/ghostmap-s3-scene.log
```

---

## Test results

All executed and observed on Unity `6000.3.24f1`.

### EditMode — passing
**266 tests, 266 passed, 0 failed, 0 skipped.** Unity exit code `0`.

| Fixture | Passed |
| --- | --- |
| `CornerCaptureControllerTests` | 37 |
| `ScanWorkflowControllerTests` | 31 |
| `GhostFrameTests` | 20 |
| `FloorLockControllerTests` | 17 |
| `ScannerXrSettingsTests` | 4 |
| `ScannerSceneTests` | 1 |
| `GhostMap.Shared.Tests` | 156 |

S2 finished at 212. S3 adds 54.

### Mutation checks — the new tests were verified to bite
Passing tests prove nothing on their own, so three deliberate defects were
introduced and the suite re-run. Each was reverted afterwards.

| Mutation | Result |
| --- | --- |
| Intersect the ray with world `y = 0` instead of `frame.FloorWorldY` | **4 failed** — `CapturedCornerIsTheRayFloorPlaneIntersection`, `CornerOrderIsPreserved`, `ReAimingAtTheFirstCornerLandsOnTheStoredFirstCorner`, `TheSnapshotCarriesTheCornersInCaptureOrder` |
| Drop the whole-room `ValidateRoom` call, keeping only the per-corner rules | **3 failed** — `WallOverTheMaximumLengthIsRejected`, `RoomBelowTheMinimumAreaIsRejected`, `ExtremeInteriorAngleIsRejected` |
| Stop forcing Ghost `y = 0`, storing the raw intersection | **0 failed at first — the test was not good enough.** See below. |

The third mutation initially survived. Every `y == 0` assertion still passed,
because at the fixture's scale — an eye 1.5 m above the floor, aiming a metre
ahead — `origin + direction * t` rounds to exactly the plane it was solved for,
so the forcing changed nothing the tests could see.

`GhostYIsForcedToZeroEvenWhenTheIntersectionCarriesResidue` was added to fix
that: the same capture aimed from progressively further out, where the three
roundings in the intersection no longer cancel. Re-running the mutation now
fails three of its four cases with the residue visible:

```text
(12.5, 7.25)      expected 0.0f, was -5.96046448E-07f
(137.77, 211.31)  expected 0.0f, was  6.07967377E-06f
(1013.4, 1777.9)  expected 0.0f, was -2.44379044E-05f
```

The `(1.5, 1)` case still rounds to exactly zero, which is precisely why the
original assertion was blind. The forcing is real and now pinned.

### Build
- `ScannerBuild.ConfigureXr` — exit `0`. Reported the ARKit loader define and
  Active Input Handling already correct; neither was changed by S3.
- `ScannerBuild.BuildScanner` — exit `0`, Xcode project written.
- `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO` — **BUILD SUCCEEDED**.

### Physical device
**Not run.** See the procedure below. S3 stays open until it passes.

---

## Task S3 physical-device test — procedure

Deploy the Xcode project at `apps/scanner/Builds/iOS` (a signing team must be
selected by hand). Scan a taped rectangle or a known rectangular bedroom, and
measure it first so the numbers can be checked.

### 1. Lock the floor — unchanged from S2
Wait for `Phase: FindFloor` and `Session: SessionTracking`. Aim the crosshair at
a clear patch of floor a metre or so ahead until the crosshair turns amber, then
press **Lock Floor**. `Phase` becomes `FloorLocked` and `Handedness: +1.000`.

### 2. Start corners
The primary button (centre of the S3 row, above Lock Floor) reads **Start
Corners**. Press it. `Phase` becomes `CaptureCorners`, the button becomes
**Capture Corner 1/4**, and the corner readout shows `Corners 0/4`.

### 3. Capture the four corners, in order around the room
Stand back and aim the centre crosshair at the point where the two walls meet
the floor. The `Aim G(x,y,z)` line updates live and is what will be stored, so
confirm it looks sane before pressing. Press **Capture Corner N/4**.

Go around the room consistently — all clockwise or all counter-clockwise. Order
is load bearing: walls are derived from consecutive corners.

After each capture, expect:
- the counter advances, `Corners 1/4` → `2/4` → `3/4` → `4/4`;
- a new line `c1 G(...)`, with `y` exactly `0.00`;
- **a sphere marker appears on the real corner** — blue for corner 1, green for
  the rest. This is the single most important thing to check: a marker that
  floats, sits at the wrong corner, or drifts as you walk means the capture is
  wrong regardless of what the numbers say;
- previously placed markers do not move.

Sanity-check the Ghost coordinates against the tape measure: consecutive corners
should be the measured wall length apart.

### 4. Closure verification
After the fourth corner, `Phase` becomes `VerifyClosure`, the readout says
`re-aim at corner 1`, and the primary button reads **Verify First Corner**.

Walk back and aim the crosshair at the **same physical corner you captured
first** — the one with the blue marker. Press **Verify First Corner**.

The readout prints `Closure <n> m — <band>` and an orange marker appears at the
verification point.

| Reading | Meaning | Expected behaviour |
| --- | --- | --- |
| `<= 0.080 m` | `Excellent` | phase moves to `CaptureHeight` |
| `0.080`–`0.150 m` | `Acceptable` | phase moves to `CaptureHeight` |
| `> 0.150 m` | `Rejected (rescan)` | phase stays `VerifyClosure`; only Redo Corners is offered |

`CaptureHeight` is Task S4 and is **not implemented**. Reaching that phase is the
S3 success signal, not an invitation to keep going.

### 5. Also exercise
- **Undo** — press it mid-capture. The counter drops by one, the last marker
  disappears, earlier markers do not move.
- **Undo from VerifyClosure** — the phase returns to `CaptureCorners` and the
  closure line clears.
- **Redo Corners** — after a closure, all markers clear and the counter returns
  to `0/4`. `Frame O`, `Frame X` and `Frame Z` in the floor-lock readout must be
  **unchanged**. The room is re-measured, not re-anchored.
- **A deliberately bad corner** — aim within about 30 cm of the previous corner
  and capture. It must be refused with a `Rejected:` line naming the spacing
  rule, and the counter must not advance.

### 6. Report as a failure
- any marker not on the physical corner it was captured at;
- any marker that moves after being placed, or when you walk;
- `Frame O/X/Z` changing at any point after the floor lock;
- a corner line whose `y` is not `0.00`;
- a Ghost distance between corners that disagrees with the tape measure by more
  than a few centimetres;
- closure consistently above 0.15 m in a room whose corners were aimed at
  carefully;
- `Aim: no floor intersection` while aiming at the floor;
- the primary button doing nothing when pressed.

---

## Known failures
None off-device. Everything below is non-blocking.

### Carried forward from S1/S2, still true
- The iOS build is two steps and cannot be collapsed into one.
- The generated Xcode project has no signing team.
- `ScannerIosPostBuild` carries the Xcode 26 `$(TOOLCHAIN_DIR)` workaround.
- `XROrigin.m_CameraYOffset` stays at `1.1176`; the frame is immune to it.
- There is no reset. A new frame means relaunching the app.
- The S1 diagnostics readout is still on screen.

### New in S3
- **The screen is now crowded.** Three readouts and four buttons. This is
  bring-up instrumentation, not the capture UI; Task S6 owns the real one.
- **No AR marker for the aim point before capture.** The `Aim G(...)` line is
  numeric only. A live preview marker would make aiming easier and is a
  reasonable S6 addition; it was left out as a stretch feature.
- **Corner markers are untextured spheres** created with
  `GameObject.CreatePrimitive`, lit by the Built-in pipeline's default material.
  They read fine against a floor but are not a designed visual.
- **`ScanPhase.CaptureHeight` is reachable with nothing behind it.** Entered on
  an accepted closure, as the plan's state machine specifies. The S3 HUD offers
  only Redo Corners there. Task S4 fills it in.
- **No corner can be edited, only undone.** Fixing corner 2 means undoing
  corners 4 and 3 first, or pressing Redo Corners. The plan asks for Undo, not
  for editing.

---

## Files most important to read next
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/CornerCaptureController.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Workflow/ScanWorkflowController.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Tests/EditMode/CornerCaptureControllerTests.cs`
- `shared/com.ghostmap.shared/Runtime/Validation/RoomValidator.cs`
- `shared/com.ghostmap.shared/Runtime/Geometry/RayPlaneMath.cs`
- `docs/plans/ghostmap-implementation-plan.md` sections 9.1, 9.2 and Task S3

## Next task
1. **Run the Task S3 physical-device test above.** S3 is not complete until it
   passes on a real iPhone.
2. Then **S4 — height capture**. It consumes the same locked frame, the derived
   walls from `RoomGeometry.BuildWalls`, and
   `GhostCoordinateFrame.WorldRayToGhost`. `ScanPhase.CaptureHeight` is already
   entered by an accepted closure, so S4 starts by giving that phase a UI.
