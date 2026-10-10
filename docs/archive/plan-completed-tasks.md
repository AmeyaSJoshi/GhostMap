# Archived: Completed Plan Tasks

Verbatim copy of implementation plan sections 15, 16, 17 and 34 as they
stood when the tasks were executed. These sections were removed from
`docs/plans/ghostmap-implementation-plan.md` on 2026-10-10 because every task
in them is complete; the plan keeps a summary table for each.

This file is history. For current behavior read `docs/status/` and
`docs/architecture/overview.md`.

---

# 15. Foundation Tasks

These tasks must be merged before Scanner and Viewer development split.

---

## Task F0: Repository + collaboration scaffold

**Files:**
- Create all top-level docs/config files described in sections 3–5.
- Create empty Unity project directories for scanner/viewer.
- Create `docs/status/*.md`.
- Create `docs/handoffs/README.md`.

**Produces:**
- collaboration rules;
- directory ownership;
- frozen architecture docs.

### Steps

- [ ] Create repository layout.
- [ ] Add Unity `.gitignore`.
- [ ] Add `.editorconfig` with UTF-8, LF, 4-space C# indentation.
- [ ] Add `.gitattributes`.
- [ ] Create `AGENTS.md` exactly from this plan.
- [ ] Put the approved project spec at `docs/specs/ghostmap-project-spec.md`.
- [ ] Put this plan at `docs/plans/ghostmap-implementation-plan.md`.
- [ ] Write four ADRs matching architecture decisions.
- [ ] Create workstream status files.
- [ ] Commit.

Commit:

```bash
git add .
git commit -m "chore(repo): scaffold GhostMap monorepo and collaboration docs"
```

Acceptance:

- clone contains no generated Unity folders;
- another developer can identify ownership and workflow from docs only.

---

## Task F1: Create shared local package and data schema

**Files:**
- `shared/com.ghostmap.shared/package.json`
- `shared/com.ghostmap.shared/Runtime/GhostMap.Shared.asmdef`
- all `Runtime/Domain/*.cs`
- `docs/contracts/scene-schema-v1.md`

**Produces:**
- exact scene data types.

### Tests first

Create tests that instantiate a complete scene snapshot and verify:

- four corners preserved;
- one door preserved;
- one object preserved;
- schemaVersion/revision preserved.

- [ ] Write failing serialization/data tests.
- [ ] Open a tiny test Unity project or one app project and confirm compile.
- [ ] Implement DTOs.
- [ ] Run EditMode tests.
- [ ] Document scene schema.
- [ ] Commit.

Commit:

```bash
git commit -m "feat(shared): define scene schema v1"
```

---

## Task F2: Geometry + validation package

**Files:**
- `Runtime/Geometry/*.cs`
- `Runtime/Validation/*.cs`
- shared EditMode tests.

**Required public interfaces:**

```csharp
public sealed class GhostCoordinateFrame
{
    public GhostCoordinateFrame(
        Vector3 worldOrigin,
        Vector3 worldRight,
        Vector3 worldUp,
        Vector3 worldForward);

    public Vector3 WorldToGhost(Vector3 world);
    public Vector3 GhostToWorld(Vector3 ghost);

    public Vector3 WorldDirectionToGhost(Vector3 direction);
    public Vector3 GhostDirectionToWorld(Vector3 direction);

    public Ray WorldRayToGhost(Ray worldRay);
    public Ray GhostRayToWorld(Ray ghostRay);
}
```

```csharp
public static class RayPlaneMath
{
    public static bool TryIntersectHorizontalPlane(
        Ray ray,
        float planeY,
        out Vector3 point);

    public static bool TryIntersectPlane(
        Ray ray,
        Plane plane,
        out Vector3 point);
}
```

```csharp
public static class RoomGeometry
{
    public static float PolygonAreaXZ(
        IReadOnlyList<Vector3> corners);

    public static bool HasSelfIntersectionXZ(
        IReadOnlyList<Vector3> corners);

    public static IReadOnlyList<WallDefinition>
        BuildWalls(RoomModel room);
}
```

