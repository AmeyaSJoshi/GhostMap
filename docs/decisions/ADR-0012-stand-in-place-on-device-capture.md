# ADR-0012: Stand in one spot, capture everything on the phone, deliver Unity-ready files

- **Status:** Accepted (owner decision)
- **Date:** 2026-10-10
- **Supersedes:** parts of ADR-0004, as listed below
- **Related:** ADR-0005 to ADR-0009 (in `GhostMapDublinHacks`, being imported),
  ADR-0010 (Send to Computer), ADR-0011 (YOLO-n identification)

## Context

The MVP was designed around assisted capture: walk to four corners, aim at a
ceiling line, place furniture by hand. ADR-0004 deliberately avoided ARKit wall
detection and any learned model.

The owner's goal for GhostMap is now:

1. **Stand in one spot and point the phone around the room**; the room's walls,
   dimensions and height come out without walking.
2. **The phone identifies the things in the room** by itself.
3. **Furniture comes out as 3D files.**
4. **One button sends the result to the computer**, where it is used in Unity.
5. **All capture and recognition runs on the phone.**

Most of points 1, 3 and 4 already exist in the owner's hackathon repository
`GhostMapDublinHacks` (automatic room scan from ARKit planes, wall sweep,
per-object `.glb` export, UDP quick-send discovery). They pass their EditMode
suites and an iOS build, but none of them has run on an iPhone.

TRELLIS (Microsoft image-to-3D) was considered for point 3 and rejected: the
smallest version needs an NVIDIA GPU with at least 12 GB of memory, TRELLIS.2
about 24 GB, and no Core ML or iOS port exists. It cannot satisfy point 5.

## Decision

**Capture is stand-in-place and fully on device. The computer only receives.**

| Step | Primary method | Fallbacks |
| --- | --- | --- |
| Floor | ARKit horizontal plane, locked once (unchanged) | — |
| Walls and footprint | **Automatic room scan**: cluster ARKit vertical planes into walls while the user turns in place, pick four that enclose the user, derive corners | Wall sweep (ADR-0005), then walked corners (S3) |
| Height | ARKit ceiling plane when seen | Aim at the wall/ceiling line, then typed height |
| Doors and windows | ARKit door/window planes when labelled | Two-point capture on a derived wall (S5) |
| Furniture identity | **YOLO-n on device** (ADR-0011) | User picks the type |
| Furniture size and position | Detection matched to the measured ARKit surface it sits on | Per-type defaults, adjustable |
| Delivery | **Send to Computer** with automatic discovery (ADR-0010, ADR-0009) | Typed IP (developer only) |
| Output on the computer | Scene JSON, one `.glb` per object, and a whole-room `.glb` | — |

Rules that stay in force:

- **No LiDAR, no dense depth reconstruction, no Gaussian splatting or NeRF, no
  cloud inference, no generative 3D models.** Nothing leaves the phone except
  the finished scene sent to the user's own computer.
- The scene stays structured data (scene schema v1). Walls are still derived
  from four ordered corners; every room still passes `RoomValidator`.
- One room, four corners, flat floor and ceiling remain the supported shape.

## What this supersedes in ADR-0004

| ADR-0004 said | Now |
| --- | --- |
| AR plane detection is used exactly once, for the floor | Vertical, ceiling, door/window and horizontal furniture planes are also used. Mathematical wall planes remain the fallback |
| No learned models in the MVP | One small on-device detector (YOLO-n, ADR-0011) is allowed for identification only, never for geometry |
| The user aims at every corner | The user turns in place; aiming is the fallback |

ADR-0004's rejection of dense depth, LiDAR, splatting, NeRF and cloud
reconstruction is unchanged.

## Unity-ready output

The GhostMap Viewer (a Unity app) receives the scene and writes an export
folder:

```text
ghostmap-export/<room-name>/
├── scene.json          the SceneSnapshot, schema v1
├── room.glb            floor, ceiling, walls with openings cut     (to build)
└── objects/<id>.glb    one file per furniture object               (built in DublinHacks)
```

`.glb` is glTF 2.0 binary. A separate Unity project imports it with Unity's
glTFast package (`com.unity.cloud.gltfast`); Blender and most 3D tools open it
directly. Exported furniture is parametric geometry built from the measured
dimensions, not a scanned mesh.

## Consequences

**Positive**

- The capture matches how people want to use it: stand, turn, done.
- No server, no GPU, no account, no network beyond the user's own computer.
- Every fallback that already works on a device stays available.

**Negative**

- Automatic wall detection depends on ARKit finding vertical planes. Blank,
  glass or heavily furnished walls may not appear; the user then falls back to
  sweeping. How often that happens on a real iPhone is unknown.
- Standing in one spot means aiming at distant walls; floor-line error grows
  with distance (see ADR-0005's table).
- The project now carries a Core ML model and a native iOS plugin (ADR-0011).
- Every one of these paths still needs a physical-device session and a tape
  measure before it is trusted (`AGENTS.md` rule 12).

## Compliance

- `AGENTS.md` rule 6 is rewritten to match this ADR.
- Any further learned component, any off-device processing, or replacing the
  structured scene with a mesh needs a new ADR.
