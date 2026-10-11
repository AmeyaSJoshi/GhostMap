# GhostMap Architecture Overview

Status: **frozen for MVP**, amended by ADR-0012 (stand-in-place, on-device
capture) and ADR-0013 (phone-built export, share-sheet transfer, browser viewer). Changing sections 1-11 requires an ADR in `docs/decisions/` per the
procedure in the implementation plan. Sections 12-13
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
┌──────────────────────────────┐            ┌──────────────────────────────┐
│  STANDARD IPHONE             │            │  ANY COMPUTER                │
│  apps/scanner                │            │                              │
│                              │  iOS share │  GhostMap-<room>.zip         │
│  ARKit tracking + planes     │  sheet     │   room.html  -> any browser  │
│  stand-in-place room scan    │ ─────────► │   room.glb   -> Unity        │
│  YOLO-n furniture (Core ML)  │  AirDrop,  │   objects/   -> Unity        │
│  validation                  │  Files,    │   scene.json                 │
│  export bundle builder       │  iCloud    │                              │
└──────────────────────────────┘            └──────────────────────────────┘
               │
               ▼
     shared/com.ghostmap.shared
     schema · geometry · validation · export · protocol
```

Legacy developer path, retiring with task `R10`: the scanner can still stream
snapshots over TCP to the desktop Unity Viewer (`apps/viewer`) for watching a
scan live.

---

## 3. Modules

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

Owns everything the user does. AR session, floor lock, room capture (automatic,
sweep, walked), height, openings, furniture identification and placement,
finalization, building the export bundle, keeping saved scans, and the share
sheet. The TCP client remains as a developer tool.

### 3.3 `apps/web-viewer` (planned, task `R8`)

A three.js app built into one self-contained HTML template. At export time the
scanner fills it with the room and furniture geometry and `scene.json` to make
`room.html`. View and measure only; it never reimplements a shared rule.

### 3.4 `apps/viewer` (frozen, retiring)

The desktop Unity app from tasks V1-V6: TCP server, revision arbitration,
rendering, orbit and dollhouse camera, editing, measurement, save/load. Frozen
by ADR-0013 and removed in task `R10`. Its geometry code (`GlbExporter`,
`FurnitureFactory.BuildParts`, `WallSliceGenerator`) moves to the shared
package in task `R6`.

Scene state flows through two layers. `ViewerSceneStore` holds the newest
accepted scanner snapshot. `ViewerEditableScene` passes it through unchanged
until a snapshot arrives with `finalized == true`; from then on it holds the
Viewer-owned copy that edits, Save and Load act on. Renderers, camera and
interaction controllers read only `ViewerEditableScene`.

---

## 4. Capture model

See `docs/decisions/ADR-0012-stand-in-place-on-device-capture.md`, which amends
`docs/decisions/ADR-0004-no-dense-depth-in-mvp.md`.

The user locks the floor, then stands in one spot and turns. ARKit's plane
detection and a small on-device detector propose the room and its contents;
GhostMap's own geometry and validators decide what is accepted. Every automatic
step has a manual fallback that does not depend on ARKit detecting anything.

| What | Primary | Fallback | Where it is built |
| --- | --- | --- | --- |
| Floor | ARKit horizontal plane, locked once | — | here, device-verified |
| Walls and corners | ARKit vertical planes clustered into four walls; corners where they meet | Sweep the floor line (ADR-0005), then walk to corners | primary and sweep: `GhostMapDublinHacks`; walked: here |
| Height | ARKit ceiling plane | Aim at the wall/ceiling line; type it | primary: `GhostMapDublinHacks`; fallbacks: here |
| Doors and windows | ARKit door/window planes | Two points on a derived wall plane | primary: `GhostMapDublinHacks`; fallback: here |
| Furniture identity | YOLO-n on device (ADR-0011) | User picks a type | not built |
| Furniture size and position | Matched ARKit horizontal surface (ADR-0006) | Floor-ray placement plus per-type defaults | primary: `GhostMapDublinHacks`; fallback: here |

The fallback math is a camera ray intersected with a plane GhostMap already
knows:

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

The detector only names objects. It never sets a corner, a wall or a dimension.

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

## 6. Synchronization model (developer stream)

See `docs/decisions/ADR-0002-snapshot-protocol.md`. Since ADR-0013 this is a
developer tool for watching a scan live in the Unity Viewer, not the product
transfer. Section 6.1 describes the product transfer.

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

### 6.1 Product transfer: export bundle and share sheet

See `docs/decisions/ADR-0013-phone-export-and-browser-viewer.md`. One button,
no IP typing (ADR-0010's requirement), no network code:

```text
Finalize
  -> scanner builds GhostMap-<room>-<yyyyMMdd-HHmm>.zip
       room.html, room.glb, objects/<id>.glb, scene.json, README.txt
  -> scan saved on the phone
  -> Send to Computer: UIActivityViewController (native plugin)
  -> AirDrop / Save to Files / iCloud / Mail
```

The bundle layout is a contract: `docs/contracts/export-bundle-v1.md` (task
`R6`). The UDP discovery from `GhostMapDublinHacks` (ADR-0009) is superseded
and is removed with the Unity Viewer.

---

## 7. Authority

See `docs/decisions/ADR-0003-scanner-authority.md`, amended by ADR-0013.

| Phase | Authoritative | Notes |
| --- | --- | --- |
| During scanning | Scanner | Developer stream, if connected, only renders |
| After finalization | Nobody edits | The export bundle is the record. Furniture is edited in Unity, or the room is rescanned |

The legacy Unity Viewer still lets a developer edit after finalization until
it is retired.

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

## 10. Room and furniture geometry

These rules produce the walls and furniture in every output: the exported
`.glb` files (on the phone, after task `R6`) and the legacy Unity Viewer.

Walls support rectangular openings **without CSG or runtime mesh booleans**. For
each wall, horizontal cuts (0, length, each opening start/end) and vertical cuts
(0, room height, each opening sill/top) are collected, sorted and de-duplicated.
Each resulting rectangular cell becomes a cuboid segment unless its center
falls inside an opening, in which case it is skipped.

Furniture is built from clean parametric primitives (a bed has a mattress, base
and headboard; a desk has a top and four legs; and so on), not anonymous boxes.

### 10.1 Export bundle

```text
GhostMap-<room>-<yyyyMMdd-HHmm>.zip
├── room.html           self-contained browser viewer, embedded geometry, no network
├── room.glb            floor, ceiling and wall segments with openings
├── objects/<id>.glb    one per furniture object
├── scene.json          the SceneSnapshot, schema v1
└── README.txt          how to import into Unity with glTFast
```

`.glb` files are glTF 2.0 binary in metres with +Y up. Unity imports them with
the glTFast package; Blender opens them directly. Built on the phone (task
`R6`); the per-object exporter exists today in `GhostMapDublinHacks`' Viewer.

---

## 11. Out of scope

Not implemented, per ADR-0012 and `AGENTS.md` rule 6:

dense RGB scanning · Gaussian splatting · NeRF reconstruction · LiDAR-like depth ·
generative 3D models (TRELLIS and similar) · any cloud or off-device processing ·
learned models other than the on-device YOLO-n detector · arbitrary curved rooms ·
multi-room and multi-floor capture · survey-grade accuracy · editing on the
computer outside Unity · any network transfer from the phone other than the iOS
share sheet · simultaneous two-device editing.

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

### 12.3 `apps/viewer/Assets/GhostMap/Viewer` (frozen, retiring)

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
