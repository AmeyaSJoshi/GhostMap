# Handoff

## Branch
`integration/r6-export-bundle`, stacked on `viewer/r8-browser-viewer` (PR #31),
which is stacked on `integration/r1-test-confirmation` (PR #30). Merge in that
order.

## Base commit
`b358e8e` (R8 docs)

## Head commit
The commit that adds this file.

## What changed
Task `R6` (ADR-0013 section 1): **Finalize GhostMap now builds the room's
export bundle on the phone and keeps it.**

- **Shared `Runtime/Export/`** (new namespace `GhostMap.Shared.Export`), moved
  out of the Viewer with `git mv` and 69 tests: `FurnitureParts` (part and
  colour tables, from `FurnitureFactory`), `WallSliceGenerator`, `WallLayout`
  (from `WallRenderer`), `FootprintMesh` (from `FloorCeilingRenderer`),
  `GlbWriter` (from `GlbExporter`), plus `RoomPalette`. The Viewer keeps
  `FurnitureFactory`, `WallRenderer` and `FloorCeilingRenderer` as thin
  wrappers; rendering is unchanged and no scene changed.
- **Whole-room `.glb`:** `GlbWriter.TryExportRoom` writes `floor`, `ceiling`
  and `walls/wall_<i>` nodes. Walls are oriented boxes from `WallLayout`, so
  doors and windows are real holes and rotated rooms work. 21 new shared tests
  read the file back (structure, bounds with Z negated, the door hole, wall
  thickness on a rotated room, every triangle's winding against its normal,
  exact accessor bounds, buffer-view targets, refusals).
- **Scanner `Runtime/Export/`:** `ExportBundleBuilder` (zip: `room.html`,
  `room.glb`, `objects/<id>.glb`, `scene.json`, `README.txt`), `SavedScanStore`
  (atomic, `persistentDataPath/scans/`, newest first, collision suffix, no path
  escapes, disk-full message), `ScanExporter` (template, store and clock
  injected; timed). `ScannerHudController` exports after `TryFinalize`, logs
  `GhostMap export: …`, and **Send to Computer** retries a failed export.
  `ScanGuide` says "saved on this iPhone" or shows the error. 27 new tests,
  including a shared `ScanWorkflowTestRoom` that drives a real workflow to
  Finalized.
- **Template:** `apps/scanner/Assets/GhostMap/Scanner/Resources/GhostMapRoomTemplate.html`
  is a copy of `apps/web-viewer/dist/room-template.html`; a test fails if it
  goes stale.
- **Contract:** `docs/contracts/export-bundle-v1.md` completed: layout and
  naming, `room.glb` and object rules, and a verified "Placing furniture in
  Unity" section.
- **Web viewer fixtures** regenerated with `GlbWriter`.

## Findings worth knowing
- **glTFast negates X on import** (glTFast 6.20.0, Unity 6000.3.24f1). With
  the exporter's Z negation, files arrive in Unity turned 180° about Y relative
  to `scene.json`. The onboarding guide said glTFast negates Z back; it does
  not, and is corrected. Unity users place furniture at `(-x, y, -z)` with yaw
  unchanged; this was verified by importing and placing the real bed. The
  bundle's `README.txt` says so.
- **Bug found and fixed:** accessor `min`/`max` were written with six decimals;
  on rotated walls float noise (`5.96e-9`) made the declared bounds differ from
  the data, which the Khronos validator rejects (8 errors). Now round-trip
  precision, with regression test `DeclaredBounds_EqualTheDataExactly`
  (mutation-checked: fails with the old formatting). Buffer views now declare
  their `target` (18 validator hints gone).
- `ZipArchive` compiles in Unity 6 without the `csc.rsp` the brief anticipated,
  and survives IL2CPP stripping in the iOS build.

## Contract impact
- Shared: new `GhostMap.Shared.Export` namespace (additive). No change to scene
  schema v1 or protocol v1.
- `docs/contracts/export-bundle-v1.md`: sections 1, 3 and 4 added; section 2
  (from `R8`) unchanged.

## How to test
1. `./tools/run_unity_tests.sh`
2. `cd apps/web-viewer && npm ci && npm test && npm run build`
3. iOS: **GhostMap > Configure Scanner XR (iOS)**, **Build Scanner (iOS)**, run
   on the iPhone, finish a scan, tap **Finalize GhostMap**. The guide should say
   "Your room is saved on this iPhone", and Xcode's console should show
   `GhostMap export: saved GhostMap-Room-<time>.zip (<KB>) in <ms> ms`.
4. Copy a bundle off the phone: Xcode > Window > Devices and Simulators, select
   the app, Download Container, then open `AppData/Documents/scans/` (Unity's
   `persistentDataPath` on iOS is the app's `Documents` folder). Or wait for
   `R7`'s share sheet. Unzip it on the Mac and double-click `room.html`.

## Test results
All on the owner's Mac, 2026-10-11:
- Unity EditMode: **shared 292/292, viewer 569/569, scanner 694/694**.
- Node (web viewer): 41/41; template rebuild byte-identical.
- `ScannerBuild.ConfigureXr` + `BuildScanner`: success. Unsigned `xcodebuild`
  Release iphoneos: exit 0, `GhostMapScanner.app` built. IL2CPP output contains
  `System.IO.Compression` and `ExportBundleBuilder`; `resources.assets`
  contains the template.
- Sample bundles from the scanner's own builder (fixture room, and rotated 30°):
  182 KB zip, 27 ms to build and save in the Editor; Khronos validator 0 errors,
  0 warnings, 0 hints on every `.glb`; `room.html` renders the real room with
  door and window holes, furniture aligned, in headless Chrome from `file://`.
- glTFast 6.20.0 import in a scratch Unity project: loads both files, keeps
  node names, scale and +Y up; the placement rule puts the bed exactly on its
  footprint.
- Not done: Blender; the iPhone (export time, files from a real scan).

## Known failures
- None in the automated suites.

## Known limitations
- Saved rooms are not listed in the UI yet and cannot be deleted from the app;
  `R7` adds the list with a Send button per room.
- Export runs on the main thread at Finalize; the iPhone time is unmeasured.
- Furniture does not arrive in Unity already placed (open owner decision 3).

## Files most important to read next
- `docs/contracts/export-bundle-v1.md`
- `shared/com.ghostmap.shared/Runtime/Export/GlbWriter.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Export/ExportBundleBuilder.cs`
- `docs/tasks/R7-share-sheet.md` (next agent task)

## Next task
- Owner: `R2` on the iPhone, now ending with Finalize and the export log line.
- Agent: `R7`, the share sheet over `SavedScanStore` bundles and the saved-rooms
  list.
