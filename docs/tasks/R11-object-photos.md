# R11: A photo of each piece of furniture

**Decision:** owner, 2026-10-11 ("option 1"): keep the measured shapes and add a
photo of each object, rather than 3D-scanning furniture. Contract:
`docs/contracts/export-bundle-v1.md` section 5.
**Workstream:** Scanner and Viewer (browser) with a contract change
(Shared/Integration).
**Status:** Built 2026-10-11 on `integration/r11-object-photos`; the owner's
device check is part of `R2`.

## Why not real 3D shapes

| Option | Why not now |
| --- | --- |
| Apple Object Capture on the phone | Needs LiDAR; GhostMap must work on any iPhone and does not use LiDAR (`AGENTS.md` rule 6) |
| Photos to the Mac, photogrammetry there | Works without LiDAR, but takes 30-60 photos per object, fails for furniture against a wall, and moves capture off the phone (ADR-0012). Owner may revisit after `R2` with a new ADR |
| One photo to an AI image-to-3D model | Needs a cloud GPU (rule 6), invents what it cannot see, wrong scale |

## What it does

- **Identified furniture** (`R4`): each time the detector sees an item with
  clearly higher confidence (+0.05) than its current photo, the HUD crops that
  box from the same camera frame (`IObjectDetector.TryCapturePhoto`) and the
  tracker keeps it (`FurnitureIdentificationController.SetPhoto`). Add All, or
  adding the surface it sits on, gives the photo to the new object.
- **Everything else** (placed by hand, an unnamed surface): one crop of the
  camera view around the object (`ObjectPhotoRules.TryFrameObject`) the first
  frame after it is added, if at least 60% of it is on screen.
- **The workflow** keeps `ObjectPhotos` by object id, drops a photo when its
  object is undone, and never publishes for a photo (not scene state).
- **Export** writes `photos/<stem>.jpg` beside `objects/<stem>.glb` and the
  same bytes as `objectPhotos` in `room.html`; README.txt mentions them.
- **`room.html`** shows the photo at the top of the selected object's panel.

## Native plugin

`GhostMapDetector.mm` keeps the `CVPixelBuffer` of the last frame whose results
were published (released when the next one arrives) and
`GhostMapDetector_CropJpeg(x, y, w, h, maxSide, quality, buffer, capacity)`
crops it in the upright image (Core Image, orientation `.right`, the same space
as Vision's boxes), scales it to at most 480 px, and encodes a JPEG.
`DetectionGeometry.TryScreenToVisionBox` maps the screen box back to that image
using the frame's display transform.

## Limits

- **No photos in demo mode** (Editor, Simulator): encoding a JPEG in Unity needs
  the Image Conversion module, which the project leaves out; adding it is a
  dependency change (`AGENTS.md` rule 10).
- A photo is whatever the camera saw: it may include neighbouring furniture or
  be blurred if the phone was moving.
- Holding one ARKit camera buffer could, in theory, make ARKit drop a frame;
  watch for camera stutter in `R2`.

## Tests

- `ObjectPhotoTests`: JPEG and size rules, padding, framing an object on
  screen (in view, behind, mostly off screen), screen-to-image round trips.
- `FurnitureIdentificationTests`: when a photo is wanted, `SetPhoto`, and the
  workflow keeping photos through Add All, surface accept, undo and phase.
- `ExportBundleBuilderTests`: `photos/` entries byte for byte, `objectPhotos`
  in `room.html`, bad or orphan photos left out, names paired with models, and
  `ScanExporter` passing them through.
- Web viewer: `data.test.mjs` (optional, per id, damaged or orphan photos are
  warnings) and `template.test.mjs` (fill round trip, CSP, the `img` element).

## Device check (owner, in `R2`)

Add furniture three ways (Add All of identified items, a surface, a hand
placement). Finish, AirDrop the zip, open `room.html`, click each object: the
photo shows that object. Note any that are missing or wrong.
