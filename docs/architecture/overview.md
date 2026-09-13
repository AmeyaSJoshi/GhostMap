# GhostMap Architecture Overview

Status: **frozen for MVP**. Changing anything in this document requires an ADR in
`docs/decisions/` per the procedure in the implementation plan.

---

## 1. Purpose

GhostMap converts one physical room into structured spatial data using a standard
non-LiDAR iPhone. The output is not a mesh scan. It is a small, explicit,
machine-readable scene description:

```text
room
├── heightM
├── corners[4]        ordered, floor-plane, Y = 0
├── openings[]        rectangular, attached to a derived wall
└── objects[]         parametric furniture with yaw + W/D/H
```

Walls are **derived** from consecutive corners. They are never serialized.

---

## 2. Physical topology

```text
┌────────────────────────┐          ┌────────────────────────┐
│  STANDARD IPHONE       │          │  LAPTOP                │
│  apps/scanner          │          │  apps/viewer           │
│                        │          │                        │
│  AR Foundation / ARKit │          │  TCP server :47831     │
│  floor lock            │  TCP     │  ViewerSceneStore      │
│  assisted capture      │ ───────► │  validation            │
│  SceneSnapshot rev N   │  NDJSON  │  semantic renderer     │
│                        │  full    │  edit / measure / save │
│  TCP client            │ snapshots│                        │
└────────────────────────┘          └────────────────────────┘
             │                                   │
             └───────────┬───────────────────────┘
                         ▼
              shared/com.ghostmap.shared
              schema · protocol · geometry
              validation · measurement math
```

Both apps are separate Unity projects. They share exactly one local package.

---

## 3. The three modules

### 3.1 `shared/com.ghostmap.shared`

The only source of truth for:

- **Domain** — `Vec3Dto`, `CornerModel`, `OpeningModel`, `SceneObjectModel`,
  `RoomModel`, `SceneSnapshot`, `ValidationResult`, `WallDefinition`.
- **Geometry** — `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry`,
  `WallGeometry`, `MeasurementMath`.
- **Protocol** — `ProtocolConstants`, `WireMessages`, `ProtocolSerializer`.
- **Validation** — `RoomValidator`, `OpeningValidator`, `FurnitureValidator`.

Neither app may redefine any of these. If an app needs a contract change, the
shared change is made, tested, documented, and merged **first**.

### 3.2 `apps/scanner`

Owns capture. Responsible for AR session lifecycle, floor lock, corner capture,
closure verification, height capture, openings, furniture placement, and the TCP
client that publishes snapshots.

### 3.3 `apps/viewer`

Owns reconstruction and, after finalization, editing. Responsible for the TCP
server, the scene store with revision arbitration, semantic rendering
(floor/ceiling/segmented walls/parametric furniture), orbit + dollhouse camera,
selection/drag/resize/rotate, measurement, and persistence.

---

## 4. Capture model — why this works without LiDAR

This is the central architectural decision. See
`docs/decisions/ADR-0004-no-dense-depth-in-mvp.md`.

AR plane detection is used **exactly once**: to find and lock the floor.

Everything captured afterwards uses a camera ray intersected with a plane that
GhostMap already knows mathematically:

| Capture | Ray | Plane |
| --- | --- | --- |
| Room corner | center-screen camera ray | locked floor plane (`Y = floorY`) |
| Furniture center | center-screen camera ray | locked floor plane |
| Room height | center-screen camera ray | derived wall plane |
| Door / window | center-screen camera ray | derived wall plane |

Wall planes are generated from captured corners:

```text
tangent = normalize(B - A)
normal  = normalize(cross(up, tangent))
plane   = plane through A with that normal
```

GhostMap therefore never waits for ARKit to fully detect a vertical wall. This is
substantially more reliable on a non-Pro iPhone than plane-detection-dependent
capture.

---

## 5. Coordinate frame

Established at the moment the user locks the floor:

```text
origin  = floor raycast hit point (AR world space)
up      = Vector3.up
forward = normalize(ProjectOnPlane(camera.forward, up))
right   = normalize(cross(up, forward))
```

Mapping into GhostMap space:

```text
origin  -> (0, 0, 0)
right   -> +X
up      -> +Y
forward -> +Z
```

