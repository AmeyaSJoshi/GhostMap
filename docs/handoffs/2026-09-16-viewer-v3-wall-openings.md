# Handoff

## Branch
`viewer/v3-wall-openings`

## Base commit
`9346252` (merge of PR #8, `viewer/v2-room-geometry` -> `main`; Scanner S1-S6 and Viewer V1-V2 complete)

## Head commit
_(recorded in the follow-up docs commit)_

## What changed
Task V3 from `docs/plans/ghostmap-implementation-plan.md` section 17: doors and
windows rendered as real openings in the room walls.

- `Runtime/Rendering/WallSliceGenerator.cs` — **new**. The grid-cut wall
  segmentation from implementation plan section 12.3, as a pure static
  function with the signature the plan specifies:
  `BuildSlices(float wallLengthM, float wallHeightM, IReadOnlyList<OpeningModel> openings)`.
  Horizontal cuts at `0`, wall length, and each opening's near/far edge;
  vertical cuts at `0`, room height, and each opening's sill/head; cuts sorted
  and epsilon-deduped; each cell between adjacent cuts kept unless its centre
  lies inside an opening. The result tiles the solid part of the wall exactly
  once — no overlaps, no gaps, no duplicate pieces — which is asserted directly
  by a 61x61 interior-sample coverage check, not merely by an area total.
  **No runtime CSG**: mesh booleans are fragile, platform-dependent and
  expensive to re-run on every accepted snapshot, and the plan explicitly calls
  for the grid decomposition instead.
- `Runtime/Rendering/WallRenderer.cs` — matches each `OpeningModel` to its wall
  by **ordered** corner pair (the reverse pair deliberately does not match:
  accepting it would silently mirror the opening to the far end of the wall,
  because `offsetM` is measured from the start corner), then places each
  wall-local `WallSlice` in world space as a `WallSegmentSpec`. The wall's
  local frame has its origin at the start corner on the floor, `u` along
  `WallDefinition.Tangent`, `v` along world up. V2's full-wall
  `Position`/`Rotation`/`Scale` are retained on `WallRenderSpec` unchanged, and
  a wall with no openings produces exactly one segment whose transform is
  identical to them — asserted by
  `WallWithoutOpeningsRendersExactlyWhereV2RenderedIt`.
- `Runtime/Rendering/RoomRenderer.cs` — a wall GameObject
  (`Wall_<i>_<startId>_<endId>`) is now a **container** of `Segment_<j>` cuboid
  children rather than being the cuboid itself. Every segment keeps the
  `BoxCollider` that `GameObject.CreatePrimitive` supplies, so V5's measurement
  raycasts still hit wall geometry. Diagnostics from the renderer's failsafe
  path are logged via `Debug.LogWarning`, never swallowed.
- `Tests/EditMode/WallSliceGeneratorTests.cs` (30),
  `WallRendererOpeningsTests.cs` (17), `RoomRendererOpeningsTests.cs` (12) —
  **new**, 59 tests.
- `Tests/EditMode/RoomRendererTests.cs` — two V2 tests updated for the new
  wall-container hierarchy (see "Deliberate V2 behaviour changes").

No new package dependency. No scene change. No `ViewerBootstrap` change.

## Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no
`apps/scanner/**` file was touched. All coordinate behaviour is unchanged from
V2 — no ARKit transform, no `CameraYOffset`, no XR Origin transform, no extra
normalization. Openings are cut in the exact wall generated from the ordered
corner pair.

## Deliberate V2 behaviour changes
Two V2 tests asserted things that V3 legitimately invalidates:

- `ValidRoomFixtureProducesExpectedGeometry` read `localScale.x` off each wall
  child. Walls are now containers, so it reads the segment instead — and since
  `valid-room-v1.json` carries a door on wall `c0->c1`, it now asserts three
  full-length solid walls plus one segmented wall.
- `DoorWindowFixtureStillRendersFourSolidWalls` asserted that a room with a
  door and a window renders four *solid* cuboids. That was correct for V2 and
  is exactly what V3 exists to stop doing; it is renamed
  `DoorWindowFixtureRendersFourWallsMadeOfSolidSegments` and now asserts each
  wall exists and every segment is real geometry.

**Note for whoever reads the task brief**: the brief asked to "verify
`valid-room-v1` still renders four solid walls with no openings." That fixture
in fact contains one door (`door-1`, wall `c0->c1`, offset 1.2 m, width 0.9 m,
sill 0), so under V3 it correctly renders a doorway plus three solid walls.
This is the intended V3 behaviour, not a regression — V2 rendered it solid only
because V2 ignored openings entirely. Screenshot `10` below shows the doorway.

## How to test
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/viewer \
  -runTests -testPlatform EditMode \
  -testResults /tmp/viewer-tests.xml -logFile /tmp/viewer-tests.log

/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/shared-tests.xml -logFile /tmp/shared-tests.log
```
(Omit `-nographics` for rendering/screenshot work — see "Known issues".)

## Test results
- **Viewer project: 279 tests, 279 passed, 0 failed, 0 skipped.** Unity exit
  code `0`. That is 156 embedded `GhostMap.Shared.Tests` (unchanged) + 64
  V1/V2 Viewer tests (two V2 tests updated as above) + **59 new V3 tests**:
  30 `WallSliceGeneratorTests`, 17 `WallRendererOpeningsTests`,
  12 `RoomRendererOpeningsTests`.
- **Shared `TestProject`: 156 tests, 156 passed, 0 failed, 0 skipped.** Unity
  exit code `0`. (The shared package is untouched by V3; run for completeness.)
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.

Every item the task brief required is covered:

| Required | Test |
| --- | --- |
| door removes correct wall region | `DoorRemovesExactlyTheDoorRegion` |
| window removes correct wall region | `WindowRemovesExactlyTheWindowRegion` |
| door sill at 0 | `DoorWithZeroSillHasNoSliceBelowIt` |
| window sill > 0 | `WindowLeavesSillSegmentBelowIt`, `WindowSegmentsSitAtTheExpectedHeights` |
| correct opening width | `OpeningWidthIsHonouredExactly` |
| correct opening height | `OpeningHeightIsHonouredExactly` |
| correct horizontal offset | `HorizontalOffsetIsMeasuredFromWallStart` |
| opening tied to correct ordered wall | `DoorCutsOnlyTheWallItNames` |
| reversed wall pair does not mirror | `ReversedCornerPairDoesNotCutTheWall` |
| wall with no openings identical to V2 | `WallWithoutOpeningsRendersExactlyWhereV2RenderedIt` |
| single door solid regions | `DoorLeavesSideAndHeaderSegments` |
| single window solid regions | `WindowLeavesSillSegmentBelowIt` |
| two openings on one wall | `TwoNonOverlappingOpeningsOnOneWallBothCutThrough`, `TwoOpeningsOnOneWallBothCutIt` |
| openings on separate walls independent | `OpeningsOnSeparateWallsRemainIndependent` |
| no overlapping wall segments | `AssertNoOverlaps` in every coverage test |
| no gaps outside the opening | `AssertExactCoverage` (61x61 interior samples) |
| solid area == wall area - opening area | `SolidAreaEqualsWallAreaMinusOpeningAreas` |
| update replaces prior segmentation | `AddingAnOpeningReplacesThePriorWallSegmentation`, `RemovingAnOpeningRestoresTheSolidWall` |
| duplicate snapshot does not duplicate | `DuplicateSnapshotDoesNotDuplicateSegments` |
| stale snapshot does not alter geometry | `StaleSnapshotDoesNotAlterCurrentGeometry` |
| malformed opening does not crash | `MalformedOpeningLeavesTheWallSolidWithoutThrowing`, `RejectedOpeningLeavesThePreviousRoomOnScreen`, and 8 generator-level rejection tests |
| fixture produces expected openings | `DoorWindowFixtureProducesTheExpectedOpenings`, `DoorWindowFixtureRendersBothOpenings` |

Test-driven: all three new test files were written and run **before**
`WallSliceGenerator` existed and before `WallRenderer` grew `Segments`; the
first run failed to compile, as intended.

**Real defect found by the tests — in the tests, not the implementation.** The
first version of `AssertExactCoverage` sampled on a grid whose points landed
exactly on cut lines (`u = 0.9`, `2.1`, `3.1`), where two adjacent slices
legitimately share an edge and an opening's own boundary belongs to neither the
hole nor the wall. Six tests failed with "covered 2 times" / "inside an opening
but covered." The independent overlap and area assertions passed throughout,
which is what identified it as a test artifact rather than a segmentation bug.
Fixed by skipping samples within `1e-3` of any cut line and asserting a minimum
interior sample count so the check cannot silently degrade to nothing.

## Visual verification
Captured the same way as V2 (a throwaway EditMode test, not committed, driving
the real `RoomRenderer`/`WallRenderer`/`WallSliceGenerator` production code
through `FixtureLoader` + `ViewerSceneStore` and rendering to an offscreen
`RenderTexture`), run with `-batchmode` **without** `-nographics`.

- **`01-doorwindow-roomcamera`** — `room-with-door-window-v1.json` from the
  scene's own saved `RoomCamera` transform `(2, 10, -3)` looking at
  `(2, 0, 1.5)`. Both openings are visible: the door as a notch in the near
  wall (`c0->c1`) revealing the brown floor, and the window as a dark slot in
  the right-hand wall (`c1->c2`). Floor intact, shell intact, no cracks.
- **`07-doorwindow-interior-door`** — interior view facing wall `c0->c1`. A
  clean full-height doorway: dark background straight through the wall, a
  header segment above it, side segments left and right, bottom edge flush with
  the floor. No cracks, no overlap seams.
- **`08-doorwindow-interior-window`** — interior view facing wall `c1->c2`. A
  clean window hole with a **sill segment below it** (correct positive-sill
  placement: the hole starts at 0.9 m, not at the floor), a header segment
  above (hole ends at 2.0 m, wall continues to 2.5 m), and wall on both sides.
- **`09-doorwindow-interior-solid-walls`** — interior view of walls `c2->c3`
  and `c3->c0`, which carry no openings: both fully solid, meeting at a clean
  corner, ceiling above and floor below.
- **`10-validroom-interior-door`** — `valid-room-v1.json` from inside: its one
  door renders as a doorway, the rest of the shell is solid. (See the note
  above about this fixture containing a door.)

Exterior elevation views (`02`-`05`) also render correctly but read less
clearly, because with flat `Unlit/Color` materials an opening seen from outside
shows the identically-coloured far wall through it rather than contrasting
background. That is a shading artifact of the MVP's unlit materials, not a
geometry defect, and the interior views above disambiguate it.

## Known issues
- **A window seen from outside is hard to read** with the current flat unlit
  materials: through the hole you see the opposite wall, in the same colour.
  Geometry is correct (proved by the interior captures and by the area/coverage
  tests); this is purely a shading limitation. Lit materials or a distinct
  interior/exterior shade would fix it, and neither is in V3's scope.
- **Reveals are not modelled.** An opening is cut through the full `0.10 m`
  wall thickness with square edges; there are no returns/jambs. The plan's
  section 12.3 does not ask for them.
- The grid decomposition is not minimal — a wall with one door yields five
  segments (left column x2, right column x2, header) where three rectangles
  would suffice. This is exactly what the plan specifies, it is deterministic,
  and at MVP room sizes the extra draw calls are irrelevant. Do not "optimize"
  it into a merge pass without a reason.
- `WallSliceGenerator`'s own overlap rule is true rectangle overlap (two
  openings at the same horizontal span but disjoint vertical spans are both
  cut). `OpeningValidator` is **stricter** — it rejects any horizontal overlap
  on the same wall regardless of height — and runs first, so the permissive
  case cannot arrive through the network path. The generator is a rendering
  failsafe, not a second source of truth.
- Wall segments inherit V2's butt-jointed corners (no mitres); unchanged.
- Carried over from V2, unchanged and still true: `-executeMethod` hangs in
  this sandbox's headless Unity (use `-runTests` with a throwaway test);
  `-nographics` segfaults on `Camera.Render()`; `RoomCamera` is a fixed
  framing that V4 supersedes.
- No physical-device testing applies to this workstream (desktop app).

## Files most important to read next
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Rendering/WallSliceGenerator.cs`
  — the whole of V3's geometry logic, and a pure function V4+ can reuse.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Rendering/RoomRenderer.cs` —
  the `Walls/Wall_i/Segment_j` hierarchy V4's furniture and V5's selection
  raycasts will sit alongside.
- `docs/status/viewer.md` — full V3 design notes and known issues.

## Next task
- **V4 — Parametric furniture + orbit/dollhouse camera** (implementation plan
  section 17, Task V4). Not started. Do not begin Integration work from here.
