# ADR-0011: Identify furniture on the phone with YOLO-n

- **Status:** Accepted (owner decision). Built in task `R4` (2026-10-11);
  not yet run on an iPhone
- **Date:** 2026-10-10
- **Supersedes:** ADR-0007 in `GhostMapDublinHacks` (furniture type from ARKit's
  plane label)
- **Related:** ADR-0006 (surface measurement and `.glb` export), ADR-0012

## Context

The owner wants the phone to identify the things in a room by itself.

The hackathon repository has two partial answers, neither sufficient:

- **ADR-0006** measures horizontal surfaces 0.20-1.40 m high as furniture
  candidates, but the user must name each one.
- **ADR-0007** guesses the name from ARKit's coarse plane label (`Table`,
  `Seat`) plus size. ARKit has no "bed", cannot tell a desk from a dresser, and
  labels nothing that has no flat top.

An image detection model recognises objects from the camera image itself. The
"nano" size of YOLO (YOLO-n) is the smallest standard detector, has a few
million parameters, and runs in real time on an iPhone's Neural Engine through
Core ML. TRELLIS was considered and rejected in ADR-0012.

## Decision

**Use YOLO11n (Ultralytics, nano size, COCO-trained) on device to identify
furniture. Geometry still comes from ARKit and GhostMap's own math.**

### Pipeline

```text
ARKit camera frame (~5 per second, not every frame)
  -> YOLO11n via Core ML      class, confidence, 2D box
  -> floor point              ray through the box's bottom centre, intersected
                              with the locked floor plane (RayPlaneMath)
  -> match a measured surface the ARKit horizontal surface (ADR-0006) under the
                              box, which gives width, depth, height and yaw
  -> tracker                  same class within 0.5 m across frames = one object;
                              proposed after 5 consistent frames at >= 0.5 confidence
  -> "Found: bed, desk, 2 chairs"  -> one tap Add All, or remove any
```

Nothing is added to the room without the user's tap. A detection with no
matching surface gets the type's default dimensions, which the user can adjust.
A surface only matches a class whose top it can be (section "As built"), so a
chair tucked under a desk does not take the desk's size.

### Classes

COCO classes map to GhostMap types:

| YOLO class | GhostMap `type` |
| --- | --- |
| `bed` | `bed` |
| `chair` | `chair` |
| `couch` | `couch` |
| `dining table` | `table`, or `desk` when the matched surface is desk-sized |
| `tv` | `tv` |
| `refrigerator`, `potted plant`, `toilet`, `sink`, `oven`, `microwave`, `bench` | `generic` |

COCO has no desk or dresser class; those come from the size rules or the user.
To keep the detector's name when the type is `generic`, `SceneObjectModel`
gains an optional `label` string (for example `"potted plant"`). That is an
additive scene-schema v1 change and goes through the shared-contract procedure
(`AGENTS.md` rule 8) as its own task.

### Running it from Unity

Unity C# cannot call Core ML. The detector is a small native iOS plugin (Swift,
Vision + Core ML) behind a C# interface, `IObjectDetector`, so every rule above
is testable in EditMode with a fake detector, the same pattern as
`ISpatialProvider`. The `.mlpackage` is produced with
`yolo export model=yolo11n.pt format=coreml nms=True` and committed with its
SHA-256 recorded.

### Licence

Ultralytics YOLO is **AGPL-3.0**. That suits this public repository. Closed-source
or commercial distribution would need an Ultralytics Enterprise licence or a
switch to an Apache-2.0 detector (for example Apple's Core ML DETR model, which
the hackathon perception audit already timed).

## As built (task `R4`, 2026-10-11)

Where the build differs from the text above, this section wins.

### The model

