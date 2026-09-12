# Shared Status

## Current state
- F0 complete. Repository scaffold, collaboration docs, ADRs, and status/handoff
  conventions are in place.
- `shared/com.ghostmap.shared/` directory tree exists but is **empty of code**.
  The package manifest, assembly definitions, DTOs, geometry, validation, and
  protocol are produced by F1–F3 and do not exist yet.
- No shared contract is frozen yet. `docs/contracts/scene-schema-v1.md` is
  produced by F1; `docs/contracts/protocol-v1.md` is produced by F3.

## Last verified commit
- `F0 commit — see docs/handoffs/2026-09-12-foundation-f0-repo-scaffold.md`

## Tests run
- None. F0 produces no compilable code, so there is nothing to test.
- Verification for F0 is structural: see the F0 acceptance section of the handoff.

## Interfaces consumed
- None yet.

## Known issues
- **No C# toolchain is installed on the current development machine.** No Unity
  `6000.3.24f1`, no Unity Hub, no .NET SDK, no Mono. `~/.dotnet` contains only
  orphaned 8.0.318 sentinel files. Unity EditMode tests required by F1, F2 and F3
  **cannot be executed** until Unity is installed. `python3` and `git` are
  available.

## Next safe task
- **F1: Create shared local package and data schema.**
  Write failing EditMode serialization/data tests first, then implement
  `Runtime/Domain/*.cs`, then document `docs/contracts/scene-schema-v1.md`.
  Blocked on installing Unity `6000.3.24f1` before its tests can be run.

## Do not touch
- Nothing is currently being changed by another workstream.
- Scanner (`S*`) and Viewer (`V*`) work must not begin until F0–F3 are merged.
