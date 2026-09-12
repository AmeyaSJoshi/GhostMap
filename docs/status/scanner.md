# Scanner Status

## Current state
- Not started. Blocked by the foundation gate.
- `apps/scanner/` contains only the empty directory skeleton created by F0. There
  is no Unity project, no `Packages/manifest.json`, no `ProjectSettings`, no
  scene, and no code.

## Last verified commit
- None.

## Tests run
- None.

## Interfaces consumed
- None yet. Once F1–F3 land, the scanner will consume from
  `com.ghostmap.shared`: `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry`,
  `RoomValidator`, `OpeningValidator`, `FurnitureValidator`, the domain DTOs, and
  `ProtocolSerializer`.

## Known issues
- No Unity installation on the current development machine.
- No physical iPhone test has been performed. Nothing in this workstream may be
  declared working on device until it actually runs on a real iPhone.

## Next safe task
- **Blocked.** Do not start `S1` until F0, F1, F2 and F3 are all merged and the
  foundation gate in the implementation plan passes.
- First task after the gate: **S1 — Scanner Unity project + physical-device AR
  smoke test.**

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/viewer/**` — owned by the Viewer workstream.