```csharp
public static class RoomValidator
{
    public static ValidationResult ValidateRoom(
        RoomModel room);

    public static ValidationResult ValidateNewCorner(
        IReadOnlyList<CornerModel> existing,
        Vector3 candidate);
}
```

```csharp
public static class OpeningValidator
{
    public static ValidationResult Validate(
        OpeningModel opening,
        RoomModel room);
}
```

Define these shared support types as part of F2:

```csharp
public readonly struct ValidationResult
{
    public bool IsValid { get; }
    public string Error { get; }

    public ValidationResult(bool isValid, string error)
    {
        IsValid = isValid;
        Error = error;
    }

    public static ValidationResult Valid()
        => new ValidationResult(true, string.Empty);

    public static ValidationResult Invalid(string error)
        => new ValidationResult(false, error);
}
```

```csharp
public readonly struct WallDefinition
{
    public string StartCornerId { get; }
    public string EndCornerId { get; }
    public Vector3 Start { get; }
    public Vector3 End { get; }
    public Vector3 Tangent { get; }
    public float LengthM { get; }

    public WallDefinition(
        string startCornerId,
        string endCornerId,
        Vector3 start,
        Vector3 end);
}
```

### Required tests

- coordinate frame round-trip error < `1e-4`;
- horizontal ray intersection;
- reject parallel ray;
- rectangle area;
- reject bow-tie/self-crossing polygon;
- wall lengths;
- reject too-short wall;
- accept valid door;
- reject door extending outside wall;
- accept valid window;
- reject opening above ceiling.

Commit:

```bash
git commit -m "feat(shared): add capture geometry and validation"
```

---

## Task F3: Protocol v1 + fixtures

**Files:**
- `Runtime/Protocol/*.cs`
- `fixtures/*.json`
- `tools/send_fixture.py`
- `tools/inspect_snapshot.py`
- `docs/contracts/protocol-v1.md`

**Produces:**
- stable scanner/viewer boundary.

### Tests

- serialize/deserialize each message type;
- reject unknown protocol version;
- reject unknown message type;
- snapshot round trip;
- stale revision logic helper;
- fixture file parses and validates.

### Fixture room

`fixtures/valid-room-v1.json` must describe approximately:

```text
4.0 m x 3.0 m room
2.5 m height
1 door
bed
desk
chair
```

`room-with-door-window-v1.json` adds window.

`malformed-room-v1.json` intentionally violates one rule.

Commit:

```bash
git commit -m "feat(shared): freeze protocol v1 and sample fixtures"
```

**Foundation gate:**

Do not start app integration until:

- shared tests pass;
- fixture parses;
- protocol docs match code;
- both developers pull this commit.

---

# 16. Parallel Scanner Workstream

Tasks S1–S6 may proceed while Viewer tasks V1–V6 proceed.

---

## Task S1: Scanner Unity project + physical-device AR smoke test

**Files:**
- scanner `Packages/manifest.json`
- scanner ProjectSettings
- `Assets/GhostMap/Scanner/Runtime/Bootstrap/ScannerBootstrap.cs`
- scanner scene
- iOS post-build script
- `docs/status/scanner.md`

**Required scene objects:**

```text
AR Session
XR Origin
  AR Camera
Canvas
ScannerBootstrap
```

Attach:

- ARPlaneManager;
- ARRaycastManager;
- camera background support.

Set plane detection horizontal + vertical, even though floor is the only required detected plane.

### Physical-device test

On real iPhone:

- camera feed displays;
- AR session reaches tracking;
- plane manager reports floor candidate;
- screen displays:
  - session state;
  - notTrackingReason;
  - camera pose.

Do not proceed based only on Editor behavior.

Commit:

```bash
git commit -m "feat(scanner): establish AR Foundation device tracking"
```

---

## Task S2: Floor lock + Ghost coordinate frame

**Files:**
- `AR/ArSpatialProvider.cs`
- `Capture/FloorLockController.cs`
- `Workflow/ScanWorkflowController.cs`
- tests.

