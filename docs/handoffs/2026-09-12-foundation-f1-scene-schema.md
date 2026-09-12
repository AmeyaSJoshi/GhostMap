# Handoff

## Branch
`main`

Foundation tasks run on `main` per implementation plan section 28, Stage A
(single integrator before the Scanner/Viewer split).

## Base commit
`76ddf3e43fe5c57de23ade76ed7e0cece6f552cd`

## Head commit
`PENDING — recorded in the follow-up docs commit`

## What changed

Task **F1: Create shared local package and data schema**.

- Created the shared Unity package:
  - `shared/com.ghostmap.shared/package.json`
  - `shared/com.ghostmap.shared/Runtime/GhostMap.Shared.asmdef`
  - `shared/com.ghostmap.shared/Tests/Editor/GhostMap.Shared.Tests.asmdef`
- Implemented scene schema v1 in `Runtime/Domain/`: `Vec3Dto`, `CornerModel`,
  `OpeningModel`, `SceneObjectModel`, `RoomModel`, `SceneSnapshot`.
- Wrote `Tests/Editor/SceneSchemaTests.cs` — 14 EditMode tests.
- Replaced the placeholder `docs/contracts/scene-schema-v1.md` with the frozen
  contract, including a worked JSON example and a verification table.
- Added `shared/TestProject/`, a minimal Unity host project for running the
  package's tests.

### Deviation from plan section 3, and why

Plan section 3 does not include a Unity project capable of running the shared
package's tests. A Unity package cannot run its own tests: the Test Runner only
discovers tests inside a project, and a package referenced by relative path must
additionally be listed in the project's `testables` array.

Options considered:

1. Use `apps/viewer` as the host — rejected. Creating that Unity project is Task
   V1, which is gated behind the foundation and belongs to another workstream.
2. Keep the host project outside the repository — rejected. The foundation gate
   requires "shared tests pass" to be verifiable from a clean clone by the other
   developer.
3. Add a minimal host project under `shared/` — **chosen.** F1 explicitly
   sanctions "a tiny test Unity project", and `shared/**` is already owned by the
   Shared/Integration workstream, so ownership stays consistent.

No schema, protocol, coordinate convention or architectural decision was changed.
`shared/TestProject/README.md` records the rationale in place.

## Contract impact

**Scene schema v1 is now frozen.** This is additive — no prior contract existed.

- `docs/contracts/scene-schema-v1.md` is authoritative and matches the code.
- The wire protocol is **not** frozen yet; that is F3.
- Any later change to these types is a shared contract change requiring an ADR,
  doc update, tests, dedicated commit, and status/handoff update.

## How to test

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-f1.xml \
  -logFile /tmp/ghostmap-f1.log
```

Exit code `0` means all tests passed. Inspect `/tmp/ghostmap-f1.xml` for
per-test results; check the log for `error CS` if the run aborted before testing.

## Test results

**Actually executed on Unity `6000.3.24f1`, EditMode. Unity exit code `0`.**

```text
total=14  passed=14  failed=0  skipped=0  inconclusive=0  result=Passed
```

All 14 passed:

```text
Vec3Dto_ConvertsToAndFromVector3
Snapshot_PreservesSchemaVersionAndRevision
Snapshot_PreservesSessionMetadata
Snapshot_PreservesFinalizedFlag
Snapshot_PreservesFourCornersInOrder
Snapshot_PreservesCornerPositions
Snapshot_AllCornersLieOnFloorPlane
Snapshot_PreservesOneDoor
Snapshot_PreservesThreeObjects
Snapshot_PreservesObjectTransformAndDimensions
Snapshot_PreservesObjectYaw
Snapshot_PreservesRoomIdentityAndHeight
Snapshot_DoesNotSerializeWalls
Snapshot_HandlesEmptyRoomAfterFloorLock
```

**Test-driven, verified red before green.** The suite was written first and run
against an empty `Runtime/Domain/`, producing compile failures
`CS0234: The type or namespace name 'Domain' does not exist` and
`CS0246: The type or namespace name 'SceneSnapshot' could not be found`, with
Unity exiting `1` and emitting no results file. The DTOs were implemented only
after that failure was observed. The suite was run a second time after aligning
the manifest's test-framework version with what Unity resolved, and stayed green.

## Known failures

None.

## Files most important to read next

1. `docs/contracts/scene-schema-v1.md` — the frozen schema.
2. `shared/com.ghostmap.shared/Runtime/Domain/` — the six DTOs.
3. `docs/plans/ghostmap-implementation-plan.md` section 15, Task F2 — the exact
   public interfaces F2 must deliver.
4. `docs/plans/ghostmap-implementation-plan.md` section 8 — coordinate frame and
   capture math, the specification F2 implements.
5. `docs/plans/ghostmap-implementation-plan.md` section 9 — room validation rules.
6. `shared/TestProject/README.md` — how to run the tests.

## Next task

**F2: Geometry + validation package.**

Test-first, in this order:

1. Write failing EditMode tests for: coordinate frame round trip under `1e-4`,
   horizontal ray intersection, parallel-ray rejection, rectangle area, bow-tie
   polygon rejection, wall lengths, too-short wall rejection, valid door, door
   extending outside the wall, valid window, opening above the ceiling.
2. Implement the support types `ValidationResult` and `WallDefinition`.
3. Implement `Runtime/Geometry/`: `GhostCoordinateFrame`, `RayPlaneMath`,
   `RoomGeometry`, `WallGeometry`, `MeasurementMath`.
4. Implement `Runtime/Validation/`: `RoomValidator`, `OpeningValidator`,
   `FurnitureValidator`.
5. Run the EditMode tests and confirm they pass.
6. Commit as `feat(shared): add capture geometry and validation`.

Scanner (`S*`) and Viewer (`V*`) tasks remain blocked until F0–F3 are merged and
the foundation gate passes.
