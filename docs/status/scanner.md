# Scanner Status

Workstream: Scanner. Owns `apps/scanner/**` and this file.
History: S1-S6 detail in `git show f5a7d30:docs/status/scanner.md`; hackathon
detail in `git show 0b7a106:docs/status/scanner.md` and the hackathon handoffs
(`git ls-tree --name-only 0b7a106 docs/handoffs/`).

## Current state

**S1-S6 are verified on a physical iPhone. Everything imported from
`GhostMapDublinHacks` (task `R1`) compiled and passed its EditMode suite there,
but none of it has run on an iPhone.**

| Feature | What it does | Device result |
| --- | --- | --- |
| S1-S2 | ARKit session, tracking diagnostics, floor lock and the GhostMap coordinate frame | Verified |
| S3 | Walked four-corner capture, closure verification | Verified (closure 0.031 m) |
| S4 | Height by aiming at a derived wall plane, typed fallback | Verified (2.69 m) |
| S5 | Two-point doors and windows; manual furniture placement | Verified |
| S6 | TCP client (now a developer tool), finalize, Reset | Verified against a Python listener |
| UI overhaul | One guided screen: step header with 7-step progress, crosshair with a target bubble, a bottom sheet with one instruction and collapsing button rows, a Details panel for debug readouts, Restart needs a second tap. `ScanGuide` owns all wording. Simulator demo mode with a virtual furnished room | Not run on a device |
| Automatic room scan (ADR-0012) | **Scan Room**: turn in place; `WallPlaneAccumulator` merges ARKit vertical planes into walls, `RoomFromWalls` picks four enclosing the user and derives corners; a ceiling plane gives the height; labelled door/window planes become openings; says which way to look when a wall is missing. **Help GhostMap** switches to the sweep, **Trace Walls Instead** and **Rescan Room** are also offered | Not run on a device |
| Wall sweep (ADR-0005) | Fallback: sweep each wall's floor line; total-least-squares fit; corners where lines meet | Not run on a device |
| Furniture surface detection (ADR-0006) | Horizontal surfaces 0.20-1.40 m high become candidates with measured size and yaw; Add, Skip, Next, Add All | Not run on a device |
| Type from plane label (ADR-0007) | Pre-selects the type from ARKit's `Table`/`Seat` label plus size | Superseded by ADR-0011; removed in `R4` |
| Export bundle (ADR-0013, task `R6`) | **Finish** (was Finalize GhostMap) builds `GhostMap-<room>-<time>.zip` (`room.html`, `room.glb`, `objects/<id>.glb`, `scene.json`, `README.txt`) with `ExportBundleBuilder` and keeps it via `SavedScanStore` in `persistentDataPath/scans/`. The guide says "saved on this iPhone" or shows the error; **Send to Computer** retries a failed export. Logs `GhostMap export: saved <file> (<KB>) in <ms>`. On the Mac: 27 ms, 182 KB for the fixture room | Not run on a device |
| Send to Computer and Your rooms (ADR-0013, task `R7`) | After **Finish**, **Send to Computer** opens the iOS share sheet (`UIActivityViewController`, native plugin `Assets/Plugins/iOS/GhostMapShare/GhostMapShare.mm`) with the saved zip: AirDrop, Save to Files, Mail. The guide reports "Sent with AirDrop.", "Saved to Files." or the error; a cancel says nothing and the button works again. **Rooms** (top bar) lists the five newest saved rooms ("Bedroom · 11 Oct, 14:30"), each with its own Send button. The network chip is now **Live view**, a developer tool that no longer opens by itself. Logic in `ShareController` behind `IShareSheet`; the Editor reveals the zip in Finder instead | Not run on a device. Simulator: share sheet, Save to Files and the callback work |
| Quick-send discovery (ADR-0009) — now the developer **Live view** card | Finds the Viewer over UDP 47832 after finalize | Superseded by ADR-0013; dormant, removed in `R10` |

### Scan flow

```text
Boot -> WaitingForTracking -> FindFloor -> FloorLocked
     -> AutoScanRoom (default) | SweepWalls (Help GhostMap) | CaptureCorners (walk)
     -> VerifyClosure (manual paths) -> CaptureHeight -> AddOpenings
     -> AddObjects (detected surfaces or manual placement)
     -> ReadyToFinalize -> Finalized
```

Every capture path fills the same corner, opening and object stores, so
validation and the snapshot are identical however the room was captured.
`closureErrorM` stays 0 on the automatic path because nothing is re-aimed.

### Where the scanner still differs from the plan

