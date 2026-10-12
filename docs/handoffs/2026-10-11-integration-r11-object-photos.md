# Handoff

## Branch
`integration/r11-object-photos`, stacked on `scanner/r4-yolo-identification`
(itself on #33, #32, #31, #30). Merge in order.

## Base commit
`8aeebe7` (R4: identification from floor lock)

## Head commit
The commit that adds this file.

## What changed
Task `R11`, the owner's "option 1": instead of 3D-scanning furniture (needs
LiDAR, a cloud GPU, or a Mac doing the capture), the phone keeps **a photo of
each object** and `room.html` shows it next to the measured size.

- **Native** (`GhostMapDetector.mm`): keeps the camera buffer of the last
  published frame; `GhostMapDetector_CropJpeg` crops it in the upright image
  (Core Image), scales to at most 480 px and encodes a JPEG.
- **C#**: `IObjectDetector.TryCapturePhoto(screenBox)`;
  `DetectionGeometry.TryScreenToVisionBox` (inverse of the box mapping);
  `ObjectPhotoRules` (JPEG and 256 KB limits, padding, framing an object on
  screen); the tracker asks for a photo when a sighting beats the current
  one by 0.05 (`WantingPhotos`, `SetPhoto`); the workflow keeps
  `ObjectPhotos` by object id, attaches them on Add All and surface accept,
  drops them on undo, never publishes for them; `FurnitureDetectionHud` takes
  the photos (at most two per frame); `ScanExporter` passes them to
  `ExportBundleBuilder`, which writes `photos/<stem>.jpg` and `objectPhotos`.
- **Viewer**: `data.js` reads optional `objectPhotos` (bad or orphan photos
  are warnings); the selection panel shows the photo; template rebuilt and
  copied to the scanner.
- **Contract**: `docs/contracts/export-bundle-v1.md` sections 1, 2 and new
  section 5. Additive; `formatVersion` stays 1.

## Contract impact
Export bundle v1, additive (rule 8): contract doc updated in its own commit,
tests on both sides (`ExportBundleBuilderTests`, `data.test.mjs`,
`template.test.mjs`), this handoff and the status pages.

## How to test
1. `./tools/run_unity_tests.sh scanner`
2. `cd apps/web-viewer && npm ci && npm test && npm run build` (the build must
   not change `dist/`).
3. iPhone (`R2`): add furniture by Add All, by a surface and by hand; Finish;
   open the bundle's `room.html`; click each object and check its photo.

## Test results
Owner's Mac, 2026-10-11:
- Scanner EditMode **805/805** (+26). Shared and viewer Unity suites untouched.
- Web viewer **46/46** (+5).
- `ConfigureXr`, `BuildScanner`, unsigned `xcodebuild`: `BUILD SUCCEEDED`,
  `GhostMapDetector_CropJpeg` linked, Core Image linked, project settings
  unchanged.
- Headless Chrome: a filled `room.html` with a real JPEG shows it in the bed's
  panel and hides the photo for the desk; no console errors.
- The plugin also passed a standalone `clang++ -fsyntax-only` against the
  iOS SDK.

## Known failures
None.

## Known limitations
- No photos in demo mode: Unity's JPEG encoder is in the Image Conversion
  module, which the project leaves out (adding it is a dependency-change PR,
  rule 10).
- Photos are untested on a real camera: orientation and crop placement are
  checked only through the maths and the Mac Vision check from `R4`.
- The plugin holds one ARKit camera buffer between frames.

## Files most important to read next
- `docs/tasks/R11-object-photos.md`
- `docs/contracts/export-bundle-v1.md` (section 5)
- `apps/scanner/Assets/Plugins/iOS/GhostMapDetector/GhostMapDetector.mm` (`CropJpeg`)

## Next task
Owner: `R2` on the iPhone (now includes the `R4` and `R11` checks). Agent:
`R5` (detector label), then `R9`.
