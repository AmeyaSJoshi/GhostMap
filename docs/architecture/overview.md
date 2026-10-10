# GhostMap Architecture Overview

Status: **frozen for MVP**. Changing sections 1-11 requires an ADR in
`docs/decisions/` per the procedure in the implementation plan. Sections 12-13
describe the code as built and are updated whenever the code changes.

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

Scene state flows through two layers. `ViewerSceneStore` holds the newest
accepted scanner snapshot. `ViewerEditableScene` passes it through unchanged
until a snapshot arrives with `finalized == true`; from then on it holds the
Viewer-owned copy that edits, Save and Load act on. Renderers, camera and
interaction controllers read only `ViewerEditableScene`.

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

### 6.1 Connection setup sits above the transport

How the phone finds the computer is separate from what it sends. Per
`docs/decisions/ADR-0007-one-button-computer-transfer.md`, the product flow is
one-button **Send to Computer** after finalization, and a normal user never
types an IP address or port.

```text
Discovery / pairing / Send UX      connection setup, user-facing   (not built)
              |
        TCP connection
              |
protocol-v1 full SceneSnapshot messages
              |
Viewer scene store / editable scene / rendered room
```

Everything below the first line is unchanged by discovery. Today the scanner's
Laptop IP field is the only way to connect; it becomes a developer fallback once
`I1B` is built. Live streaming while scanning stays supported but is optional.

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

---

## 12. Code map

Every runtime type, by project and folder. Tests mirror these names with a
`Tests` suffix under each project's `Tests/` folder.

### 12.1 `shared/com.ghostmap.shared/Runtime`

| Folder | Types | Role |
| --- | --- | --- |
| `Domain` | `Vec3Dto`, `CornerModel`, `OpeningModel`, `SceneObjectModel`, `RoomModel`, `SceneSnapshot` | Wire and save format (scene schema v1) |
| `Domain` | `ValidationResult`, `WallDefinition` | Validation result; a derived wall |
| `Geometry` | `GhostCoordinateFrame` | AR world ↔ Ghost space |
| `Geometry` | `RayPlaneMath` | Ray / floor-plane and ray / wall-plane intersection |
| `Geometry` | `RoomGeometry`, `WallGeometry` | Area, self-intersection, interior angles, wall derivation, wall-local `u`/`v` |
| `Geometry` | `MeasurementMath` | Distance, horizontal distance, area, volume, perimeter |
| `Validation` | `RoomValidator`, `OpeningValidator`, `FurnitureValidator`, `ClosureQuality` | Every limit in section 9 |
| `Protocol` | `ProtocolConstants`, `WireMessages`, `ProtocolSerializer`, `SnapshotRevisionPolicy` | Protocol v1 |

### 12.2 `apps/scanner/Assets/GhostMap/Scanner`

| Folder | Types | Role |
| --- | --- | --- |
| `Runtime/AR` | `ISpatialProvider`, `FloorHit`, `ArSpatialProvider` | The only seam onto AR Foundation; faked in tests |
| `Runtime/Workflow` | `ScanPhase`, `ScanWorkflowController` | Phase machine, session id, revision, current snapshot |
| `Runtime/Capture` | `FloorLockController`, `CornerCaptureController`, `HeightCaptureController`, `OpeningCaptureController`, `ObjectPlacementController` | One plain-C# controller per capture step |
| `Runtime/Networking` | `ISnapshotSink`, `ScannerSnapshotPublisher`, `ScannerNetworkClient` | Revision changes → messages → TCP |
| `Runtime/UI` | `FloorLockHud` | Composition root: builds every controller and the workflow; Reset rebuilds them |
| `Runtime/UI` | `CornerCaptureHud`, `HeightCaptureHud`, `OpeningCaptureHud`, `ObjectPlacementHud` | Per-phase buttons, readouts, world-space markers |
| `Runtime/UI` | `ScannerHudController` | Connect, network status, Reset, Finalize; owns the network thread |
| `Runtime/Bootstrap` | `ScannerBootstrap` | S1 tracking diagnostics readout |
| `Editor` | `ScannerSceneBuilder` | Generates `Scanner.unity` (menu **GhostMap > Build Scanner Scene**) and verifies its wiring |
| `Editor` | `ScannerBuild`, `ScannerXrSettings`, `ScannerInputSettings`, `ScannerIosPostBuild` | Two-step iOS build: **Configure Scanner XR (iOS)**, then **Build Scanner (iOS)** into `apps/scanner/Builds/iOS` |

### 12.3 `apps/viewer/Assets/GhostMap/Viewer`

| Folder | Types | Role |
| --- | --- | --- |
| `Runtime/Networking` | `ViewerTcpServer`, `LineReader` | Accept one scanner, frame lines, deserialize on background threads |
| `Runtime/Bootstrap` | `ViewerSession` | Drains the server queue on the main thread and dispatches messages |
| `Runtime/Bootstrap` | `ViewerBootstrap` | Composition root: wires session, scenes, renderer, camera, interaction |
| `Runtime/Scene` | `IViewerSceneSource`, `ViewerSceneStore`, `ViewerEditableScene`, `SceneSnapshotValidator`, `FixtureLoader` | Scene ownership layers (section 3.3) and validation |
| `Runtime/Rendering` | `RoomRenderer`, `FloorCeilingRenderer`, `WallRenderer`, `WallSliceGenerator` | Room shell and opening segmentation (section 10) |
| `Runtime/Rendering` | `FurnitureRenderer`, `FurnitureFactory`, `SceneObjectBinding`, `RoomBounds` | Parametric furniture, object identity on colliders, framing bounds |
| `Runtime/Interaction` | `OrbitCameraRig`, `OrbitCameraController` | Camera maths (pure) and its input wrapper |
| `Runtime/Interaction` | `ObjectSelectionController`, `SelectionOutlineBuilder`, `ObjectEditController`, `MeasurementController` | Select, highlight, edit, measure |
| `Runtime/Interaction` | `ViewerInteractionRouter` | The single per-frame mouse reader; routes clicks and drags |
| `Runtime/Persistence` | `ScenePersistence` | Atomic save and validated load of a `SceneSnapshot` |
| `Runtime/UI` | `ViewerHudController`, `InspectorPanelController` | Status, buttons, inspector fields |
| `Editor` | `ViewerSceneBuilder` | Generates `Viewer.unity` (menu **GhostMap > Build Viewer Scene**) |

Both `.unity` scenes are generated by their builders. After changing a builder,
regenerate and commit the scene rather than editing the scene by hand.

## 13. Runtime model

**Scanner.** Everything runs on the Unity main thread except
`ScannerNetworkClient.PumpOnce`, which a background thread calls every 100 ms.
That thread touches only the socket and a locked outgoing queue, never a Unity
API. Each frame `ScannerHudController.Update` calls
`ScannerSnapshotPublisher.Tick`, which enqueues a `scene.snapshot` when the
workflow's revision changed. On every (re)connect the client clears its queue
and sends `hello` plus the current snapshot.

**Viewer.** `ViewerTcpServer` runs an accept thread and one read thread per
connection; they only parse lines and enqueue messages. `ViewerBootstrap.Update`
drains the queue on the main thread, so all scene, rendering and UI work stays
single-threaded. Every accepted snapshot or local edit rebuilds the whole
`RenderedRoom`.

**Known gaps against sections 6-7** (tracked in `docs/status/`):

- the scanner does not send `phone.pose`;
- the Viewer ignores `heartbeat` and has no read timeout, so a silently dropped
  connection is not shown as disconnected;
- the Viewer ignores `scan.finalized` and keys ownership on
  `snapshot.finalized`.
