# Handoff

## Branch
`main`

Foundation tasks run on `main` per implementation plan section 28, Stage A.

## Base commit
`b34195c` (F1 sha recorded)

## Head commit
`PENDING — recorded in the follow-up docs commit`

## What changed

Task **F2: Geometry + validation package**.

Support types, in `Runtime/Domain/` per plan section 3:

- `ValidationResult` — `IsValid`, `Error`, `Valid()`, `Invalid(string)`.
- `WallDefinition` — `StartCornerId`, `EndCornerId`, `Start`, `End`, `Tangent`,
  `LengthM`. Tangent and length are derived in the constructor.

`Runtime/Geometry/`:

- `GhostCoordinateFrame` — immutable; normalizes its axes on construction so a
  non-unit axis cannot silently scale every converted distance.
- `RayPlaneMath` — `TryIntersectHorizontalPlane`, `TryIntersectPlane`. Both
  reject rather than extrapolate: a parallel, grazing, or behind-the-camera
  intersection returns false.
- `RoomGeometry` — `PolygonAreaXZ`, `SignedPolygonAreaXZ`,
  `HasSelfIntersectionXZ`, `BuildWalls`, `TryFindWall`, `InteriorAngleDeg`.
- `WallGeometry` — `Normal`, `PlaneFor`, `ToWallLocal`, `FromWallLocal`,
  `ContainsSpan`.
- `MeasurementMath` — `Distance`, `DistanceXZ`, `RoomAreaM2`, `RoomVolumeM3`,
  `RoomPerimeterM`.

`Runtime/Validation/`:

- `RoomValidator` — `ValidateRoom`, `ValidateNewCorner`, `ClassifyClosure`, and
  the limit constants.
- `OpeningValidator` — `Validate`.
- `FurnitureValidator` — `Validate`, `SupportedTypes`, `IsSupportedType`,
  `TryGetDefaultDimensions`.
- `ClosureQuality` enum — `Excellent`, `Acceptable`, `Rejected`.

Tests added: `CoordinateFrameTests`, `RayPlaneMathTests`, `RoomGeometryTests`,
`MeasurementMathTests`, `RoomValidatorTests`, `OpeningValidatorTests`,
`FurnitureValidatorTests`.

### Design decisions worth knowing

These resolve genuine ambiguities in the plan. None changes a documented
contract; all are additive.

1. **`ValidateRoom` validates a room as far as it has been captured.** Live
   snapshots arrive mid-scan with zero corners, a partial corner chain, or
   `heightM == 0`. None of those is an error, so structural rules apply only once
   the data they govern exists. Without this the viewer would reject every
   snapshot sent before height capture. `heightM == 0` specifically means "not
   captured yet"; any other out-of-range height is rejected.

2. **`PolygonAreaXZ` returns absolute area; `SignedPolygonAreaXZ` is separate.**
   The plan states the area rule in absolute terms ("absolute polygon area >= 2.0
   m²"), but the viewer needs winding to orient floor triangles (plan section
   12.1). Both are provided rather than overloading one return value with two
   meanings.

3. **`TryFindWall` matches the corner pair in declared order.** An opening's
   `offsetM` is measured from its declared start corner, so a reversed pair would
   silently mirror the opening along the wall. Matching in order makes a reversed
   reference a loud failure instead of a wrong doorway.

4. **The closing corner is treated as a neighbor of corner 0.** When the fourth
   corner is captured it forms a wall back to the first, so it is held to the
   0.50 m wall minimum rather than the 0.20 m non-neighbor separation.

5. **`ValidateNewCorner` checks self-intersection when closing the polygon** but
   leaves area and interior angles to `ValidateRoom`. This rejects a bow-tie at
   the moment of capture, when the user can still act on it.

6. **Furniture dimension bounds are 0.05 m to 5.0 m.** The plan says "positive
   and within defined sane bounds" without giving numbers. The lower bound admits
   a TV's 0.10 m depth; the upper bound admits a 2.10 m couch with margin.

### Deviation from plan section 3

Plan section 3 lists four shared test files. This task adds three more —
`MeasurementMathTests`, `RoomValidatorTests`, `FurnitureValidatorTests` — because
`RoomValidator`, `FurnitureValidator` and `MeasurementMath` are all required by
F2 and plan section 3's list gives them no home. Folding room-validation tests
into `RoomGeometryTests` would have made the failure output harder to read. This
is additive; no listed file was renamed or removed.

