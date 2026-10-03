# Handoff

## Branch
`hackathon/auto-room-scan` (from `integration/ui-pr16-device-test` at `ff8222d`).

## What changed
- **Automatic room scan** (`ScanPhase.AutoScanRoom`): after floor lock the user taps
  *Scan Room* and turns once. `WallPlaneAccumulator` merges ARKit vertical planes into
  wall clusters (same normal within 12 deg and centers within 0.30 m of one line);
  `RoomFromWalls` picks the best four that enclose the scan center, orders them by
  bearing, and derives corners with the existing `WallFitting.TryDeriveCorners`.
  Corners enter the existing store via `CornerCaptureController.TryAdoptDerivedCorners`
  and are validated by the unchanged `RoomValidator`.
- It never fabricates a wall: with three trusted walls it reports the bearing of the gap
  ("Look toward the wall behind you.").
- Ceiling height from an ARKit ceiling plane (2.0-4.0 m) if seen, else the existing
  height step. Door/window frame planes become openings through
  `OpeningCaptureController.TryAdoptDetectedOpening`. *Add All* adopts detected furniture
  with each surface's ADR-0007 suggested type (unlabelled surfaces become `generic`).
- *Help GhostMap* falls back to the existing wall sweep (floor lock kept); *Trace Walls
  Instead* and *Rescan Room* also provided. Manual and sweep paths are unchanged.
- Research that led here: `Experiment/E1-room-footprint` (not part of this commit) found
  only 6-8% of floor-visible columns in a furnished lab show an unobstructed wall/floor
  junction, so semantic floor-boundary walls cannot be the primary signal.

## Contract impact
None. No schema, protocol, Viewer or shared change. `scanPhase` gains the string
`AutoScanRoom` (free-form, display only).

## Tests
Scanner EditMode: 571 total, 571 passed (27 new in `AutoRoomScanTests`). iOS build
succeeded; installed on an iPhone 17.

## Known limitations
- **Not verified on a physical iPhone yet.** How many vertical planes ARKit reports for
  a real classroom, and whether it labels them `WallFace`, is unknown until the lab test.
- Four walls only (schema). Glass walls are not detected as planes.
- Door/window planes depend on ARKit labelling them; usually absent.
- Furniture inherits ADR-0006/0007 limits (horizontal planes 0.2-1.4 m high only).
- Closure is not measured on the automatic path (`closureErrorM` stays 0).

## Next task
Classroom test: tune the fixed thresholds in `WallPlaneAccumulator` only if the
`GhostMap auto:` console lines show planes are seen but rejected.
