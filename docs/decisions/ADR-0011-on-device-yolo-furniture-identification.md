# ADR-0011: Identify furniture on the phone with YOLO-n

- **Status:** Accepted (owner decision), not built
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