**ArSpatialProvider responsibilities:**

- expose AR camera;
- expose `ARSession.state`;
- expose `ARSession.notTrackingReason`;
- perform center-screen AR raycast against `PlaneWithinPolygon`;
- return hit plane/alignment;
- create screen ray.

**Public API:**

```csharp
public bool TryGetFloorHit(
    Vector2 screenPoint,
    out ARRaycastHit hit);

public Ray GetScreenRay(
    Vector2 screenPoint);

public bool IsTrackingGood { get; }
```

**Floor lock behavior:**

- crosshair center;
- user points at visible floor;
- raycast must hit horizontal plane;
- if tracking poor, button disabled;
- store floor hit;
- store camera forward;
- create `GhostCoordinateFrame`;
- transition to `FloorLocked`;
- increment scene revision;
- snapshot has empty room with height 0.

### Tests

Mock spatial provider:

- good horizontal hit -> lock succeeds;
- vertical hit -> rejected;
- poor tracking -> rejected;
- forward vector projection creates normalized frame.

Commit:

```bash
git commit -m "feat(scanner): lock floor and establish GhostMap coordinates"
```

---

## Task S3: Four-corner capture + closure verification

**Files:**
- `Capture/CornerCaptureController.cs`
- `UI/CornerCaptureHud.cs`
- tests.

**Capture algorithm:**

- get center-screen camera ray;
- intersect with stored AR-world floor Y;
- transform to Ghost coordinates;
- force y=0;
- validate;
- assign GUID ID;
- append;
- revision++;
- send/update local snapshot.

UI shows:

```text
Corner 1/4
Corner 2/4
Corner 3/4
Corner 4/4
```

After fourth:

```text
Verify first corner
```

On verification:

- capture floor point again;
- compute closure error;
- if > 0.15 m reject and return to corner capture;
- otherwise proceed.

Add Undo Corner.

### EditMode tests

- valid rectangle accepted;
- candidate too close rejected;
- self-crossing corner sequence rejected after four;
- closure 0.05 accepted;
- closure 0.12 accepted with yellow quality;
- closure 0.20 rejected;
- undo decrements count/revision correctly.

### Physical test

Scan a taped rectangle or known bedroom.

Confirm AR markers appear visually on captured corner locations.

Commit:

```bash
git commit -m "feat(scanner): add validated corner capture and closure check"
```

---

## Task S4: Height capture

**Files:**
- `Capture/HeightCaptureController.cs`
- UI controls;
- tests.

Workflow:

1. display numbered walls in a simple top-down mini preview;
2. user selects wall;
3. user aims at wall/ceiling boundary;
4. get camera ray;
5. convert the AR-world camera ray using `GhostCoordinateFrame.WorldRayToGhost`;
6. intersect that Ghost-space ray with the selected Ghost-space wall plane;
7. set `room.heightM = intersection.y`;
8. validate 2.0–4.0;
9. revision++;
10. proceed.

Fallback:

- manual numeric height field;
- range 2.0–4.0;
- explicit "Use manual height" button.

Tests:

- known ray hits wall at 2.5 m;
- parallel ray rejected;
- 1.5 m rejected;
- 5 m rejected;
- manual 2.6 accepted.

Commit:

```bash
git commit -m "feat(scanner): capture room height with manual fallback"
```

---

## Task S5: Doors, windows, furniture

**Files:**
- `Capture/OpeningCaptureController.cs`
- `Capture/ObjectPlacementController.cs`
- `UI/ObjectPlacementHud.cs`
- tests.

### Doors/windows

User:

1. chooses door/window;
2. chooses wall;
3. captures lower-left;
4. captures upper-right.

Both points come from the center-screen AR-world camera ray converted with `GhostCoordinateFrame.WorldRayToGhost`, then intersected with the selected generated Ghost-space wall plane.

Convert the resulting Ghost-space point to wall-local `u/v`.

Door:

