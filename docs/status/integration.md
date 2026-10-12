# Integration Status

Workstream: Shared / Integration. Tracks the stand-in-place roadmap, tasks
`R1`-`R10` in plan section 18 (direction: ADR-0012 and ADR-0013).

## Current state

**`R1` done: merged, and all three Unity suites pass in this repository (shared 202, viewer 548, scanner 577, run on the owner's Mac on 2026-10-11). `R4` (on-device YOLO furniture identification, running from floor lock), `R11` (a photo of each object in the export), `R6` (phone-built export bundle), `R7` (share-sheet Send to Computer) and `R8` (browser viewer) are built and verified as far as a Mac can; none has run on the iPhone. The whole product path now exists in code. Next: `R2` on the iPhone, ending with Finish and an AirDrop to the Mac.**

| Task | What | State |
| --- | --- | --- |
| R1 | Bring `GhostMapDublinHacks` into this repository | **Merged** (owner-approved, 2026-10-11). Suites confirmed here 2026-10-11 at `69af8eb`: 202 / 548 / 577, all passing |
| R2 | First device session: every capture path on a real iPhone | Not started |
| R3 | Accuracy benchmark against a tape measure | Not started |
| R4 | YOLO-n furniture identification on the phone (ADR-0011) | **Built** on `scanner/r4-yolo-identification`: YOLO11n Core ML model (SHA-256s in ADR-0011), native Vision plugin, `FurnitureIdentificationController`, "Found: ..." and Add All; ARKit-label guessing removed. EditMode-tested, compiled for iPhone with Core ML linked and the model inside. Device check is the owner's (`R2`) |
| R5 | Optional `label` on `SceneObjectModel` (additive schema change) | **Built** on `shared/r5-detector-label`: field, 64-character rule, inspector, fixture label, scanner fills it with the COCO class name, `room.html` shows it (title only for `generic`). All suites green |
| R6 | The phone builds the export bundle (`room.html`, `room.glb`, objects, `scene.json`) and keeps saved scans | **Built** on `integration/r6-export-bundle`: shared `Runtime/Export/`, whole-room `.glb`, zip on Finalize, saved scans, contract complete. All `.glb` pass the Khronos validator; imports into Unity with glTFast verified. Not run on the iPhone; saved-scans list UI moves to `R7` |
| R7 | Send to Computer through the iOS share sheet | **Built** on `scanner/r7-share-sheet`: native share-sheet plugin, `ShareController`, **Send to Computer** after Finish, **Rooms** list. EditMode-tested and compiled for iPhone; the AirDrop device check is the owner's (`R2`) |
| R8 | Browser viewer `room.html` (three.js, view and measure) | **Built** on `viewer/r8-browser-viewer`: template, 41 Node tests, CI job. Checked in headless Chrome; owner's double-click check in Safari still to do. The phone fills it in `R6` |
| R9 | Failure hardening and one guided scan flow | Not started |
| R10 | Retire the Unity Viewer and the discovery code | Not started |
| R11 | A photo of each piece of furniture, exported and shown in `room.html` (owner's "option 1" instead of 3D-scanning furniture) | **Built** on `integration/r11-object-photos`: native crop from the detector's frame, photos kept per object, `photos/<id>.jpg` and `objectPhotos` (contract section 5), shown in the viewer. Compiled for iPhone; checked in Chrome; device check in `R2` |

Nothing has gone from the phone to a computer yet. The S6 network test used a
Python listener.

### What `R1` brought in

`GhostMapDublinHacks` at `0b7a106`: the guided-scan UI overhaul and Simulator
demo mode, the automatic room scan, wall sweep (ADR-0005), furniture surface
detection and per-object `.glb` export (ADR-0006), plane-label type guessing
(ADR-0007, removed by `R4`), peer-to-peer scoping (ADR-0008), UDP quick-send
discovery (ADR-0009, superseded by ADR-0013, removed in `R10`), and two research
spikes now under `docs/research/`.

How it was merged:

- Code (`apps/`, `shared/`, `fixtures/`) merged without conflicts and is
  byte-identical to the hackathon tree apart from READMEs and removed
  placeholder files.
- Documentation conflicts were resolved in favour of this repository's docs;
  the status pages now describe the imported features.
- The hackathon's eight handoffs and older status text stay in git history
  (`0b7a106`), not in the working tree.
- `tools/run_unity_tests.sh` runs the scanner suite without `-buildTarget iOS`,
  since `ScannerIosPostBuild` is now guarded by `UNITY_IOS`.

## Last verified commit

`69af8eb` (main after PR #29): shared 202, viewer 548, scanner 577, all passing (EditMode, owner's Mac). Nothing since R1 has run on an iPhone.

## Tests run

| Date | Where | Suites | Result |
| --- | --- | --- | --- |
| 2026-10-11 | Agent cloud container | shared, viewer, scanner | **Not run.** The container has no Unity and its network policy blocks Unity's download and licensing hosts (`download.unity3d.com`, `public-cdn.cloud.unity3d.com`, `unity.com`); Unity also needs the owner's licence. Added GitHub Actions CI instead |
| 2026-10-11 | Agent cloud container | Python tools job (`inspect_snapshot.py` on the three fixtures, `send_fixture.py --dry-run`, `bash -n tools/run_unity_tests.sh`) | Passed |
| 2026-10-11 | GitHub Actions, run 1 (`7859878`) | tools; Unity shared, viewer, scanner | tools **passed**. All three Unity jobs **stopped at "Unity licence secrets are missing"**, as designed: the secrets are not set yet. Unity never started |
| 2026-10-11 | GitHub Actions, run 5 on `main` (`04550b9`, merge of PR #25) | tools; Unity shared, viewer, scanner | Same as run 1: tools **passed**, Unity jobs stopped at the missing-secrets check. Artifact upload now skips cleanly |
| 2026-10-11 | Owner's Mac, Unity 6000.3.24f1, `./tools/run_unity_tests.sh` at `69af8eb` | shared, viewer, scanner (EditMode) | **All passed: shared 202/202, viewer 548/548, scanner 577/577**, 0 failed, 0 skipped. First Unity run in this repository; confirms `R1` |
| 2026-10-11 | Owner's Mac, Node 24, `viewer/r8-browser-viewer` | web-viewer (`npm ci`, `npm test`, `npm run build`) | **41 tests, 41 passed**; rebuilt template byte-identical to the committed one |
| 2026-10-11 | Owner's Mac, headless Chrome 154, `file://` | `demo/room.html` (fixture room, real exported furniture, stand-in room shell) | Loads with one request (the page) and no console errors; select, list, Frame room, Dollhouse, Measure and the template message work at 1280×800 and 390×844 |
| 2026-10-11 | Owner's Mac, Unity 6000.3.24f1, `integration/r6-export-bundle` | shared, viewer, scanner (EditMode) | **All passed: shared 292/292, viewer 569/569, scanner 694/694** |
| 2026-10-11 | Owner's Mac, `integration/r6-export-bundle` | `ScannerBuild.ConfigureXr`, `ScannerBuild.BuildScanner`, unsigned `xcodebuild` Release iphoneos | Both succeeded (`xcodebuild` exit 0, `GhostMapScanner.app` built). The IL2CPP output contains `System.IO.Compression` and `ExportBundleBuilder`, and `resources.assets` contains the template, so neither was stripped |
| 2026-10-11 | Owner's Mac, Khronos `gltf-validator` 2.0.0-dev.3.10 | `room.glb` and three `objects/*.glb` from a scanner-built bundle (fixture room, and the same room rotated 30°) | First run: 8 errors (accessor min/max rounded to 6 decimals) and 18 hints (no bufferView target). After the fix: **0 errors, 0 warnings, 0 hints** on all |
| 2026-10-11 | Owner's Mac, scratch Unity 6000.3.24f1 project with glTFast 6.20.0 | Import `room.glb` and `objects/bed-1.glb` from the bundle | Loads; node names kept; **glTFast negates X**, so the room imports turned 180° about Y (floor x -4..0, z -3..0). The bed placed at `(-x, y, -z)`, rotation `(0, yaw, 0)` fills its exact footprint with its headboard at the right wall |
| 2026-10-11 | Owner's Mac, headless Chrome 154, `file://` | `room.html` from the scanner-built bundles | Renders the real `room.glb` with door and window holes; furniture aligned with the walls, also in the rotated room; Dollhouse and Measure work |
| 2026-10-11 | Owner's Mac, Unity 6000.3.24f1, `scanner/r7-share-sheet` | scanner (EditMode) | **710/710** (+16 share and saved-rooms tests); `ScannerSceneTests` passes on the regenerated `Scanner.unity`. Shared and viewer unchanged by `R7` |
| 2026-10-11 | Owner's Mac, `scanner/r7-share-sheet` | `ConfigureXr`, `BuildScanner`, unsigned `xcodebuild` Release iphoneos | `BUILD SUCCEEDED`; `GhostMapShare.mm` compiled into `UnityFramework` (`_GhostMapShare_ShareFile` defined) and called from the IL2CPP `DllImport` stub |
| 2026-10-11 | Owner's Mac, iOS Simulator (iPhone 17, iOS 26.5), Simulator demo build | **Rooms** list, **Send to Computer**, the real share sheet | **Rooms** listed a saved bundle as "Fixture Bedroom With Window · 11 Oct, 12:00"; **Send to Computer** opened `UIActivityViewController` with the 182 KB zip; **Save to Files** → Save reported back through `UnitySendMessage` and the panel showed "Saved to Files."; the file in Files was byte-identical. AirDrop does not exist in the Simulator. Demo mode's automatic scan did not finish ("Having trouble? Tap Help GhostMap."), so the bundle was placed in `Documents/scans` by hand rather than made by Finish |
| 2026-10-11 | Owner's Mac, Unity 6000.3.24f1, `scanner/r4-yolo-identification` | scanner (EditMode) | **779/779** (773, then +6 when identification moved to floor lock; +73 identification cases, +4 guide and detection, -14 ADR-0007 suggester tests). Shared and viewer unchanged by `R4` |
| 2026-10-11 | Owner's Mac, `scanner/r4-yolo-identification` | `ConfigureXr`, `BuildScanner`, unsigned `xcodebuild` Release iphoneos | `BUILD SUCCEEDED`. `UnityFramework` links `CoreML.framework` and `Vision.framework` and defines the `GhostMapDetector_*` functions the IL2CPP stubs call; the app carries the model at `Data/Raw/GhostMap/yolo11n.mlpackage` (5.2 MB, no `.meta` files) |
| 2026-10-11 | Owner's Mac, Swift + Vision, the committed `yolo11n.mlpackage` | Load, classify a test photo upright and as a landscape sensor image with orientation `.right` | 80 COCO labels including `bed`, `couch`, `dining table`, `tv`; identical boxes both ways (so Vision reports boxes in the upright image, which `DetectionGeometry` assumes); 2.3 ms per frame warm on the Mac |
| 2026-10-11 | Owner's Mac, Unity 6000.3.24f1, `integration/r11-object-photos` | scanner (EditMode) | **805/805** (+26 photo tests). Shared and viewer unchanged |
| 2026-10-11 | Owner's Mac, `integration/r11-object-photos` | `ConfigureXr`, `BuildScanner`, unsigned `xcodebuild` Release iphoneos | `BUILD SUCCEEDED`; `GhostMapDetector_CropJpeg` is in `UnityFramework` and called from its IL2CPP stub; Core Image linked |
| 2026-10-11 | Owner's Mac, Node 24, `integration/r11-object-photos` | web-viewer | **46/46** (+5); rebuilt template copied to the scanner |
| 2026-10-11 | Owner's Mac, headless Chrome 154, `file://` | `room.html` filled with the fixture room and a real JPEG for the bed | Selecting the bed shows the 810×1080 photo above its sizes ("Photo of the bed, taken by the phone"); selecting the desk (no photo) hides it; no console errors |
| 2026-10-11 | Owner's Mac, Unity 6000.3.24f1, `shared/r5-detector-label` | shared, viewer, scanner (EditMode) | **All passed: shared 303/303, viewer 580/580, scanner 821/821** (+11 shared label tests in each; scanner +5 label workflow tests) |
| 2026-10-11 | Owner's Mac, Node 24, `shared/r5-detector-label` | web-viewer | **48/48** (+2: a typed object keeps its type as the title; the fixture carries a label) |
| 2026-10-11 | Owner's Mac, `shared/r5-detector-label` | `inspect_snapshot.py` on the three fixtures, and on a copy with a 65-character label and a newline in another | Fixtures as before (two valid, malformed rejected on height alone; the desk now prints `"dining table"`); the copy rejected on exactly those two labels |
| 2026-10-11 | Owner's Mac, `69af8eb` | Python tools checks (`inspect_snapshot.py --quiet` on the three fixtures, `send_fixture.py --dry-run`, `bash -n tools/run_unity_tests.sh`) | Passed: the two valid fixtures valid, the malformed one rejected on the height rule alone |

The counts match those recorded in `GhostMapDublinHacks` at `0b7a106`. CI's Unity jobs still stop at the missing-secrets check, so for now the suites are confirmed only on the owner's Mac.

## Open decisions for the owner

1. **Unity or native Swift for the phone app: decided "Unity for now",
   revisit after `R2`** (owner, 2026-10-11). Reasoning in `docs/onboarding.md`,
   "Where Unity fits, and why"; rule in `AGENTS.md` rule 15. When `R2` is recorded, put the question back to the
   owner with the result.
2. **Unity licence secrets** for CI (`docs/ci.md`). Until they exist, Unity
   results come only from local runs on the owner's Mac (`./tools/run_unity_tests.sh`).
3. **Should furniture arrive in Unity already placed?** `R6` kept the brief's
   design: each `objects/<id>.glb` sits at its own origin and its place is in
   `scene.json`. The bundle's `README.txt` now gives the verified rule for
   Unity (glTFast turns files 180° about Y, so position `(-x, y, -z)`,
   rotation `(0, yaw, 0)`), which works but is manual per object. If the owner
   wants furniture already placed, adding a `room-furnished.glb` is an additive
   change to `docs/contracts/export-bundle-v1.md` section 4.
4. **20 stale remote branches.** Agent sessions cannot delete branches. From a
   clone:

   ```bash
   git push origin --delete docs/one-button-computer-transfer \
     feature/drag-drop-furniture feature/nl-asset-editing feature/peer-to-peer-transport \
     integration/furniture-detect-and-export integration/sweep-and-furniture \
     scanner/s1-unity-project-ar-smoke-test scanner/s2-floor-lock-coordinate-frame \
     scanner/s3-corner-capture-closure scanner/s4-room-height-capture \
     scanner/s5-openings-furniture scanner/s6-network-finalization scanner/sweep-wall-capture \
     shared/status-docs-refresh \
     viewer/v1-desktop-network-foundation viewer/v2-room-geometry viewer/v3-wall-openings \
     viewer/v4-objects-camera viewer/v5-edit-measure viewer/v6-persistence-hud
   ```

   Their work is either on `main` already or was dropped on purpose during the
   2026-10-10/11 cleanup; nothing on them is needed.

## Known risks

From reading the code, not from a failed run:

- **Automatic walls depend on ARKit.** How many vertical planes ARKit reports in
  a real furnished room on the owner's iPhone is unknown. The sweep and walked
  fallbacks exist for this.
- **Standing in one spot means aiming far.** Floor-line error grows with
  distance; stand near the room centre (ADR-0005's error table).
- **Phone-side export cost.** Writing several `.glb` files and a ~1 MB
  `room.html` at the end of a scan has not been measured on an iPhone (`R6`).
- **AirDrop needs both devices' Bluetooth and Wi-Fi on** and receiving enabled;
  Save to Files is the fallback (`R7`).
- **Local `room.html` limits.** Browsers block a local page from reading files
  beside it, so all geometry must be embedded (`R8`).
- **Detector licence.** YOLO-n is AGPL-3.0 (ADR-0011).
- **Detector in real rooms.** COCO has no desk or dresser; a desk is a "dining
  table" on a desk-height surface. Recall in the owner's room and the frame
  time on the iPhone are unknown until `R4`'s device check.

## Next safe task

1. **Owner: `R2`**, the first device session on the iPhone
   (`docs/tasks/R2-first-device-session.md`). The Unity suites are green at
   `69af8eb`, so the build is ready to try.
2. **Owner, quick:** double-click a filled `room.html` in Safari to finish `R8`
   (`apps/web-viewer/README.md`, "Manual check"); record it in
   `docs/status/viewer.md`. In `R2`, also note the `GhostMap export:` time,
   then **Send to Computer** by AirDrop three times, Save to Files once, and
   open `room.html` from the unzipped bundle (`R7` device check).
3. **Owner, in `R2`:** `R4`'s device check: point at the bed, desk, table,
   chairs; note what "Found:" says, the `GhostMap yolo:` frame time, and the
   added sizes against a tape. Then `R11`'s: open the exported `room.html` and
   check each object's photo shows that object.
4. **Agent, in parallel:** `R9` (hardening) parts that need no device.
   See `docs/tasks/README.md`.
5. **Owner, when convenient:** add `UNITY_LICENSE`, `UNITY_EMAIL`,
   `UNITY_PASSWORD` as repository secrets (`docs/ci.md`) so CI can run the
   Unity suites on every push. Until then, run `./tools/run_unity_tests.sh` on
   the Mac before merging Unity changes.

## Do not touch

`apps/scanner/**` and `apps/viewer/**`, except on a short-lived, explicitly
scoped `integration/<task>` branch.

---

## R2 device log

| Date | Step | Result | Notes |
| --- | --- | --- | --- |
| — | — | — | — |

## R3 accuracy benchmark

Targets: median wall absolute error ≤ 0.12 m, max wall error ≤ 0.20 m, height
error ≤ 0.15 m.

| Scan | Wall A err | Wall B err | Wall C err | Wall D err | Height err | Door width err | Walls found automatically | Fallback used |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| — | — | — | — | — | — | — | — | — |
