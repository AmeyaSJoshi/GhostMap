# Handoff

## Branch
`scanner/r4-yolo-identification`, stacked on `scanner/r7-share-sheet` (PR #33),
then #32, #31 and #30. Merge in order: #30, #31, #32, #33, this.

## Base commit
`e3d8c99` (R7 docs)

## Head commit
The commit that adds this file.

## What changed
Task `R4` (ADR-0011): **the phone names the furniture from the camera image.**
In `AddObjects` the guide says "Found: bed, desk, 2 chairs. Tap Add All." and
one tap adds them. The model only names things; every size comes from an
ARKit surface or the type's default.

- **Model** `apps/scanner/Assets/StreamingAssets/GhostMap/yolo11n.mlpackage`
  (YOLO11n, COCO, NMS pipeline, 5.2 MB). SHA-256s and the export environment
  are in ADR-0011, "As built". It sits in `StreamingAssets` so Unity copies it
  into the app untouched; the plugin compiles it on first use.
- **Native plugin** `apps/scanner/Assets/Plugins/iOS/GhostMapDetector/GhostMapDetector.mm`
  (Objective-C++): loads and compiles the model off the main thread, runs a
  `VNCoreMLRequest` on the `ARFrame`'s `capturedImage` (orientation `.right`,
  `scaleFill`), drops frames while busy, and returns the boxes with the frame's
  portrait `displayTransform`.
- **C# seam** (`Runtime/AR/`): `IObjectDetector` and `ObjectDetection`;
  `CoreMlObjectDetector` (submits every 0.2 s through
  `ARCameraManager.subsystem.TryGetLatestFrame`, converts boxes to Unity screen
  pixels, logs `GhostMap yolo: <ms> ms` every 25 frames);
  `SimulatedObjectDetector` (demo mode: the virtual room's furniture, now
  listed by `SimulatedRoom.FurnitureInRoom`); `ObjectDetectors.Create`.
- **Pure C# pipeline** (`Runtime/Capture/`): `DetectionGeometry` (Vision box
  to screen), `FurnitureClassMap` (ADR-0011's table, desk vs table rule, which
  surface heights each class can sit under), `FurnitureIdentificationController`
  (floor point, room and existing-object checks, surface match, tracker,
  proposals, `TryBuild`, `Describe`).
- **Workflow**: `ScanWorkflowController.UpdateFurnitureIdentification`
  (AddObjects only, publishes nothing) feeds the tracker and names the matching
  surfaces; `TryAcceptAllDetectedFurniture` adds surfaces with their identified
  type and then the remaining proposals at default size, one revision each.
- **ADR-0007 removed**: `FurnitureTypeSuggester` and its tests are gone. A
  surface nobody has identified is offered as `generic`
  (`FurnitureDetectionController.SuggestedTypeFor`, `SetIdentifiedTypes`,
  `IsIdentified`).
- **UI**: `FurnitureDetectionHud` creates the detector on entering
  AddObjects, polls it every frame, adds an "Identify:" line to Details, and
  logs `GhostMap identify: <list>` when the list changes. `ScanGuide` shows
  "Found: ... Tap Add All." and "Looks like a desk" only for identified
  surfaces. `AutoScanHud` shows **Add All** when there are proposals.
- No scene change: everything new is created at runtime, so `Scanner.unity`
  was not regenerated.

## Contract impact
- None. No shared, schema, protocol or export-bundle change. The COCO class
  is kept as `IdentifiedFurniture.ClassName` for `R5`.

## How to test
1. `./tools/run_unity_tests.sh scanner`
2. iOS: Configure Scanner XR, Build Scanner, run on the iPhone. The Xcode
   console shows `GhostMap yolo: model ready, 80 classes` once you reach
   furniture, then `GhostMap yolo: <ms> ms, <n> objects` lines.
3. Device check (`docs/tasks/R4-yolo-furniture-identification.md`): scan the
   room, finish openings, then look at the bed, desk, table and chairs for a
   few seconds each. Note what "Found:" says, tap **Add All**, and compare the
   added sizes and positions with the room (Details shows each surface's
   numbers; `GhostMap detect:` logs them).

## Test results
All on the owner's Mac, 2026-10-11:
- Scanner EditMode **773/773** (710 after `R7`; +73
  `FurnitureIdentificationTests` cases, +2 `ScanGuideTests`, +2 detection
  tests, -14 suggester tests). Shared (292) and viewer (569) unchanged.
- `ConfigureXr` + `BuildScanner` + unsigned `xcodebuild` Release iphoneos:
  `BUILD SUCCEEDED`. `UnityFramework` links Core ML and Vision and defines the
  `GhostMapDetector_*` functions; the `.app` carries
  `Data/Raw/GhostMap/yolo11n.mlpackage` without `.meta` files. Project
  settings unchanged after the build.
- The committed model in a Swift + Vision check on the Mac: 80 COCO labels;
  identical boxes for an upright photo and the same photo as a landscape
  sensor image read with `.right`; 2.3 ms per frame warm.
- Not done: anything on an iPhone; the Simulator demo flow (demo mode's
  automatic scan does not finish there, see Known failures).

## Known failures
- None in the automated suites.
- Simulator demo mode still cannot finish the automatic scan
  (pre-existing, `docs/status/scanner.md`), so "Found: ..." has not been seen
  on screen; it is covered by `ScanGuideTests` and the workflow tests.

## Known limitations
- The box-to-screen maths is tested against ARKit's documented portrait
  `displayTransform`, not a camera. If proposals land in the wrong place on
  the phone, check `DetectionGeometry.UprightToRawImage` first.
- Per-item remove and world-space labels from the brief are not built; Undo
  removes the last added object.
- A chair or couch added from a surface by hand still takes the seat height
  (ADR-0006); through identification it keeps at least the default height.
- A TV on a stand is placed by where its box meets the floor, behind the
  stand, and never takes a surface.
- The simulated detector ignores walls (no occlusion test).

## Files most important to read next
- `docs/decisions/ADR-0011-on-device-yolo-furniture-identification.md` ("As built")
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/FurnitureIdentificationController.cs`
- `apps/scanner/Assets/Plugins/iOS/GhostMapDetector/GhostMapDetector.mm`
- `apps/scanner/Assets/GhostMap/Scanner/Tests/EditMode/FurnitureIdentificationTests.cs`

## Next task
- Owner: `R2` on the iPhone, including `R4`'s device check.
- Agent: `R5` (keep the detector's label in `SceneObjectModel`), then `R9`.
