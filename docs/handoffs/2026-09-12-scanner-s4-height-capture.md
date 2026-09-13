# Handoff

## Branch
`scanner/s4-room-height-capture`

## Base commit
`bff7590` — merge of Task S3 (PR #3) into `main`. S1, S2 and S3 are all in
history.

## Head commit
`1b8a6ce` — `feat(scanner): capture room height with manual fallback`. The
commit that follows it changes only this handoff document, so its scanner
sources are byte-identical.

## Status
**Task S4 is complete and verified on a physical iPhone.** This handoff closes
S4. The device procedure below was run against the build produced from
`1b8a6ce` and passed; the results are recorded under "Physical-device test
procedure — run, passed". Captured height on the test room was **2.69 m**.

---

## What changed

### New scanner runtime
- `Runtime/Capture/HeightCaptureController.cs` — the whole of S4's logic:
  deriving vertical wall planes from the S3 corners, wall selection,
  ray/plane intersection with a parallel-vs-behind-camera distinction,
  2.0-4.0 m range validation, and the manual-entry fallback. Plain C#, no
  MonoBehaviour, so all of it is testable off-device — same shape as
  `CornerCaptureController`.
- `Runtime/UI/HeightCaptureHud.cs` — scene shell. A wall-cycle button, a
  Capture Height button, a manual height `InputField` with a confirm button,
  a readout, a world-space line on the selected wall, and a color-coded aim
  marker.

### Changed scanner runtime
- `Runtime/Workflow/ScanWorkflowController.cs` — now composes
  `HeightCaptureController`. Constructor is now
  `(FloorLockController, CornerCaptureController, HeightCaptureController)`.
  Added `SelectHeightWall`, `TryCaptureHeight`, `TrySetManualHeight`; both
  capture methods transition `CaptureHeight -> AddOpenings` on success.
  `RedoCorners` now also calls `HeightCaptureController.ResetWallSelection()`,
  so a stale wall index cannot resolve against a footprint that no longer
  exists. `BuildSnapshot` now publishes the real captured height instead of a
  hardcoded `0f`.
- `Runtime/UI/FloorLockHud.cs` — the scene's composition root now also builds
  `HeightCaptureController` and passes it to `ScanWorkflowController`.
- `Editor/ScannerSceneBuilder.cs` — creates and wires the S4 UI (wall-select
  button, capture button, manual-height input field and button, readout), and
  `VerifyScene()` now asserts the S4 wiring too.
- `Scanner.unity` — rebuilt from the builder.

### Tests
- `Tests/EditMode/HeightCaptureControllerTests.cs` — new, 18 tests.
- `Tests/EditMode/ScanWorkflowControllerTests.cs` — 8 S4 tests added (31 ->
  39); the `Workflow()` fixture helper now builds and passes a
  `HeightCaptureController`.
- `Tests/EditMode/ScannerSceneTests.cs` — renamed the test to cover all three
  tasks.

---

## Contract impact

**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no viewer file was
touched. Scene schema v1 and protocol v1 are unchanged.

`RoomModel.heightM` already existed in scene schema v1 with exactly the
meaning S4 needed: "floor-to-ceiling height, valid range 2.0-4.0, 0 before
height capture." S4 is the first task to actually publish a non-zero value
into it; no schema change was needed to do that.

`RoomValidator.MinRoomHeightM` / `MaxRoomHeightM` are referenced directly by
`HeightCaptureController.ValidateHeight`, not restated, so the automatic and
manual paths — and the shared room-level check — can never drift apart on the
numeric bounds.

---

## How the capture works

```text
walls  = RoomGeometry.BuildWalls(room)          // from the S3 corners, in order
wall   = walls[selectedWallIndex]               // user-selected
plane  = WallGeometry.PlaneFor(wall)            // vertical, through wall.Start
ray    = provider.GetScreenRay(provider.CenterScreenPoint)   // AR world space
ghost  = frame.WorldRayToGhost(ray)             // Ghost-space ray
       -> intersect ghost ray with plane
       -> candidate = intersection.y
       -> reject if candidate < 2.0 or > 4.0
       -> RoomModel.heightM = candidate, revision++, republish snapshot
```

### Walls are generated, never detected
The S3 footprint is already exact — that is what closure verification bought
— so a vertical plane through two consecutive Ghost-space corners is exactly
where the real wall is, without waiting on ARKit to detect a vertical plane or
depending on LiDAR. `HeightCaptureController` never raycasts against a
detected plane.

### Distinguishing why a ray was rejected
`RayPlaneMath.TryIntersectPlane` collapses "parallel to the plane" and
"intersection behind the camera" into a single `false`. The plan's test list
asks for them separately, so `HeightCaptureController` performs the same
two-step check inline (`RayPlaneMath.ParallelEpsilon`, then `Plane.Raycast`'s
own distance) instead of calling `TryIntersectPlane` and losing which one
fired. This is not a second implementation of a validation *rule* — the
numeric epsilon and the raycast math are still `RayPlaneMath`'s own constant
and `UnityEngine.Plane`'s own method — it only exposes which branch of one
existing check took effect.

### The frame and footprint are read, never written
`HeightCaptureController` holds `ISpatialProvider`, `FloorLockController` and
`CornerCaptureController` purely to read `Frame`, `IsComplete` and
`CopyCorners()`. It has no setter path onto any of them. Two tests pin it: the
frame survives by reference and the corners survive by value across a height
capture.

### The manual fallback
Per implementation plan section 8.7, "a failed automatic height capture must
never block the demo." `TrySetManualHeight` runs the identical range check and
is available in `CaptureHeight` regardless of wall selection or aim. It is not
written to the wire: scene schema v1 has no manual-entry flag, and this task
does not need one, so `HeightCaptureController.IsManualEntry` is HUD-visible
only.

### Wall-span checking is informational, not a gate
`TryProjectCrosshairToWall`'s `withinWallSpan` output tells the HUD whether
the aim currently falls within (or near) the selected wall's horizontal
extent, for the "is my crosshair actually on this wall" readout the plan asks
for. It does **not** block `TryCaptureHeight`: a user aiming slightly past a
wall's endpoint at the ceiling line is still describing that wall's height,
not a different one, and the plan's own test list has no such rejection case.

---

## How to test

### EditMode tests
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s4-final.xml -logFile /tmp/ghostmap-s4-final.log
```

### iOS build — still two steps, still cannot be collapsed
```bash
U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity

$U -batchmode -nographics -projectPath apps/scanner \
   -executeMethod GhostMap.Scanner.Editor.ScannerBuild.ConfigureXr -quit \
   -logFile /tmp/ghostmap-s4-configurexr.log

$U -batchmode -nographics -projectPath apps/scanner \
   -executeMethod GhostMap.Scanner.Editor.ScannerBuild.BuildScanner -quit \
   -logFile /tmp/ghostmap-s4-build.log
```

```bash
cd apps/scanner/Builds/iOS
xcodebuild -project Unity-iPhone.xcodeproj -target Unity-iPhone \
  -configuration Release -sdk iphoneos CODE_SIGNING_ALLOWED=NO
```

### Rebuilding the scene after a UI change
```bash
$U -batchmode -nographics -projectPath apps/scanner \
   -executeMethod GhostMap.Scanner.Editor.ScannerSceneBuilder.BuildScene -quit \
   -logFile /tmp/ghostmap-s4-scene.log
```

---

## Test results

All executed and observed on Unity `6000.3.24f1`.

### EditMode — passing
**292 tests, 292 passed, 0 failed, 0 skipped.** Unity exit code `0`.

| Fixture | Passed |
| --- | --- |
| `HeightCaptureControllerTests` | 18 |
| `ScanWorkflowControllerTests` | 39 |
| `CornerCaptureControllerTests` | 37 |
| `GhostFrameTests` | 20 |
| `FloorLockControllerTests` | 17 |
| `ScannerXrSettingsTests` | 4 |
| `ScannerSceneTests` | 1 |
| `GhostMap.Shared.Tests` | 156 |

S3 finished at 266. S4 adds 26 (18 controller + 8 workflow).

### Mutation checks — the new tests were verified to bite
| Mutation | Result |
| --- | --- |
| Replace `frame.WorldRayToGhost(worldRay)` with the raw world ray (both call sites) | **14 failed** — every test built on the hostile fixture frame (floor 1.4 m below Unity's origin, 40° yaw): all four validation-range tests, both ray-rejection tests, both immutability tests, the winding-independence-adjacent camera-offset test, the manual-override test, the wall-span projection test, and the workflow snapshot test |
| Force the parallel-ray branch to never fire (`if (false)` in place of the `RayPlaneMath.ParallelEpsilon` comparison) | **1 failed** — exactly `RayParallelToTheWallIsRejected` |

Both mutations were reverted after observing the failures; the clean suite was
re-run afterward and confirmed back at 292/292.

The first mutation's 14-of-18 hit rate (not 18-of-18) is itself informative:
the four tests it did **not** catch are `WallsAreDerivedFromTheFourCapturedCornersInOrder`,
`NoWallsExistBeforeAllFourCornersAreCaptured`, `ManualHeightWithinRangeIsAccepted`
and `ManualHeightOutsideRangeIsRejectedAndDoesNotMutateState` — none of which
touch the camera ray at all, which is exactly the expected shape for a mutation
in ray-to-Ghost conversion.

### Build
- `ScannerBuild.ConfigureXr` — exit `0`.
- `ScannerBuild.BuildScanner` — exit `0`, Xcode project written.
- `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO` — **BUILD SUCCEEDED**.

### Shared package, run standalone
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s4-shared.xml -logFile /tmp/ghostmap-s4-shared.log
```
**156 tests, 156 passed, 0 failed, 0 skipped.** Unchanged from S3, confirming
S4 touched nothing under `shared/`.

### Physical device
**Run on a real iPhone against the build produced from `1b8a6ce`. Passed.**
See "Physical-device test procedure — run, passed" below.

---

## Physical-device test procedure — run, passed

Observed on a real iPhone against the build produced from `1b8a6ce`:

- `Phase` reached `CaptureHeight` after the S3 closure
- wall selection worked: cycling `Wall N/4` moved the highlighted wall
- aiming the center crosshair at the selected wall's ceiling junction produced
  a valid candidate height
- **candidate/captured height was 2.69 m**
- `Capture Height` succeeded and advanced `Phase` to `AddOpenings`
- `Frame O`, `Frame X` and `Frame Z` stayed fixed through height capture
- the S3 corner markers stayed fixed through height capture
- `Session` stayed `SessionTracking` throughout
- the measured room height agreed with the captured 2.69 m within acceptable
  tolerance

That covers the golden path of the procedure below, and confirms on hardware
what the automated tests assert off it: walls are derived correctly from the
S3 footprint, the ray/plane intersection lands on the real ceiling line, the
S2 frame and S3 corners are untouched by height capture, and a valid height
advances the phase.

**Not exercised on this device pass** — still covered only by EditMode
tests: the manual height fallback, an out-of-range automatic candidate (red
marker / rejected capture), aiming at a wall other than the one selected, and
a non-rectangular room. See "Known issues" below.

---

## The procedure that was run

Deploy the Xcode project at `apps/scanner/Builds/iOS` (a signing team must be
selected by hand) to the same room used for S1-S3, or any rectangular room
measured with a tape measure first.

### 1-4. Lock the floor, capture four corners, verify closure — unchanged from S2/S3
Follow the S3 handoff's procedure through a successful closure. `Phase`
becomes `CaptureHeight`.

### 5. What you should see entering CaptureHeight
- The corner-capture button row and its readout are still visible but
  inactive (the primary button there only responds in `FloorLocked`,
  `CaptureCorners` and `VerifyClosure`).
- A new readout above it shows `Wall 1/4: <cornerId>-><cornerId> (L m)`, an
  `Aim` line, and — once a height is captured — `Room height H m (auto|manual)`.
- Two new buttons appear: **Wall 1/4** (cycles the selection) and
  **Capture Height**.
- A manual height field and **Use Manual Height** button also appear.
- A yellow line should be visible in the room along the floor of the
  currently selected wall (wall 1, between corners 1 and 2, to start).

### 6. Select a wall
Press **Wall N/4** to cycle. The yellow line should jump to a different real
wall each time, and the label should advance `1/4 -> 2/4 -> 3/4 -> 4/4 -> 1/4`.
Confirm the highlighted line always sits on a real wall of the room, not
floating or on the wrong wall.

### 7. Aim at the ceiling/wall line
Stand back so the whole wall is visible and aim the center crosshair at the
line where the selected wall meets the ceiling. Watch the readout's `Aim`
line — it should update live with `u=<along-wall> h=<height>`, `on wall` or
`off wall`, and `valid` or `out of range`. The aim marker (a small sphere)
should appear on the real ceiling line, colored green when the candidate is
in the 2.0-4.0 m range and red otherwise.

### 8. Capture
Once the aim marker is green and sitting on the visible ceiling line, press
**Capture Height**. Expect:
- `Phase` advances to `AddOpenings`;
- the readout shows `Room height H m (auto)`;
- `H` should be within a few centimeters of a tape-measure reading of the same
  wall, floor to ceiling.

### 9. Also exercise
- **Wrong wall.** Select a wall you are not standing near and try to aim at
  its ceiling line from across the room — confirm the marker still lands
  correctly on that wall's plane, not the nearby one you are actually facing.
- **Out-of-range height.** If the room is close to 2.0 or 4.0 m, try aiming
  slightly above the real ceiling line or at a much lower point, and confirm
  **Capture Height** does nothing and the readout shows a rejection rather
  than a wrong number.
- **Manual fallback.** Type a value like `2.60` into the height field and
  press **Use Manual Height** without touching the automatic controls.
  `Phase` should still advance to `AddOpenings` and the readout should read
  `(manual)`.
- **Manual out of range.** Type `1.5` or `5.0` and press **Use Manual
  Height** — confirm it is rejected and the phase does not advance.
- **Frame and corners intact.** Check the S2 floor-lock readout's `Frame O`,
  `Frame X`, `Frame Z` are unchanged from before height capture, and (if
  visible) the S3 corner markers have not moved.

### 10. Report as a failure
- the yellow wall line not sitting on a real wall, or jumping to the wrong
  wall when cycling;
- the aim marker floating off the real ceiling line, or landing on a
  different wall than the one selected;
- a captured height that disagrees with a tape measure by more than a few
  centimeters;
- `Capture Height` doing nothing when the aim marker is green;
- `Capture Height` succeeding when the aim marker is red;
- `Frame O/X/Z` or any corner marker changing during height capture;
- the manual height field not accepting typed numeric input on the iOS
  keyboard;
- the app crashing or freezing entering `CaptureHeight`.

---

## Known issues

### New in S4
- **The screen is even more crowded**: four readouts, seven buttons and an
  input field. Bring-up instrumentation, not the capture UI; Task S6 owns the
  real one.
- **Wall selection is text-and-line, not a rendered top-down mini preview.**
  Implementation plan section 8.7 describes "numbered walls in a simple
  top-down mini preview." The yellow world-space line plus the `Wall N/4`
  label satisfy the same requirement — unambiguous wall identification — at
  much lower implementation cost. A real 2D floorplan preview is left for
  Task S6.
- **The aim marker's color reflects range validation only**, not whether the
  aim is within the wall's span. `withinWallSpan` is readout-text-only.
- **The manual height field commits immediately on button press.** There is
  no confirmation step beyond the 2.0-4.0 m range check.

### Not covered by the S4 device test
The S4 hardware run exercised the golden path on a rectangular room: locking
the floor, capturing four corners, an accepted closure, selecting a wall,
aiming at its ceiling line, and a successful automatic capture (2.69 m). These
paths are covered by EditMode tests but have **not** been seen on a phone:

- the manual height fallback end to end, including the legacy `InputField`'s
  `DecimalNumber` content type on the iOS on-screen keyboard;
- an out-of-range automatic candidate — the red aim marker and a refused
  `Capture Height` press;
- aiming at a wall other than the one currently selected, to confirm the
  highlighted wall and the intersected plane agree;
- a non-rectangular four-corner room's wall planes (only the S3 rectangular
  fixture was used, both in tests and on this device pass).

