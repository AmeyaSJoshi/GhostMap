# Handoff

## Branch
`fix/scanner-r2-wall-planes`, stacked on `scanner/r9-hardening` (PR #37).

## Base commit
`8b2029b` (R9 docs)

## Head commit
The commit that adds this file.

## What changed
The first iPhone session (R2) hit the project's main risk: **the stand-in-place
scan does not find the room.** Two attempts on the owner's iPhone:

1. **Run 1** (`scanner/r9-hardening`): "planes 0" for a whole slow turn. The
   per-plane wall filter (1.5 m x 1.2 m) rejected every ARKit patch.
   `1a5c025` relaxed it:
   - unlabelled patches from 0.5 m wide that reach 1.3 m high now count;
   - labelled patches count from 0.3 m;
   - trust thresholds unchanged;
   - the YOLO model loads at launch and runs at 2 fps during the scan;
   - raw ARKit planes are logged.
2. **Run 2** (`1a5c025`): ARKit reported 7 vertical planes (4 labelled
   "wall"), but only **2 were trusted** (2.6 m and 1.8 m). It needs 4. The
   rest are 0.3-0.8 m wide.

This commit adds each wall's bearing and distance from the scan centre to the
log (`AutoRoomScanController.WallBearing`), so the next run shows which planes
are which wall. It also writes everything down for whoever continues:

- `docs/tasks/R2-auto-scan-walls.md`: the problem, code map, gates and
  values, hypotheses, next steps, done-when. **Start here.**
- `docs/research/r2-device-logs/2026-10-11-auto-scan.md`: both device logs.
- Pointers in `CLAUDE.md`, `README.md`, `docs/onboarding.md`,
  `docs/status/integration.md` (blocker banner, R2 row, R2 device log, next
  safe task), `docs/status/scanner.md`, `docs/tasks/README.md`,
  `docs/tasks/R2-first-device-session.md` and the plan.

## Contract impact
None.

## How to test
1. `./tools/run_unity_tests.sh scanner`
2. Device: `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj` is already built
   from this branch with the owner's signing team (`553SS7SNPX`) set. In Xcode,
   choose "Use Version on Disk" if asked, then press Run. Scan Room, then paste
   the `GhostMap auto:` lines (now with `bearing ... deg dist ... m`).

## Test results
Owner's Mac, 2026-10-11: scanner EditMode **859/859** (+6 wall-patch tests in
`1a5c025`, +4 bearing tests here). Device build regenerated, with no
project-settings drift.

## Known failures
- **On the device the stand-in-place scan still does not build the room.**
  That is the open problem; see the brief.

## Known limitations
- The owner's room size and the furniture along each wall are not recorded
  yet; the brief asks for them.
- R2's other steps (sweep, furniture, Finish, AirDrop, photos) have not been
  run on the device.

## Files most important to read next
- `docs/tasks/R2-auto-scan-walls.md`
- `docs/research/r2-device-logs/2026-10-11-auto-scan.md`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/WallPlaneAccumulator.cs` (thresholds, `IsTrusted`)
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/RoomFromWalls.cs` (`Select`)

## Next task
Owner: one more Scan Room with the bearing log, plus the room's tape size and
what is along each wall; then finish R2 with Help GhostMap. Agent: turn that log
into an EditMode test and tune the trust rule (brief, "Next steps").