## Contract impact

**None.** No schema, protocol, coordinate convention or architectural decision
changed. Scene schema v1 is untouched.

The validation limits implemented here match the table already published in
`docs/architecture/overview.md` section 9 and the ranges stated in
`docs/contracts/scene-schema-v1.md`; both were checked field by field and needed
no edit.

## How to test

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-f2.xml \
  -logFile /tmp/ghostmap-f2.log
```

## Test results

**Actually executed on Unity `6000.3.24f1`, EditMode. Unity exit code `0`.**

```text
total=114  passed=114  failed=0  skipped=0  result=Passed
```

That is the whole shared suite: 14 schema tests from F1 plus 100 new geometry and
validation tests.

Every test the plan's F2 section requires is present and passing:

| Required by plan | Test |
| --- | --- |
| coordinate frame round trip < 1e-4 | `WorldToGhost_ThenGhostToWorld_RoundTripsWithinTolerance` |
| horizontal ray intersection | `HorizontalPlane_IntersectsWhenAimingDown` |
| reject parallel ray | `HorizontalPlane_RejectsParallelRay`, `ArbitraryPlane_RejectsParallelRay` |
| rectangle area | `PolygonArea_ComputesRectangleArea` |
| reject bow-tie polygon | `SelfIntersection_RejectsBowTie` |
| wall lengths | `BuildWalls_ComputesLengths` |
| reject too-short wall | `Room_RejectsWallShorterThanMinimum` |
| accept valid door | `AcceptsValidDoor` |
| reject door outside wall | `RejectsDoorExtendingPastWallEnd` |
| accept valid window | `AcceptsValidWindow` |
| reject opening above ceiling | `RejectsOpeningTallerThanRoom`, `RejectsWindowWhoseTopExceedsCeiling` |

**Test-driven, verified red before green.** The suite was written first and run
against empty `Geometry/` and `Validation/` folders, producing
`CS0234: The type or namespace name 'Geometry' does not exist`,
`CS0234: ...'Validation' does not exist` and
`CS0246: ...'GhostCoordinateFrame' could not be found`, with Unity exiting `1`
and emitting no results file.

One test was corrected during this task. `Room_RejectsExtremeInternalAngle`
originally used corners whose interior angles were all between 74° and 107°, so
it would have passed for the wrong reason — the room was actually valid. The
corners were replaced with a sliver quad whose angles are 8.5° and 175.6°, and
the replacement was verified numerically to pass the area check (4.35 m²) and all
four wall-length checks, so it fails on the angle rule alone and nothing else.

## Known failures

None.

## Files most important to read next

1. `docs/plans/ghostmap-implementation-plan.md` section 7 — protocol v1, the
   exact specification F3 implements.
2. `docs/contracts/scene-schema-v1.md` — the payload protocol v1 carries.
3. `shared/com.ghostmap.shared/Runtime/Domain/SceneSnapshot.cs` — the snapshot
   type the wire format wraps.
4. `docs/decisions/ADR-0002-snapshot-protocol.md` — why synchronization is
   full-snapshot rather than delta replay.
5. `shared/com.ghostmap.shared/Runtime/Validation/RoomValidator.cs` — the
   validator the viewer runs before applying a received snapshot.

## Next task

**F3: Protocol v1 + fixtures.**

Test-first, in this order:

1. Write failing EditMode tests: serialize/deserialize each message type, reject
   unknown protocol version, reject unknown message type, snapshot round trip,
   stale-revision helper, fixture parses and validates.
2. Implement `Runtime/Protocol/`: `ProtocolConstants`, `WireMessages`,
   `ProtocolSerializer`.
3. Create `fixtures/valid-room-v1.json` (4.0 x 3.0 m room, 2.5 m height, 1 door,
   bed, desk, chair), `fixtures/room-with-door-window-v1.json`, and
   `fixtures/malformed-room-v1.json`.
4. Write `tools/send_fixture.py` and `tools/inspect_snapshot.py`.
5. Replace the `docs/contracts/protocol-v1.md` placeholder.
6. Run the EditMode tests and confirm they pass.
7. Commit as `feat(shared): freeze protocol v1 and sample fixtures`.

After F3, the **foundation gate** applies: shared tests pass, fixture parses,
protocol docs match code, and both developers pull the commit. Only then may the
Scanner (`S*`) and Viewer (`V*`) workstreams split.