```text
offset = min(u1,u2)
width = abs(u2-u1)
sill = 0
height = max(v1,v2)
```

Window:

```text
offset = min(u1,u2)
width = abs(u2-u1)
sill = min(v1,v2)
height = abs(v2-v1)
```

Validate.

### Furniture

- choose type;
- floor-plane center placement;
- default dimensions;
- dimension/yaw sliders;
- confirm.

Tests:

- wall local coordinate math;
- valid door;
- invalid outside-wall door;
- valid window;
- default furniture dimensions;
- negative dimension rejected.

Commit:

```bash
git commit -m "feat(scanner): capture openings and parametric furniture"
```

---

## Task S6: Scanner TCP client + complete scanner UI

**Files:**
- `Networking/ScannerNetworkClient.cs`
- `Networking/ScannerSnapshotPublisher.cs`
- `UI/ScannerHudController.cs`
- iOS plist post-build script;
- tests.

Connection screen:

```text
Laptop IP: [             ]
Port:      [47831]
[Connect]
```

Show:

```text
Connected
Disconnected
Retrying
```

Implementation requirements:

- `TcpClient`;
- background read/write loop;
- queued writes;
- no Unity API calls from network background thread;
- snapshot sent after every structural revision;
- heartbeat every 2 sec;
- pose at <= 5 Hz;
- retry every 2 sec after disconnect;
- snapshot resend after reconnect.

UI must expose:

- reset;
- undo;
- current phase;
- tracking quality;
- network status;
- closure error;
- finalize.

Scanner task is complete when it can produce valid JSON snapshots even if viewer is replaced by a simple TCP test receiver.

Commit:

```bash
git commit -m "feat(scanner): stream reliable snapshot protocol over local TCP"
```

---

# 17. Parallel Viewer Workstream

---

## Task V1: Viewer project + TCP server + fixture ingestion

**Files:**
- `Bootstrap/ViewerBootstrap.cs`
- `Networking/ViewerTcpServer.cs`
- `Scene/ViewerSceneStore.cs`
- tests.

Server:

- listens port 47831;
- accepts one scanner;
- reads lines;
- rejects >262144 bytes;
- deserializes;
- queues main-thread dispatch;
- logs protocol errors clearly.

`ViewerSceneStore`:

```csharp
public sealed class ViewerSceneStore
{
    public SceneSnapshot Current { get; }

    public bool TryApplyScannerSnapshot(
        SceneSnapshot snapshot,
        out string error);

    public event Action<SceneSnapshot> Changed;
}
```

Behavior:

- accept new session;
- for a new session ID, accept its first valid snapshot;
- for the same session ID, accept only `snapshot.revision > Current.revision`;
- treat equal revision as a harmless duplicate and ignore it;
- ignore lower/stale revisions;
- validate room before apply;
- never clear scene on disconnect.

Provide developer button:

```text
Load fixture
```

so viewer development never waits on scanner.

Commit:

```bash
git commit -m "feat(viewer): receive and store protocol v1 snapshots"
```

---

## Task V2: Floor, ceiling, walls

**Files:**
- `Rendering/RoomRenderer.cs`
- `Rendering/FloorCeilingRenderer.cs`
- `Rendering/WallRenderer.cs`
- tests.

`RoomRenderer` listens to SceneStore change and rebuilds.

For MVP, full scene rebuild on each snapshot is acceptable.

Requirements:

- floor mesh;
- ceiling mesh;
- four walls;
- colliders;
- clean hierarchy:

```text
RenderedRoom
├── Floor
├── Ceiling
├── Walls
│   ├── Wall_0
│   ├── Wall_1
│   ├── Wall_2
│   └── Wall_3
└── Objects
```

Use deterministic names containing IDs.

Tests:

- fixture yields 4 walls;
- wall transform length matches model;
- floor vertices match corners;
- ceiling y matches height.

Commit:

```bash
git commit -m "feat(viewer): render structured room shell"
```

---

## Task V3: Wall openings

**Files:**
- `Rendering/WallSliceGenerator.cs`
- `Rendering/WallRenderer.cs`
- tests.

