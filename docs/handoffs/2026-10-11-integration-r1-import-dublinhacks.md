# Handoff

## Branch
`claude/adoring-archimedes-kle6ph` (the plan names `integration/import-dublinhacks`;
this session could only push to its own branch).

## Base commit
`b791501` (merge of PR #23)

## Head commit
The merge commit that adds this file.

## What changed

**Roadmap task `R1`: the owner's hackathon repository `GhostMapDublinHacks`
(`0b7a106`) is merged into this repository**, with the owner's explicit
approval.

Brought in:

- guided-scan UI overhaul and Editor/Simulator demo mode (`ScanGuide`,
  `ScannerGuideHud`, `SimulatedRoom`, `ScannerSimulatorBuild`);
- automatic room scan from ARKit planes (`AutoRoomScanController`,
  `WallPlaneAccumulator`, `RoomFromWalls`, `ScanPhase.AutoScanRoom`);
- wall sweep (ADR-0005, `WallSweepController`, shared `WallFitting`);
- furniture surface detection and per-object `.glb` export (ADR-0006);
- plane-label type suggestion (ADR-0007, superseded by ADR-0011, removed in `R4`);
- peer-to-peer scoping (ADR-0008, proposal only);
- UDP quick-send discovery (ADR-0009, superseded by ADR-0013, dormant until
  `R10` removes it);
- research spikes, moved from `Experiment/` to `docs/research/` with a README.

How conflicts were resolved:

- **Code merged without conflicts.** `apps/`, `shared/` and `fixtures/` are
  byte-identical to `0b7a106` apart from this repository's READMEs and removed
  placeholder `.gitkeep` files (checked with `git diff 0b7a106 -- apps shared
  fixtures`).
- Status pages, architecture overview, handoff index and the test runner were
  resolved in favour of this repository's docs, then updated to describe the
  imported features (state machine, capture paths, validation gates, code map).
- `tools/run_unity_tests.sh` keeps this repository's version without
  `-buildTarget iOS`, because `ScannerIosPostBuild` is now guarded by
  `UNITY_IOS`.
- The hackathon's eight handoffs stay in git history
  (`git ls-tree --name-only 0b7a106 docs/handoffs/`), not in the working tree.
- ADR-0007 and ADR-0009 are marked superseded; ADR-0008 overtaken.

## Contract impact
Additive shared code arrives with the merge: `WallFitting`,
`RoomGeometry.ContainsPointXZ`, `PeerDiscoveryProtocol` (dormant). Scene schema
and protocol v1 messages are unchanged. `protocol-v1.md` keeps the discovery
section, marked superseded.

## How to test
1. On a Mac with Unity 6000.3.24f1: `./tools/run_unity_tests.sh`.
2. Expected: shared 202, viewer 548, scanner 577, all passing (the counts last
   recorded at `0b7a106`).
3. Record the result in `docs/status/integration.md`.

## Test results
- **Not run in this repository**: no Unity in this environment. The merged
  code is identical to the hackathon tree that passed 202 / 548 / 577 and an
  unsigned iOS build.
- One hackathon run had a transient failure in
  `ReconnectAfterFinalizationResendsTheFinalizedSnapshot` (timing); a rerun
  passed.
- `docs/research/E1-room-footprint` Python tests were not run (needs `scipy`).
- All relative Markdown links resolve.

## Known failures
None known. Nothing imported has run on an iPhone.

## Files most important to read next
1. `docs/status/scanner.md` (what the scanner does now)
2. `docs/architecture/overview.md` sections 4, 8 and 12
3. Plan section 18, task `R2`

## Next task
1. Run the three suites and record the counts.
2. **`R2`**: first device session with every capture path.