| | |
| --- | --- |
| File | `apps/scanner/Assets/StreamingAssets/GhostMap/yolo11n.mlpackage` (5.2 MB) |
| Source weights | `yolo11n.pt`, SHA-256 `0ebbc80d4a7680d14987a577cd21342b65ecfd94632bd9a8da63ae6417644ee1` |
| Export | `yolo export model=yolo11n.pt format=coreml nms=True imgsz=640` |
| Export environment | Python 3.12.13, `ultralytics` 8.4.175, `torch` 2.7.1, `torchvision` 0.22.1, `numpy` 1.26.4, `coremltools` 9.0 (newer `torch` and `numpy` 2 fail the export) |
| `Manifest.json` | SHA-256 `f08764f5830f2348813b3a35df958d3ec14331bcf5d56d8f0b58c7f2337872c3` |
| `Data/com.apple.CoreML/model.mlmodel` | SHA-256 `c5bac47705cccb4052a2247c2766652fd8ac570ddde3acf74a869a77b1dfa417` |
| `Data/com.apple.CoreML/weights/weight.bin` | SHA-256 `7e28de4c897432a4413e15432685334a7c983e92f23d14936f2e5ca38c800785` |
| Checked on the Mac | Vision returns 80 COCO labels (including `bed`, `couch`, `dining table`, `tv`); the same five boxes for an upright photo and for that photo turned into a landscape sensor image read with orientation `.right`; 2.3 ms per frame warm on the owner's Mac. Phone timing is the `R4` device check |

The package lives in `StreamingAssets`, not `Plugins/iOS`: Unity copies it
verbatim into the app (`Data/Raw/GhostMap/`, checked in the built `.app`),
and the plugin compiles it on first use with `MLModel compileModelAtURL`, off
the main thread. Unity does not add an `.mlpackage` in a plugin folder to
Xcode's sources, and a post-build step would be one more thing to keep working.

### The plugin

`Assets/Plugins/iOS/GhostMapDetector/GhostMapDetector.mm`, Objective-C++ (the
same form as the `R7` share plugin; no Swift bridging). C functions:
`GhostMapDetector_Load(path)`, `_State`, `_LabelCount`, `_CopyLabel`,
`_CopyError`, `_Submit(nativeFrame, viewportWidth, viewportHeight)` and
`_Latest(out, max, &frameId, displayTransform[6], &elapsedMs)`. `Submit` takes
AR Foundation's `XRCameraFrame.nativePtr` (`UnityXRNativeFrame_1 { int version;
void* framePtr; }`, `framePtr` is the `ARFrame`), runs a `VNCoreMLRequest`
(`scaleFill`, orientation `.right`) on `capturedImage` on a background queue,
and drops the frame if one is already running. Core ML and Vision link
automatically (checked with `otool -L` on the built framework).

C# (`CoreMlObjectDetector`) submits at most every 0.2 s and turns each box into
Unity screen pixels with `DetectionGeometry.VisionBoxToScreen`, using the
frame's `displayTransform(for: .portrait, viewportSize:)` captured with it.
It logs `GhostMap yolo: <ms>` every 25 frames.

### Rules added while building

- **A surface matches only the classes it can be the top of**
  (`FurnitureClassMap.CanSitUnder`): `dining table` 0.55-1.00 m, `bed`
  0.25-0.85 m, `chair` and `bench` 0.35-0.60 m, `couch` 0.30-0.60 m; nothing
  else takes a surface.
- **A chair or couch keeps at least its default height**: ARKit measures the
  seat, never the back. Its footprint and yaw still come from the seat.
- **ARKit's plane label is no longer used.** A surface the detector has not
  named is offered as `generic` until it does.
- **Demo mode** (`SimulatedObjectDetector`) reports the virtual room's
  furniture with the COCO name YOLO would give it, so the flow runs in the
  Editor.

## Consequences

**Positive**

- Real names for the common furniture, including bed, which ARKit cannot label.
- Fully on device: no network, no account. The nano model is small enough for
  real-time use on recent iPhones; its latency on the owner's phone is measured
  in task R4.
- Measurement stays deterministic; the model never sets a dimension.

**Negative**

- New native plugin and a committed model file (a few MB).
- COCO's indoor coverage is limited; desks and dressers rely on size rules.
- Accuracy on the owner's iPhone in real rooms is unknown until a device test.
- AGPL licence constraint above.

## Compliance

- `AGENTS.md` rule 6 now allows this on-device detector and nothing broader.
- The model may only name objects. It must never move a corner, set a wall or
  change a measured dimension.
- Swapping the model, adding classes, or running it off device needs a new ADR.