Implement grid-cut wall segmentation from section 12.3.

Pure function:

```csharp
public static IReadOnlyList<WallSlice>
    BuildSlices(
        float wallLength,
        float wallHeight,
        IReadOnlyList<OpeningModel> openings);
```

Test:

- wall no openings -> one slice;
- one door -> no geometry in door rectangle;
- one window -> bottom/top/side geometry exists;
- door + window non-overlap works;
- invalid overlap rejected before rendering.

Visual test fixture must visibly contain a doorway and window hole.

Commit:

```bash
git commit -m "feat(viewer): render doors and windows as wall openings"
```

---

## Task V4: Parametric furniture + camera

**Files:**
- `Rendering/FurnitureRenderer.cs`
- `Rendering/FurnitureFactory.cs`
- `Interaction/OrbitCameraController.cs`
- tests where practical.

Factory API:

```csharp
public GameObject Create(
    SceneObjectModel model,
    Transform parent);
```

Every created root:

- named with type + ID;
- has object metadata component;
- has one selection collider;
- visually recognizable.

Implement orbit camera and frame-room function.

Add dollhouse button:

- hide ceiling;
- move camera to angled overhead view;
- frame room.

Commit:

```bash
git commit -m "feat(viewer): add parametric furniture and dollhouse camera"
```

---

## Task V5: Object editing + measurement

**Files:**
- `Interaction/ObjectSelectionController.cs`
- `Interaction/ObjectEditController.cs`
- `Interaction/MeasurementController.cs`
- `UI/InspectorPanelController.cs`
- tests.

Only allow scene editing if:

```text
snapshot.finalized == true
```

Selection:

- click object;
- highlight;
- populate inspector.

Drag:

- project cursor ray to floor;
- update X/Z.

Inspector:

- yaw;
- width;
- depth;
- height.

Every edit:

- validate;
- increment viewer-owned revision;
- update snapshot;
- rerender only object if easy, otherwise rebuild scene.

Measurement:

- two Physics.Raycast hits;
- line;
- distance label.

Commit:

```bash
git commit -m "feat(viewer): edit furniture and measure reconstructed spaces"
```

---

## Task V6: Persistence + polished viewer HUD

**Files:**
- `Persistence/ScenePersistence.cs`
- `UI/ViewerHudController.cs`
- tests.

UI:

- connection status;
- session ID shortened;
- revision;
- scan phase;
- save;
- load;
- dollhouse;
- reset camera;
- measure mode;
- object inspector.

Persistence tests:

- save fixture;
- load fixture;
- equality of semantic data;
- invalid file rejected.

Commit:

```bash
git commit -m "feat(viewer): save reload and control GhostMap scenes"
```

---

# 34. Plan Self-Review Result

This plan has been checked against the approved GhostMap spec with these decisions made explicit:

- **Spec coverage:** AR tracking, floor capture, corners, room height, doors, windows, furniture, networking, Unity reconstruction, dollhouse view, editing, measurement, persistence, testing, and parallel Git collaboration all have implementation tasks.
- **Contract consistency:** scanner and viewer share exactly one schema and protocol package; walls are derived rather than duplicated.
- **Coordinate consistency:** all post-floor-lock geometry is normalized into GhostMap coordinates and later wall captures use Ghost-space rays and Ghost-space wall planes.
- **Ownership consistency:** scanner is authoritative during scanning; viewer becomes authoritative only after finalization.
- **Networking consistency:** protocol v1 is full-snapshot based; stale/equal revisions are explicitly ignored.
- **Reliability scope:** single four-corner room is the required MVP. More general geometry is a stretch goal.
- **Undefined shared types:** `ValidationResult` and `WallDefinition` are explicitly defined in the foundation work.
- **No hidden depth assumption:** only the first floor acquisition uses AR plane raycast. Subsequent geometry uses camera rays plus known mathematical planes.
- **Parallel safety:** scanner and viewer work in separate Unity projects and only share versioned contracts through the local shared package.
