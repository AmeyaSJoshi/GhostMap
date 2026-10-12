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
| Type from plane label (ADR-0007) | Pre-selected the type from ARKit's `Table`/`Seat` label plus size | **Removed** in `R4` (superseded by ADR-0011) |
| Furniture identification (ADR-0011, task `R4`) | YOLO11n (Core ML, plugin `Assets/Plugins/iOS/GhostMapDetector/GhostMapDetector.mm`, model in `StreamingAssets/GhostMap/`) reads the camera image five times a second **from floor lock until furniture is done**, so what the camera passes while the user turns to scan the room is already found at the furniture step. `FurnitureIdentificationController` takes each box's bottom-centre to the floor, ignores points over 8 m away, outside the room once it exists, or on furniture already added, matches the ARKit surface it sits under, maps the COCO class (`FurnitureClassMap`), and proposes an item after 5 frames at >= 0.5 confidence (same type within 0.5 m is one item). A sighting not yet proposed is forgotten after 3 s unseen; a proposal is kept until added, or dropped when the closed room shows it is outside; a proposal seen before surfaces were measured takes its surface (and desk-or-table type) once they are. The guide says "Found: bed, desk, 2 chairs. Tap Add All."; **Add All** adds surfaces with their identified type and the rest at their floor position with default sizes, one revision each. A surface the detector has not named is offered as `generic`. Logs `GhostMap yolo: <ms> ms` every 25 frames and `GhostMap identify: <list>` when the list changes | Not run on a device. Model checked with Vision on the Mac; app compiled with Core ML and Vision linked and the model inside |
| Export bundle (ADR-0013, task `R6`) | **Finish** (was Finalize GhostMap) builds `GhostMap-<room>-<time>.zip` (`room.html`, `room.glb`, `objects/<id>.glb`, `scene.json`, `README.txt`) with `ExportBundleBuilder` and keeps it via `SavedScanStore` in `persistentDataPath/scans/`. The guide says "saved on this iPhone" or shows the error; **Send to Computer** retries a failed export. Logs `GhostMap export: saved <file> (<KB>) in <ms>`. On the Mac: 27 ms, 182 KB for the fixture room | Not run on a device |
| Send to Computer and Your rooms (ADR-0013, task `R7`) | After **Finish**, **Send to Computer** opens the iOS share sheet (`UIActivityViewController`, native plugin `Assets/Plugins/iOS/GhostMapShare/GhostMapShare.mm`) with the saved zip: AirDrop, Save to Files, Mail. The guide reports "Sent with AirDrop.", "Saved to Files." or the error; a cancel says nothing and the button works again. **Rooms** (top bar) lists the five newest saved rooms ("Bedroom · 11 Oct, 14:30"), each with its own Send button. The network chip is now **Live view**, a developer tool that no longer opens by itself. Logic in `ShareController` behind `IShareSheet`; the Editor reveals the zip in Finder instead | Not run on a device. Simulator: share sheet, Save to Files and the callback work |
| A photo of each object (task `R11`) | The detector plugin keeps its last analysed camera frame and crops JPEGs from it (at most 480 px). Identified furniture keeps the crop of its most confident sighting; anything else gets a crop of the camera view around it when added, if mostly on screen. `ScanWorkflowController.ObjectPhotos` holds them by object id (dropped on undo, never published); the export writes `photos/<id>.jpg` and `objectPhotos` in `room.html`. No photos in demo mode (the project has no Image Conversion module) | Not run on a device. Compiled for iPhone |
| Hardening and guided flow (task `R9`) | Six steps (Floor, Room, Height, Openings, Furniture, Send); every capture refused while tracking is lost, including adding detected furniture; after backgrounding, tracking must settle for 1 s ("Welcome back..."); a full turn with 0-2 walls offers Help GhostMap after 6 s and says how many walls were found; with a detector, the furniture step first asks you to look around, then after 10 s points to placing by hand; no debug text unless developer mode (Editor, Development builds, or five quick taps on Details); 108 px reticle with a dark halo; a light haptic tap per capture. Fixes: detected furniture was a quarter turn off (since `R1`); detected doors and windows were lost when the ceiling was not seen; demo mode had no AR planes; the Simulator build did not link Core ML/Vision | Not run on a device. **Demo run end to end in the iOS Simulator** (2026-10-11) |
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
| Remove any single proposed item before Add All | Undo removes the last added object | Not planned yet |
| `phone.pose` at <= 5 Hz | Not sent | Not planned |
| Manual height marked in snapshot metadata | HUD only | Not planned |