Invariants:

- all distances are **meters**;
- the floor is **Y = 0** after normalization;
- corner `Y` is forced to `0`;
- `WorldToGhost` and `GhostToWorld` must round-trip within `1e-4`.

Wall-local coordinates for openings and height, given wall start `A` and
intersection point `Q`:

```text
u = dot(Q - A, tangent)   horizontal distance from wall start
v = Q.y                   height above floor
```

---

## 6. Synchronization model

See `docs/decisions/ADR-0002-snapshot-protocol.md`.

- Transport: **TCP**, port **47831**.
- Framing: **UTF-8, one JSON object per line**, terminated with `\n`.
- Maximum line length: **262144 bytes** — longer input is rejected.
- Payload: the **entire current `SceneSnapshot`**, resent after every structural
  mutation. There is no delta or event replay.

`revision` is monotonic per session. The viewer:

- accepts the first valid snapshot of a new `sessionId`;
- accepts a snapshot for the current session only if `revision > current`;
- ignores an equal revision as a harmless duplicate;
- ignores any lower revision as stale;
- validates the room before applying;
- **never** clears the displayed scene because the network dropped.

Required MVP message types: `hello`, `heartbeat`, `phone.pose`, `scene.snapshot`,
`scan.finalized`. `phone.pose` is debug/display only and capped at 5 Hz — the
room is never reconstructed from pose messages.

---

## 7. Authority

See `docs/decisions/ADR-0003-scanner-authority.md`.

| Phase | Authoritative | Other side |
| --- | --- | --- |
| During scanning | Scanner | Viewer renders snapshots, sends no scene edits |
| After `scan.finalized` | Viewer | Scanner is read-only/finished for that session |

Concurrent two-way editing is **not** implemented in the MVP.

---

## 8. Scanner state machine

One explicit enum, changed only by `ScanWorkflowController`. No UI button sets a
phase directly.

```text
Boot
 → WaitingForTracking
 → FindFloor
 → FloorLocked
 → CaptureCorners
 → VerifyClosure
 → CaptureHeight
 → AddOpenings
 → AddObjects
 → ReadyToFinalize
 → Finalized
```

Allowed back transitions:

```text
VerifyClosure → CaptureCorners
CaptureHeight → CaptureCorners
AddOpenings   → CaptureHeight
AddObjects    → AddOpenings
```

---

## 9. Validation gates

Bad scans are rejected, never silently rendered.

| Gate | Rule |
| --- | --- |
| Tracking | `ARSession.notTrackingReason` must be `None` before a critical capture |
| Corner spacing | ≥ 0.50 m from previous corner |
| Corner separation | ≥ 0.20 m from any non-neighbor corner |
| Polygon | no self-intersection in XZ |
| Area | ≥ 2.0 m² |
| Wall length | 0.5 m – 20 m |
| Internal angle | 35° – 145°, measured on the inside of the footprint, so a reflex (> 180°) corner is rejected |
| Closure error | ≤ 0.08 m excellent · ≤ 0.15 m acceptable · > 0.15 m reject |
| Room height | 2.0 m – 4.0 m |
| Opening width | 0.30 m – 4.0 m |
| Opening top | must not exceed room height |
| Opening extent | must lie fully inside the chosen wall |
| Furniture | positive dimensions within sane bounds |

---

## 10. Viewer rendering

Walls support rectangular openings **without CSG or runtime mesh booleans**. For
each wall, horizontal cuts (0, length, each opening start/end) and vertical cuts
(0, room height, each opening sill/top) are collected, sorted and de-duplicated.
Each resulting rectangular cell is rendered as a cuboid segment unless its center
falls inside an opening, in which case it is skipped.

Furniture is built from clean parametric primitives (a bed has a mattress, base
and headboard; a desk has a top and four legs; and so on), not anonymous boxes.
Each furniture root carries exactly one collider covering its full bounding box.

---

## 11. Out of MVP scope

Explicitly not implemented until the full MVP acceptance test passes:

dense RGB scanning · Gaussian splatting · NeRF reconstruction · LiDAR-like depth ·
fully automatic furniture dimensions · arbitrary curved rooms · multi-floor
buildings · automatic semantic recognition · survey-grade accuracy · simultaneous
two-device editing.
