# Viewer Status

## Current state
- Not started. Blocked by the foundation gate.
- `apps/viewer/` contains only the empty directory skeleton created by F0. There
  is no Unity project, no `Packages/manifest.json`, no `ProjectSettings`, no
  scene, and no code.

## Last verified commit
- None.

## Tests run
- None.

## Interfaces consumed
- None yet. Once F1–F3 land, the viewer will consume from
  `com.ghostmap.shared`: the domain DTOs, `RoomGeometry.BuildWalls`,
  `WallGeometry`, `MeasurementMath`, `RoomValidator`, `OpeningValidator`, and
  `ProtocolSerializer`.

## Known issues
- No Unity installation on the current development machine.

## Next safe task
- **Blocked.** Do not start `V1` until F0, F1, F2 and F3 are all merged and the
  foundation gate in the implementation plan passes.
- First task after the gate: **V1 — Viewer project + TCP server + fixture
  ingestion.**

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/scanner/**` — owned by the Scanner workstream.
