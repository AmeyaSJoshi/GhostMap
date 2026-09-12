# Viewer Status

## Current state
- Not started. **The foundation gate now passes, so this workstream is
  unblocked and may begin.**
- `apps/viewer/` contains only the empty directory skeleton created by F0. There
  is no Unity project, no `Packages/manifest.json`, no `ProjectSettings`, no
  scene, and no code.

## Last verified commit
- None.

## Tests run
- None.

## Interfaces consumed
- Available now from the frozen shared package. The viewer will consume from
  `com.ghostmap.shared`: the domain DTOs, `RoomGeometry.BuildWalls`,
  `WallGeometry`, `MeasurementMath`, `RoomValidator`, `OpeningValidator`, and
  `ProtocolSerializer`.

## Known issues
- None specific to this workstream yet.

## Next safe task
- **V1 — Viewer project + TCP server + fixture ingestion.** F0-F3 are merged and the foundation gate passes.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/scanner/**` — owned by the Scanner workstream.
