# Scanner Status

## Current state
- Not started. **The foundation gate now passes, so this workstream is
  unblocked and may begin.**
- `apps/scanner/` contains only the empty directory skeleton created by F0. There
  is no Unity project, no `Packages/manifest.json`, no `ProjectSettings`, no
  scene, and no code.

## Last verified commit
- None.

## Tests run
- None.

## Interfaces consumed
- Available now from the frozen shared package. The scanner will consume from
  `com.ghostmap.shared`: `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry`,
  `RoomValidator`, `OpeningValidator`, `FurnitureValidator`, the domain DTOs, and
  `ProtocolSerializer`.

## Known issues
- None specific to this workstream yet.
- No physical iPhone test has been performed. Nothing in this workstream may be
  declared working on device until it actually runs on a real iPhone.

## Next safe task
- **S1 — Scanner Unity project + physical-device AR smoke test.** F0-F3 are merged and the foundation gate passes.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/viewer/**` — owned by the Viewer workstream.