## Last verified commit

- Device-verified: `f087aaa` (S6).
- Latest scanner sources: `0b7a106` from `GhostMapDublinHacks`, merged in task
  `R1`. EditMode suite passes at `69af8eb`. Not device-verified.
- Export bundle: branch `integration/r6-export-bundle`; EditMode 694/694,
  `ScannerBuild.BuildScanner` and unsigned `xcodebuild` on the owner's Mac,
  2026-10-11. Not device-verified.
- Hardening: branch `scanner/r9-hardening`; EditMode 849/849; Simulator demo
  build run from Lock Floor to Finish; exported room checked in Chrome.
- Detector label: branch `shared/r5-detector-label`; EditMode 821/821.
  Objects the detector named carry its COCO class as `label`. Not
  device-verified.
- Object photos: branch `integration/r11-object-photos`; EditMode 805/805,
  iOS build and unsigned `xcodebuild` on the owner's Mac, 2026-10-11. Not
  device-verified.
- Furniture identification: branch `scanner/r4-yolo-identification`; EditMode
  779/779 (773 before identification moved to floor lock), `ScannerBuild.BuildScanner` and unsigned `xcodebuild` on the
  owner's Mac, 2026-10-11. Not device-verified.

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
passed**. **After `R6`: 694 tests, 694 passed** (+90 embedded shared, +27 export), 0 failed (owner's Mac, Unity 6000.3.24f1).
**After `R7`: 710. After `R4`: 779 tests, 779 passed** **After `R11`: 805** (+26 photo tests). **After `R5`: 821** (+11 shared label tests, +5 label workflow tests). **After `R9`: 849** (+28: hardening, few walls, demo planes, three regressions). For `R4`: 773 at its first commit, +6 for identification from floor lock; +73
`FurnitureIdentificationTests` cases, +2 guide tests, +2 detection tests with
the six ADR-0007 suggestion tests rewritten for identified types, -14
`FurnitureTypeSuggesterTests` removed); the iOS
build and unsigned `xcodebuild` succeed.

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
  is the seat when added from a surface by hand; extents are a lower bound;
  glass and dark surfaces may be missed.
- Furniture identification (`R4`): how often YOLO11n names real bedroom
  furniture, its frame time on the owner's iPhone, and whether the box-to-screen
  mapping (`DetectionGeometry`, ARKit's portrait `displayTransform`) lands the
  floor point on the object. Unit tests prove the maths against ARKit's
  documented transform, not against a camera.

**Known behaviour**
- Fixed in `R9`: the demo's automatic scan never finished because demo mode
  reported no AR planes. The demo now runs from Lock Floor to Finish in the
  iOS Simulator (see `R9` below).
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
- Identification only runs in portrait (the app's only orientation). A TV on a
  stand projects its floor point behind the stand, so it may be placed late or
  outside the room; a TV is never matched to a surface.
- Add All adds every proposal; removing one afterwards is Undo (last added
  only). Items without a surface are added facing +Z at default size.
- In demo mode the virtual furniture is "seen" through the walls (the
  simulated detector does not test occlusion).
- Object photos (`R11`): none in demo mode; an object off screen when added
  gets none; a photo can include neighbouring furniture. The plugin holds one
  ARKit camera buffer between frames, which could in theory make ARKit drop a
  frame.

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
(`R7`'s device check in `docs/tasks/R7-share-sheet.md`). Include `R4`'s device
check (`docs/tasks/R4-yolo-furniture-identification.md`, "Device check"): the
`GhostMap yolo:` frame time and what "Found:" names in the room. The demo build
(`ScannerSimulatorBuild`) now walks the whole flow in the Simulator, so use it
to rehearse. Agent work: whatever `R2` turns up. Progress:
`docs/status/integration.md`.

## Do not touch

`shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
`docs/decisions/**` (Shared/Integration) and `apps/viewer/**`,
`apps/web-viewer/**` (Viewer).
