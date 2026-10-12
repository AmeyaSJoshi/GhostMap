# R2 blocker: the stand-in-place room scan does not find the room on the owner's iPhone

**Status (2026-10-11): open. This is the project's current blocker.**
**Owner's goal:** stand in one spot in a real, furnished room, turn once, and
get the room's four walls, corners and size, on any iPhone, without LiDAR.
**Decision:** ADR-0012 (stand-in-place capture). **Workstream:** Scanner.
**Branch with the work so far:** `fix/scanner-r2-wall-planes` (stacked on
`scanner/r9-hardening`, PR #37).
**Raw evidence:** `docs/research/r2-device-logs/2026-10-11-auto-scan.md`.

Read this file, then the log file, before changing anything.

## What the owner sees

Lock Floor works. **Scan Room**, a slow full turn (10-16 deg/s, 100% coverage):
the guide stays on "Keep turning", then "Having trouble? Tap Help GhostMap.".
**Finish Scan** is refused (`NoRoomYet`). In the first attempt the owner gave
up. The fallback (**Help GhostMap** = trace each wall's floor line) was not
tried yet.

## What the logs show

| Run | Build | ARKit reported | Kept as wall evidence | Trusted walls | Result |
| --- | --- | --- | --- | --- | --- |
| 1 | `scanner/r9-hardening` | unknown (no counter yet) | **0** for the whole turn | 0 | No room |
| 2 | `fix/scanner-r2-wall-planes` `1a5c025` | 7 vertical planes, 4 labelled "wall" by ARKit | 7 | **2** (2.6 m and 1.8 m wide, labelled, full height) | No room |

Run 2's untrusted candidates:

- a labelled 0.8 m x 2.0 m plane (bar: 1.0 m);
- labelled 0.6 m x 0.7 m and 0.3 m x 1.8 m patches;
- unlabelled 0.5-0.7 m wide patches under 1 m tall (bar: 2.5 m).

Run 2 did not log where each wall is, so it is unknown whether the small planes
are the third and fourth walls, the same walls again, or furniture. Every
summary line now ends with `bearing <deg> deg dist <m> m`; the next device run
answers that.

## How the scan decides (code map)

```text
ArSpatialProvider.TryGetDetectedSurfaces          ARPlaneManager.trackables -> DetectedSurface
  -> AutoRoomScanController.Tick (every frame while scanning)
       CountRawVerticals                            the "raw vertical" log line
       WallPlaneAccumulator.Update -> TryObserve    per-plane filter (below)
                                   -> Recluster     merge planes on the same line
       RoomFromWalls.Select (every 0.5 s)           trust, pick 4, build corners
       UpdateGuidance                               the guide's messages
  -> ScanWorkflowController.TryFinishAutoScan       adopt corners, ceiling height, openings
```

| Gate | Where | Value now | Notes |
| --- | --- | --- | --- |
| Per-plane: unlabelled | `WallPlaneAccumulator.MinUnlabelledWidthM` / `HeightM` / `TopM` | 0.5 m wide, 0.4 m tall, top at least 1.3 m above the floor | Was 1.5 m x 1.2 m until `1a5c025`; that rejected everything in run 1 |
| Per-plane: labelled | `MinLabelledWidthM` | 0.3 m | Was 0.6 m |
| Same wall | `SameWallAngleDeg` / `SameWallOffsetM` | 12 deg, 0.30 m | Planes merge into one cluster |
| **Trusted wall** | `TrustedLabelledWidthM` / `TrustedUnlabelledWidthM` | **0.5 m labelled** (was 1.0 m until run 3's build), 2.5 m unlabelled, observed width along the line | **This is what failed in run 2** |
| Room | `RoomFromWalls.Select` | at least 3 trusted to name the missing direction, **4 trusted** that enclose the scan centre (no angular gap over 170 deg, centre at least 0.3 m from each wall, each wall at least 0.5 m observed (`MinObservedPerWallM`, was 0.8 m), observed length at least 25% of the perimeter) | ADR-0012: never invents a wall |
| Help offer | `AutoRoomScanController.OfferHelpAfterS` / `FewWallsHelpAfterS` | 30 s; 6 s after a full turn with 0-2 trusted | |

## Hypotheses, most likely first

1. **The trust bar is too high for what ARKit gives a phone without LiDAR.**
   ARKit labels wall planes well ("wall-labelled 4 of 7") but keeps them
   small unless the camera dwells on them. A labelled 0.8 m plane is probably a
   real wall. Lowering `TrustedLabelledWidthM` (to about 0.5 m) would likely
   make 3-4 trusted walls; check with the bearing log that they are 4 distinct
   directions before changing it.
2. **The user turns without pausing,** so ARKit never grows planes on some
   walls. The guide could ask for a one-second pause per wall, or the scan
   could keep going until each sector has a trusted wall rather than stopping
   at 100% heading coverage.
3. **Furniture hides the lower walls,** so only small high patches exist, and
   the scan should prefer labelled patches high on the wall (it already
   ignores low unlabelled ones).
4. **Small planes are furniture** (a wardrobe or shelf front). The bearing and
   distance log will show a plane much nearer than the wall behind it.
5. Lesser: ARKit's "resource constraints" warning (it appears even with
   YOLO loaded at launch), and YOLO competing for the Neural Engine. To rule
   it out, try one run with the detector off.

Not in scope without the owner and an ADR: LiDAR or scene reconstruction
(`AGENTS.md` rule 6), inventing a missing wall from three, or moving the app to
native Swift (rule 15; the owner chose to finish R2 on Unity first).

## Change made without more owner data (2026-10-11)

The owner declined to measure the room or describe each wall. Acting on
hypothesis 1 from run 2's numbers alone, `TrustedLabelledWidthM` went from 1.0
to 0.5 m and `MinObservedPerWallM` from 0.8 to 0.5 m. A new test,
`TheOwnersRoom_LabelledWallsAsSmallAsTheDeviceSaw_BuildTheRoom`, builds a room
from labelled walls of 2.6, 1.8, 0.8 and 0.6 m, the sizes the phone reported.
Unlabelled walls still need 2.5 m. **Risk:** a small plane that ARKit labels
"wall" but is really furniture could now become a wall; `RoomFromWalls` still
requires four walls enclosing the user, and the shared room validator still
applies. The next device run (it logs bearings) shows whether this worked.

## Next steps

1. **Owner:** run the scan once more with the current build (it logs bearing and
   distance), using wireless debugging (Xcode > Window > Devices and
   Simulators > Connect via network). Paste every `GhostMap auto:` line. Also:
   the room's size by tape, and what stands along each wall (bed, desk,
   wardrobe, window, door, bare wall). Then tap **Help GhostMap** and finish
   R2 with the sweep, so the rest of the product gets its device test.
2. **Agent:** from that log, map clusters to physical walls by bearing. Then:
   - if the four walls are there but small, lower the labelled trust bar
     (hypothesis 1);
   - if a wall is missing entirely, change the guidance to dwell on each wall
     (hypothesis 2).

   Encode the device log as an EditMode test (planes built with the logged
   sizes, bearings and distances) that fails today and passes after the
   change; `AutoRoomScanTests` has the helpers (`Patch`, `Wall`, `TurnOnce`).
3. Rebuild (`ScannerBuild.ConfigureXr`, `ScannerBuild.BuildScanner`). Then
   put the signing team back into
   `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj/project.pbxproj`:
   `sed -i '' 's/DEVELOPMENT_TEAM = "";/DEVELOPMENT_TEAM = 553SS7SNPX;/'`.
   Ask the owner to press Run and choose "Use Version on Disk".
4. Record every attempt in `docs/status/integration.md` ("R2 device log"),
   including failures.

## Done when

On the owner's iPhone, in the owner's room, **Scan Room** builds the room
from one turn at least 4 times out of 5. The wall lengths are then checked in
`R3` against a tape.
