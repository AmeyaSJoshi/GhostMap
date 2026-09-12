# Shared Status

## Current state
- F0 and F1 complete.
- `shared/com.ghostmap.shared` exists as a real Unity package: `package.json`,
  `Runtime/GhostMap.Shared.asmdef`, and `Tests/Editor/GhostMap.Shared.Tests.asmdef`.
- **Scene schema v1 is frozen.** All six domain types are implemented in
  `Runtime/Domain/`: `Vec3Dto`, `CornerModel`, `OpeningModel`,
  `SceneObjectModel`, `RoomModel`, `SceneSnapshot`.
- `docs/contracts/scene-schema-v1.md` is written and matches the code.
- `shared/TestProject/` is a minimal Unity host project that exists solely to run
  the shared package's EditMode tests. It contains no application code.
- `Runtime/Geometry/`, `Runtime/Protocol/` and `Runtime/Validation/` are still
  empty. They are produced by F2 and F3.
- `docs/contracts/protocol-v1.md` is still a placeholder, produced by F3.

## Last verified commit
- `<F1 sha — see docs/handoffs/2026-09-12-foundation-f1-scene-schema.md>`

## Tests run
- Command:
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
    -batchmode -nographics -projectPath shared/TestProject \
    -runTests -testPlatform EditMode -testResults <out>.xml -logFile <out>.log
  ```
- Result: **14 tests, 14 passed, 0 failed, 0 skipped.** Unity exit code `0`.
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.
- Test-driven: the suite was written first and observed failing to compile
  (`CS0234`/`CS0246`, domain types absent) before the DTOs were implemented.

## Interfaces consumed
- None. The shared package depends only on `UnityEngine` for `Vector3` and
  `JsonUtility`.

## Interfaces published
- `GhostMap.Shared.Domain.Vec3Dto` — struct, with `ToVector3()` / `FromVector3()`.
- `GhostMap.Shared.Domain.CornerModel`
- `GhostMap.Shared.Domain.OpeningModel`
- `GhostMap.Shared.Domain.SceneObjectModel`
- `GhostMap.Shared.Domain.RoomModel`
- `GhostMap.Shared.Domain.SceneSnapshot`

## Known issues
- None blocking.
- `shared/TestProject/` is not part of the layout in implementation plan section 3.
  It was added because a Unity package cannot run its own tests without a host
  project, and F1 explicitly sanctions "a tiny test Unity project". Rationale is
  recorded in `shared/TestProject/README.md` and the F1 handoff.
- No physical-device testing applies to this workstream.

## Next safe task
- **F2: Geometry + validation package.**
  Write the EditMode tests first, then implement `Runtime/Geometry/*.cs` and
  `Runtime/Validation/*.cs`, including the `ValidationResult` and
  `WallDefinition` support types defined in the F2 section of the plan.

## Do not touch
- Nothing is currently being changed by another workstream.
- Scanner (`S*`) and Viewer (`V*`) work must not begin until F0–F3 are merged.