| Plan says | Scanner does today | Fixed by |
| --- | --- | --- |
| Furniture named by YOLO-n on device | Named from ARKit plane labels, or by the user | `R4` |
| No debug text in the guided flow | `S6 diag:` line still in the status text; S1 diagnostics in Details | `R9` |
| `phone.pose` at <= 5 Hz | Not sent | Not planned |
| Manual height marked in snapshot metadata | HUD only | Not planned |

## Last verified commit

- Device-verified: `f087aaa` (S6).
- Latest scanner sources: `0b7a106` from `GhostMapDublinHacks`, merged in task
  `R1`. EditMode suite passes at `69af8eb`. Not device-verified.
- Export bundle: branch `integration/r6-export-bundle`; EditMode 694/694,
  `ScannerBuild.BuildScanner` and unsigned `xcodebuild` on the owner's Mac,
  2026-10-11. Not device-verified.

## Tests run

```bash
./tools/run_unity_tests.sh scanner
```

Last recorded result, in `GhostMapDublinHacks` at the quick-send commit:
**577 tests, 577 passed** (scanner plus embedded shared). One earlier run had a
transient failure in `ReconnectAfterFinalizationResendsTheFinalizedSnapshot`
(timing); an immediate rerun passed. `ScannerBuild.BuildScanner` and an
unsigned `xcodebuild` (Release, iphoneos) succeeded there.

**Confirmed in this repository on 2026-10-11 at `69af8eb`: 577 tests, 577
passed**. **After `R6`: 694 tests, 694 passed** (+90 embedded shared, +27 export), 0 failed (owner's Mac, Unity 6000.3.24f1). The iOS build has not been
repeated here.

## Interfaces consumed

From `com.ghostmap.shared`: the domain DTOs, `GhostCoordinateFrame`,
`RayPlaneMath`, `RoomGeometry` (including `ContainsPointXZ`), `WallGeometry`,
`WallFitting`, `MeasurementMath`, the validators, `ProtocolConstants`, the wire
messages, `ProtocolSerializer` and `PeerDiscoveryProtocol`.

## Known issues

**Unknown until a device session (`R2`)**
- How many vertical planes ARKit reports in a real furnished room. The automatic
  scan never invents a wall; with three it asks the user to look toward the
  fourth.
- Sweep accuracy: aim error grows with roughly the square of aim distance, and a
  consistent aim bias enlarges the whole room while the fit looks clean.
- Whether ARKit labels door and window planes at all (usually it does not).
- Furniture detection: only flat tops 0.20-1.40 m high; chair and couch height
  is the seat; extents are a lower bound; glass and dark surfaces may be missed.

**Known behaviour**
- In the Simulator demo build the automatic room scan reaches "100% seen" but
  does not finish ("Having trouble? Tap Help GhostMap."). Not investigated;
  check whether it also happens in the Editor demo before `R9`.
- Export runs on the main thread when **Finish** is tapped. 27 ms on
  the Mac; the iPhone time is unmeasured (spec target under 2 s).
- Saved rooms accumulate in `persistentDataPath/scans/`; **Rooms** lists the
  newest five and counts the rest. There is no delete yet.
- The share sheet's outcome comes back through `UnitySendMessage` to a
  runtime-created `GhostMapShareSheet` GameObject; if iOS never calls back, the
  Send button stays disabled until Restart.
- Walls must be swept in order around the room; out of order is refused as
  "self-intersecting" without saying why.
- A skipped detected surface stays skipped after a re-sweep.
- Once height is captured the only way back is Restart.
- `TcpClient.Connect` blocks the network thread for the OS timeout on an
  unreachable host (developer path only).
- A non-numeric Port field silently becomes port 0 (developer path only).
- No handling of app backgrounding.

**Build**
- iOS build: **GhostMap > Configure Scanner XR (iOS)**, then **GhostMap > Build
  Scanner (iOS)**; select a signing team in Xcode; delete the old app before a
  device test. Simulator demo build: `ScannerSimulatorBuild.ConfigureForSimulator`
  then `ScannerSimulatorBuild.BuildForSimulator` (run with `-executeMethod`).
- `ScannerIosPostBuild` is guarded by `UNITY_IOS`, so the EditMode suite runs
  without iOS Build Support. Its Xcode 26 Swift-library workaround needs
  re-checking on any Xcode or ARKit upgrade.

## Next safe task

`R2`: the first device session (plan section 18), now ending with **Finish**,
the `GhostMap export:` log line, and **Send to Computer** by AirDrop to the Mac
(`R7`'s device check in `docs/tasks/R7-share-sheet.md`). Agent work in
parallel: `R4` (YOLO-n), then `R9` (hardening). Progress:
`docs/status/integration.md`.

## Do not touch

`shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
`docs/decisions/**` (Shared/Integration) and `apps/viewer/**`,
`apps/web-viewer/**` (Viewer).