None of these is suspected broken — each has a passing EditMode test, and the
ray/plane and range-validation logic was mutation-checked. They are recorded
because a passing test is not the same evidence as a passing phone.

### Carried forward from S1-S3, still true
- The iOS build is two steps and cannot be collapsed into one.
- The generated Xcode project has no signing team.
- `ScannerIosPostBuild` carries the Xcode 26 `$(TOOLCHAIN_DIR)` workaround.
- `XROrigin.m_CameraYOffset` stays at `1.1176`; the frame — and now height
  capture — is immune to it structurally, since neither reads a local
  position.
- There is no reset. A new frame means relaunching the app.

---

## Files most important to read next
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/HeightCaptureController.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Workflow/ScanWorkflowController.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Tests/EditMode/HeightCaptureControllerTests.cs`
- `shared/com.ghostmap.shared/Runtime/Geometry/WallGeometry.cs`
- `shared/com.ghostmap.shared/Runtime/Geometry/RayPlaneMath.cs`
- `docs/plans/ghostmap-implementation-plan.md` sections 8.5-8.7 and Task S4

## Next task
**S4 is complete and verified.** The scanner workstream may proceed to
**S5 — doors, windows, furniture**, per
`docs/plans/ghostmap-implementation-plan.md`. S5 has not been started in this
handoff.
