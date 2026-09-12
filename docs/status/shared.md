# Shared Status

## Current state
- **F0, F1, F2 and F3 complete. The foundation gate passes.**
- `shared/com.ghostmap.shared` exists as a real Unity package: `package.json`,
  `Runtime/GhostMap.Shared.asmdef`, and `Tests/Editor/GhostMap.Shared.Tests.asmdef`.
- **Scene schema v1 is frozen.** All six domain types are implemented in
  `Runtime/Domain/`: `Vec3Dto`, `CornerModel`, `OpeningModel`,
  `SceneObjectModel`, `RoomModel`, `SceneSnapshot`.
- `docs/contracts/scene-schema-v1.md` is written and matches the code.
- `shared/TestProject/` is a minimal Unity host project that exists solely to run
  the shared package's EditMode tests. It contains no application code.
- **Capture geometry and validation are implemented.** `Runtime/Geometry/`
  holds `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry`, `WallGeometry`
  and `MeasurementMath`. `Runtime/Validation/` holds `RoomValidator`,
  `OpeningValidator` and `FurnitureValidator`. The support types
  `ValidationResult` and `WallDefinition` live in `Runtime/Domain/`.
- **Protocol v1 is frozen.** `Runtime/Protocol/` holds `ProtocolConstants`,
  `WireMessages`, `ProtocolSerializer` and `SnapshotRevisionPolicy`.
- `docs/contracts/protocol-v1.md` is written and matches the code.
- `fixtures/` holds the three canonical fixtures; `tools/` holds
  `send_fixture.py` and `inspect_snapshot.py`.
- `docs/contracts/protocol-v1.md` is still a placeholder, produced by F3.

## Last verified commit
- `33d63fed8474c8146e959ec3a082a29a9b7d1f19` (F1)

## Tests run
- Command:
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
    -batchmode -nographics -projectPath shared/TestProject \
    -runTests -testPlatform EditMode -testResults <out>.xml -logFile <out>.log
  ```
- Result: **143 tests, 143 passed, 0 failed, 0 skipped.** Unity exit code `0`.
- Re-run after deleting `Library/`, confirming package resolution from a clean state.
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.
- Test-driven: each task's suite was written first and observed failing to
  compile before the implementation existed — F1 with the domain types absent,
  F2 with the `Geometry` and `Validation` namespaces absent.

## Interfaces consumed
- None. The shared package depends only on `UnityEngine` for `Vector3` and
  `JsonUtility`.

## Interfaces published

Domain (`GhostMap.Shared.Domain`):
- `Vec3Dto` — struct, with `ToVector3()` / `FromVector3()`.
- `CornerModel`, `OpeningModel`, `SceneObjectModel`, `RoomModel`, `SceneSnapshot`
- `ValidationResult` — `IsValid`, `Error`, `Valid()`, `Invalid(string)`
- `WallDefinition` — `StartCornerId`, `EndCornerId`, `Start`, `End`, `Tangent`, `LengthM`

Geometry (`GhostMap.Shared.Geometry`):
- `GhostCoordinateFrame` — `WorldToGhost`, `GhostToWorld`, `WorldDirectionToGhost`,
  `GhostDirectionToWorld`, `WorldRayToGhost`, `GhostRayToWorld`, `FloorWorldY`
- `RayPlaneMath` — `TryIntersectHorizontalPlane`, `TryIntersectPlane`
- `RoomGeometry` — `PolygonAreaXZ`, `SignedPolygonAreaXZ`, `HasSelfIntersectionXZ`,
  `BuildWalls`, `TryFindWall`, `InteriorAngleDeg`
- `WallGeometry` — `Normal`, `PlaneFor`, `ToWallLocal`, `FromWallLocal`, `ContainsSpan`
- `MeasurementMath` — `Distance`, `DistanceXZ`, `RoomAreaM2`, `RoomVolumeM3`, `RoomPerimeterM`

Protocol (`GhostMap.Shared.Protocol`):
- `ProtocolConstants` — `ProtocolVersion`, `SchemaVersion`, `Port`,
  `MaxLineLengthBytes`, `LineTerminator`, type constants, timing constants
- `WireMessageHeader`, `HelloMessage`, `HeartbeatMessage`, `PhonePoseMessage`,
  `SceneSnapshotMessage`, `ScanFinalizedMessage`
- `ProtocolSerializer` — `Serialize`, `SerializeLine`, `TryDeserialize`
- `SnapshotRevisionPolicy` — `Evaluate`, `ShouldApply`; `SnapshotAcceptance` enum

Validation (`GhostMap.Shared.Validation`):
- `RoomValidator` — `ValidateRoom`, `ValidateNewCorner`, `ClassifyClosure`, limit constants
- `OpeningValidator` — `Validate`
- `FurnitureValidator` — `Validate`, `SupportedTypes`, `IsSupportedType`, `TryGetDefaultDimensions`
- `ClosureQuality` — `Excellent`, `Acceptable`, `Rejected`

## Known issues
- None blocking.
- `shared/TestProject/` is not part of the layout in implementation plan section 3.
  It was added because a Unity package cannot run its own tests without a host
  project, and F1 explicitly sanctions "a tiny test Unity project". Rationale is
  recorded in `shared/TestProject/README.md` and the F1 handoff.
- No physical-device testing applies to this workstream.

## Next safe task
- **None in the foundation. F0-F3 are complete and the gate passes.**
- The Scanner (`S1`) and Viewer (`V1`) workstreams may now split and run in
  parallel. Both build against this frozen shared package.
- The next shared/integration work is reactive: handle contract-change requests
  from either workstream via the procedure in implementation plan section 27.

## Do not touch
- Once the workstreams split: `apps/scanner/**` belongs to Scanner and
  `apps/viewer/**` to Viewer. Do not edit either from this workstream.
- Any change to `shared/**`, `fixtures/**` or `docs/contracts/**` from an app
  workstream must follow the shared contract change procedure first.
