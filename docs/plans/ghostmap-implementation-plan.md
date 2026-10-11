# GhostMap Implementation Plan

> **For agentic workers:** Execute this plan task-by-task. Do not skip the dependency gates or acceptance tests. Keep the status files and handoffs current so another worker can take over with no chat history.

**Goal:** Stand in one spot in a room, point a standard non-LiDAR iPhone around it, and get a structured 3D model of the room and its furniture. The phone measures the room, identifies the furniture on device, builds Unity-ready files plus a browser viewer, and with one button sends them to the user's computer.

**Architecture:** An iPhone **Scanner** (Unity) does all capture, recognition and export, using one shared local package for the scene schema, geometry, validation and export. **Send to Computer** is the iOS share sheet (AirDrop). On the computer, `room.html` opens in any browser and the `.glb` files go into Unity. The desktop Unity **Viewer** is frozen and retiring. Direction: ADR-0012 and ADR-0013.

**Tech Stack:** Unity 6000.3.24f1, AR Foundation 6.3.x, Apple ARKit XR Plugin 6.3.x, C#, Core ML (YOLO11n) and the iOS share sheet via small native plugins, Unity Test Framework, uGUI, glTF 2.0 binary, three.js for the browser viewer. TCP + newline-delimited JSON remains a developer tool.

**Spec:** `docs/specs/ghostmap-project-spec.md`

## Where the project is

| Stage | Tasks | State |
| --- | --- | --- |
| A — Foundation | `F0`-`F4` | Complete |
| B — Assisted-capture MVP | `S1`-`S6`, `V1`-`V6` | Complete; scanner verified on a physical iPhone |
| C — Stand-in-place GhostMap | `R1`-`R10` (section 18) | **`R1` merged 2026-10-11. Next: confirm the test suites, then `R2`** |

Task `R1` brought the owner's hackathon work into this repository: the guided
scan UI, automatic room scan, wall sweep, furniture surface detection and
per-object `.glb` export. All of it passed its EditMode suites; none of it has
run on an iPhone.

Section numbers in this plan are stable and are cited from code comments.
Sections whose content lives in another canonical document are short pointers.
The full original text of every completed task is in git:
`git show f5a7d30:docs/plans/ghostmap-implementation-plan.md`.

# 0. Read This Before Changing Anything

This is the execution contract for the project.

## Product promise

GhostMap must do this, on a standard non-LiDAR iPhone, with everything running on the phone:

1. Launch, establish AR tracking and lock the floor.
2. Let the user **stand in one spot and turn** while GhostMap finds the walls and derives the room's four corners and dimensions. Sweep and walked-corner capture remain as fallbacks.
3. Capture the room height, from the ceiling plane when ARKit sees it, otherwise by aiming or typing.
4. Capture doors and windows, automatically when ARKit labels them, otherwise by two-point aiming.
5. **Identify the furniture on device** with a YOLO-n detector and measure each piece from the ARKit surface it sits on. The user confirms with one tap.
6. Finalize. The phone **builds the export bundle**: `room.html`, a whole-room `room.glb`, one `.glb` per piece of furniture, and `scene.json`. It keeps every finalized scan so it can be sent again.
7. **Send to Computer** with one button: the iOS share sheet, AirDrop to a Mac (or Files, iCloud, Mail). No IP address, port or network setup.
8. On the computer, **double-click `room.html`** to orbit, use dollhouse mode, click an object for its name and size, and measure, in any browser, offline.
9. **Drag the `.glb` files into Unity**; they arrive in metres with +Y up and the furniture standing on the floor.

## Does NOT promise

- dense 3D scanning, LiDAR-like depth, Gaussian splatting or NeRF;
- generative 3D models of furniture (TRELLIS and similar need a large NVIDIA GPU; ADR-0012);
- any cloud processing, account or internet requirement;
- photoreal or textured meshes; exported furniture is parametric geometry built from measured dimensions;
- arbitrary curved rooms, multi-room or multi-floor capture;
- recognition of every object; only the detector's furniture-scale classes;
- centimetre survey-grade accuracy;
- editing on the computer outside Unity; the browser viewer is view-and-measure only;
- a live view of the room on the computer while scanning (developer TCP tool only);
- simultaneous editing from both devices.

The project wins by producing **structured geometry that drops straight into Unity**, not by pretending an ordinary camera is a depth sensor.

---

# 1. Hard Reliability Rules

These rules exist because the product must survive a live hackathon demo.

## 1.1 Single-room first

The first release only supports:

- one room;
- four ordered floor corners;
- flat floor;
- flat ceiling;
- room height between 2.0 m and 4.0 m;
- wall lengths between 0.5 m and 20 m;
- rectangular door/window openings;
- non-overlapping openings;
- furniture represented by parametric objects.

Only add arbitrary polygons after the four-corner version passes all field tests.

## 1.2 Use AR wall detection, never depend on it

The primary capture path (ADR-0012) builds walls from ARKit's detected vertical planes while the user turns in place. ARKit can miss blank, glass or furnished walls, so the scanner must always offer a path that does not need them:

- the floor is locked once from a detected horizontal plane;
- if fewer than four trustworthy walls are found, the scanner says which direction to look, then offers the wall sweep (ADR-0005);
- the walked-corner path (camera ray plus the mathematical floor plane) remains the last fallback;
- height, door and window capture fall back to mathematically derived wall planes.

A scan must never stall because ARKit did not report a plane.

## 1.3 Reject bad scans instead of rendering bad scans

Never silently accept a scan that violates validation.

Required checks:

- AR session must be tracking before a capture.
- `ARSession.notTrackingReason` must be `None` before critical captures.
- Corner must be at least 0.50 m from previous corner.
- Four corners must not self-intersect.
- Room area must be at least 2 m².
- User must verify the first corner after capturing all four corners.
- Closure error:
  - green: <= 0.08 m
  - yellow: > 0.08 m and <= 0.15 m
  - reject/rescan: > 0.15 m
- Captured room height must be 2.0–4.0 m.
- Opening width must be 0.30–4.0 m.
- Opening top must not exceed room height.
- Opening must lie inside the selected wall.
- Furniture dimensions must be positive and within defined sane bounds.

## 1.4 Snapshot synchronization, not event replay

Do not make the desktop reconstruct the room from a long chain of tiny mutation events.

After every meaningful scanner mutation, send the **entire current SceneSnapshot**.

Benefits:

- idempotent;
- easy reconnection;
- easy debugging;
- no hidden divergence;
- viewer can ignore stale revisions;
- a new worker can understand the state from one JSON object.

The scene will be small enough that this is cheap.

## 1.5 Source-of-truth ownership

During scanning:

- scanner owns scene state;
- viewer renders scanner snapshots;
- viewer does not send scene edits back.

After `scan.finalized`:

- viewer owns editable scene state;
- scanner becomes read-only/finished for that session.

Do not implement concurrent two-way scene edits in MVP.

---

# 2. Exact Environment

## 2.1 Unity

Pin the repository to:

- Unity Editor: `6000.3.24f1`

Every developer must use the exact same editor revision.

Do not upgrade Unity during the hackathon unless a blocking engine bug is proven.

Both Unity projects must commit:

- `ProjectSettings/ProjectVersion.txt`
- `Packages/manifest.json`
- `Packages/packages-lock.json`

Never commit:

- `Library/`
- `Temp/`
- `Logs/`
- `obj/`
- generated iOS Xcode builds.

## 2.2 Scanner packages

Scanner project needs:

- AR Foundation `6.3.1`;
- Apple ARKit XR Plugin `6.3.1`;
- Unity Test Framework;
- Unity UI/uGUI;
- shared local package `com.ghostmap.shared`.

Use package versions committed in `packages-lock.json`; once the scanner successfully builds to a real iPhone, freeze those versions.

## 2.3 Viewer packages

Legacy: the Unity Viewer is frozen (ADR-0013). The browser viewer (`apps/web-viewer/`, task `R8`) uses three.js, bundled into one HTML file, with no runtime network requests.

Viewer project needs only:

- Unity Test Framework;
- Unity UI/uGUI;
- shared local package `com.ghostmap.shared`.

Avoid adding third-party networking/JSON packages unless a tested blocker proves they are required.

## 2.4 iOS settings

Scanner:

- ARM64 only;
- camera usage description present;
- local-network usage description present;
- require ARKit-capable device;
- device orientation: portrait for scanner MVP;
- no simulator dependency;
- build/test on a physical iPhone.

The scanner initiates an outgoing local TCP connection, so iOS local-network permission must be handled.

Add to generated `Info.plist`:

- `NSCameraUsageDescription`
- `NSLocalNetworkUsageDescription`

Use an iOS post-build script so these values are reproducible and not manually re-added every build.

---

# 3. Repository Layout

The repository has exactly this high-level structure:

```text
GhostMap/
├── AGENTS.md                    execution contract, read first
├── README.md
├── .editorconfig  .gitattributes  .gitignore
├── .github/pull_request_template.md
│
├── docs/
│   ├── README.md                documentation map
│   ├── specs/                   product spec
│   ├── plans/                   this plan
│   ├── architecture/            architecture overview and code map
│   ├── contracts/               scene-schema-v1.md, protocol-v1.md
│   ├── decisions/               ADR-0001 .. ADR-0004 and an index
│   ├── status/                  shared.md, scanner.md, viewer.md, integration.md
│   └── handoffs/                one file per handoff, never overwritten
│
├── shared/
│   ├── com.ghostmap.shared/     the shared Unity package
│   │   ├── Runtime/{Domain,Geometry,Protocol,Validation}/
│   │   └── Tests/Editor/
│   └── TestProject/             host project that only runs the shared tests
│
├── apps/
│   ├── scanner/                 iOS Unity project: capture, recognition, export, share
│   │   └── Assets/GhostMap/Scanner/{Runtime/{AR,Bootstrap,Capture,Networking,UI,Workflow},Editor,Tests/EditMode}
│   ├── web-viewer/              three.js source of room.html            (task R8)
│   └── viewer/                  desktop Unity project, frozen, retiring (task R10)
│       └── Assets/GhostMap/Viewer/{Runtime/{Bootstrap,Networking,Scene,Rendering,Interaction,Persistence,UI},Editor,Tests/EditMode}
│
├── fixtures/                    valid-room-v1, room-with-door-window-v1, malformed-room-v1
└── tools/                       inspect_snapshot.py, send_fixture.py, run_unity_tests.sh
```

The scanner and viewer manifests reference the shared package from their own `Packages/manifest.json` files with:

```json
"com.ghostmap.shared": "file:../../../shared/com.ghostmap.shared"
```

The path is relative to each project's `Packages` folder.

Do not duplicate shared models inside either app.

**As built:** `shared/TestProject/` was added in F1 because a Unity package
cannot run its own tests; it contains no application code. File-level detail
for every project is in `docs/architecture/overview.md` section 12.

---

# 4. Parallel-Development Contract

This section is mandatory.

## 4.1 Directory ownership

After foundation tasks F0–F3 are merged:

### Scanner workstream owns

```text
apps/scanner/**
docs/status/scanner.md
```

### Viewer workstream owns

```text
apps/viewer/**
docs/status/viewer.md
```

### Shared/integration workstream owns

```text
shared/**
fixtures/**
docs/contracts/**
docs/decisions/**
docs/status/shared.md
docs/status/integration.md
tools/**
```

Do not casually edit another workstream's files.

If a workstream needs a shared contract change:

1. stop feature implementation;
2. write the proposed change into `docs/status/shared.md`;
3. make the shared change on a dedicated branch;
4. update schema/protocol docs;
5. update shared tests;
6. merge it first;
7. both scanner/viewer branches pull/rebase it;
8. continue app work.

This prevents two agents from silently inventing incompatible interfaces.

## 4.2 Branch naming

Use:

```text
foundation/<task>
shared/<task>
scanner/<task>
viewer/<task>
integration/<task>
fix/<scope>-<description>
```

Examples:

```text
shared/protocol-v1
scanner/corner-capture
viewer/wall-rendering
integration/device-to-viewer
```

## 4.3 Commit convention

Use:

```text
feat(scanner): ...
feat(viewer): ...
feat(shared): ...
fix(scanner): ...
fix(viewer): ...
test(shared): ...
docs(architecture): ...
chore(repo): ...
```

Every commit should be small enough that another person can review it quickly.

## 4.4 Status files

At the beginning of work, read:

```text
AGENTS.md
docs/status/shared.md
docs/status/<your-workstream>.md
docs/contracts/protocol-v1.md
docs/contracts/scene-schema-v1.md
```

At the end of work, update only your own status file.

Required format:

```markdown
# Scanner Status

## Current state
- Short factual description of what works.

## Last verified commit
- `<git sha>`

## Tests run
- Command
- Result

## Interfaces consumed
- Shared type/method names used by this workstream.

## Known issues
- Concrete reproducible issues only.

## Next safe task
- Exact next step.

## Do not touch
- Files/behavior currently being changed by another workstream.
```

## 4.5 Handoff files

For any meaningful branch handoff, create a new file, never overwrite an old one:

```text
docs/handoffs/YYYY-MM-DD-<workstream>-<short-description>.md
```

Required contents:

```markdown
# Handoff

## Branch
`scanner/corner-capture`

## Base commit
`<sha>`

## Head commit
`<sha>`

## What changed
- ...

## Contract impact
- None
or
- Exact contract change and version.

## How to test
1. ...
2. ...

## Test results
- ...

## Known failures
- ...

## Files most important to read next
- ...

## Next task
- ...
```

A new worker must be able to continue from the handoff without chat history.

## 4.6 Pull-request checklist

Every PR description must contain:

```markdown
## Summary
...

## Workstream
Scanner / Viewer / Shared / Integration

## Contract change
No / Yes

## Tests
- ...

## Physical-device test
Not required / Passed / Failed with reason

## Handoff
`docs/handoffs/...`

## Known issues
- ...

## Screenshots/video
- if UI/visual behavior changed
```

## 4.7 Unity merge protection

Never commit Unity-generated folders.

Set `.gitattributes` for text serialization:

```gitattributes
*.unity text eol=lf
*.prefab text eol=lf
*.asset text eol=lf
*.meta text eol=lf
*.cs text eol=lf
*.json text eol=lf
*.md text eol=lf
```

Use visible meta files and Force Text serialization in both projects.

Because scanner and viewer are separate Unity projects, scene/prefab conflicts should already be rare.

---

# 5. `AGENTS.md` Contract

Moved. [`AGENTS.md`](../../AGENTS.md) at the repository root is the contract;
its numbered rules are authoritative and were created from this section in F0.

---

# 6. Shared Scene Schema v1

Moved. The frozen schema is [`docs/contracts/scene-schema-v1.md`](../contracts/scene-schema-v1.md),
and the code in `shared/com.ghostmap.shared/Runtime/Domain/` is the source of truth.

| Former section | Now |
| --- | --- |
| 6.1 `Vec3Dto` | schema section 2 |
| 6.2 `CornerModel` | schema section 3 |
| 6.3 `OpeningModel` | schema section 4 |
| 6.4 `SceneObjectModel` | schema section 5 |
| 6.5 `RoomModel` (walls derived, never serialized) | schema section 6 |
| 6.6 `SceneSnapshot` | schema section 7 |

Conventions that apply everywhere: meters, degrees, +Y up, floor at Y = 0, +Z
forward and +X right from the floor-lock moment.

---

# 7. Network Protocol v1

Moved. The frozen protocol is [`docs/contracts/protocol-v1.md`](../contracts/protocol-v1.md),
and the code in `shared/com.ghostmap.shared/Runtime/Protocol/` is the source of truth.

| Former section | Now |
| --- | --- |
| Transport (TCP 47831, UTF-8 NDJSON, 262144-byte lines) | protocol section 1 |
| 7.1 Message header | protocol section 2 |
| 7.2 Required message types | protocol section 3 |
| 7.3 Serialization | protocol section 4 |
| 7.4 Reconnection | protocol section 6 |

---

# 8. Coordinate System and Capture Math

This section is central to making the non-LiDAR approach work.

## 8.1 Floor-lock frame

When user locks the floor:

Inputs:

- AR camera world position `C`;
- AR camera world forward `F`;
- floor raycast hit point `P`.

Create:

```csharp
Vector3 up = Vector3.up;

Vector3 forward = Vector3.ProjectOnPlane(
    camera.transform.forward,
    up).normalized;

Vector3 right = Vector3.Cross(up, forward).normalized;

Vector3 origin = new Vector3(
    floorHit.x,
    floorHit.y,
    floorHit.z);
```

`origin.y` is the real AR-world floor height.

The GhostMap coordinate frame is:

```text
origin -> (0, 0, 0)
right  -> +X
up     -> +Y
forward-> +Z
```

## 8.2 World to GhostMap

```csharp
public Vector3 WorldToGhost(Vector3 world)
{
    Vector3 delta = world - origin;

    return new Vector3(
        Vector3.Dot(delta, right),
        Vector3.Dot(delta, up),
        Vector3.Dot(delta, forward));
}
```

## 8.3 GhostMap to World

```csharp
public Vector3 GhostToWorld(Vector3 ghost)
{
    return origin
        + right * ghost.x
        + up * ghost.y
        + forward * ghost.z;
}
```

Unit-test round trips.

## 8.4 Corner capture without depth

After floor lock, corner capture does **not** depend on a new AR raycast result.

Use:

```csharp
Ray screenRay = arCamera.ScreenPointToRay(screenCenter);
```

Intersect with the known mathematical floor plane.

Equivalent math:

```csharp
float floorYWorld = floorOrigin.y;

float denom = screenRay.direction.y;

if (Mathf.Abs(denom) < 0.0001f)
    reject;

float t =
    (floorYWorld - screenRay.origin.y)
    / denom;

if (t <= 0)
    reject;

Vector3 worldPoint =
    screenRay.origin + screenRay.direction * t;

Vector3 ghostPoint =
    frame.WorldToGhost(worldPoint);

ghostPoint.y = 0;
```

This means the user only needs to aim at the floor/wall boundary.

## 8.5 Wall plane

For wall from GhostMap-space corners `A` and `B`:

```csharp
Vector3 tangent =
    (B - A).normalized;

Vector3 up = Vector3.up;

Vector3 normal =
    Vector3.Cross(up, tangent).normalized;
```

Create mathematical plane through `A`.

The sign of the normal is irrelevant for ray intersection.

## 8.6 Wall-local coordinates

For intersection point `Q`:

```csharp
float u = Vector3.Dot(Q - A, tangent);
float v = Q.y;
```

Where:

- u = horizontal distance from wall start;
- v = height above floor.

This drives:

- room height capture;
- doors;
- windows.

## 8.7 Height capture

User selects a wall.

User aims at the ceiling/wall line.

Intersect camera ray with selected wall plane.

Then:

```csharp
heightM = intersectionGhost.y;
```

Validate 2.0–4.0 m.

Fallback:

- provide manual height entry;
- user can type measured height;
- mark snapshot metadata/UI as manually entered.

A failed automatic height capture must never block the demo.

**As built:** wall selection is a `Wall N/4` readout plus a highlighted line on
the selected wall rather than a top-down preview, and a manual height is marked
on the scanner HUD only. Schema v1 has no field to carry it in the snapshot.

---

# 9. Room Validation

Implement deterministic shared validation.

## 9.1 Corner validation

For each new corner:

- finite numbers only;
- y approximately 0;
- distance to previous corner >= 0.50 m;
- distance from any existing non-neighbor corner >= 0.20 m.

After four corners:

- no self-intersection in XZ;
- absolute polygon area >= 2.0 m²;
- each wall <= 20 m;
- each internal angle must be between 35° and 145° for MVP sanity.

## 9.2 Closure verification

After four corners, app enters `VerifyClosure`.

User re-aims at first physical corner.

Capture a verification point using same floor-plane ray math.

```csharp
closureErrorM =
    Vector2.Distance(
        new Vector2(first.x, first.z),
        new Vector2(verify.x, verify.z));
```

Rules:

```text
<= 0.08 m   Excellent
<= 0.15 m   Acceptable
>  0.15 m   Reject
```

If rejected:

- show exact closure error;
- offer "Redo corners";
- do not continue to height capture.

## 9.3 Quality tracking

Track how many frames during scan report:

```text
ARSession.notTrackingReason != None
```

If a critical capture occurs while tracking is degraded:

- block capture;
- explain reason:
  - excessive motion;
  - insufficient features;
  - insufficient light;
  - relocalizing;
  - etc.

---

# 10. Scanner State Machine

Use one explicit state enum.

```csharp
public enum ScanPhase
{
    Boot,
    WaitingForTracking,
    FindFloor,
    FloorLocked,
    CaptureCorners,
    VerifyClosure,
    CaptureHeight,
    AddOpenings,
    AddObjects,
    ReadyToFinalize,
    Finalized
}
```

Only `ScanWorkflowController` changes phase.

No UI button may set phase directly.

Required transitions:

```text
Boot
 -> WaitingForTracking
 -> FindFloor
 -> FloorLocked
 -> CaptureCorners
 -> VerifyClosure
 -> CaptureHeight
 -> AddOpenings
 -> AddObjects
 -> ReadyToFinalize
 -> Finalized
```

Allowed back transitions:

```text
VerifyClosure -> CaptureCorners
CaptureHeight -> CaptureCorners
AddOpenings -> CaptureHeight
AddObjects -> AddOpenings
```

Reset starts a new AR session and new session ID.

**As built:** Reset creates a new session ID, room ID and revision 0 but keeps
the running AR session, because re-initialising ARKit costs tracking quality for
no benefit. The back transitions `AddOpenings -> CaptureHeight` and
`AddObjects -> AddOpenings` are legal in the transition table, but no scanner
control uses them yet.

---

# 11. Furniture MVP

Do not attempt visual object reconstruction. Furniture is identified, not scanned: a YOLO-n detector names it and an ARKit surface measures it (ADR-0011). The model never sets a dimension.

Supported types:

```text
bed
desk
chair
couch
table
dresser
tv
generic
```

## 11.1 Placement workflow

**Primary (task R4):** the detector proposes "Found: bed, desk, 2 chairs"; each item already has a floor position and measured size; the user taps **Add All** or removes items. The manual workflow below remains the fallback for anything the detector misses.

1. User chooses type.
2. User aims crosshair at desired object center on floor.
3. Intersect camera ray with floor plane.
4. Spawn object preview.
5. Apply default dimensions.
6. User adjusts:
   - width;
   - depth;
   - height;
   - yaw.
7. Confirm.

## 11.2 Default dimensions

Use approximate defaults only as starting points:

```text
bed      1.52 x 2.03 x 0.60
desk     1.40 x 0.70 x 0.75
chair    0.50 x 0.50 x 0.90
couch    2.10 x 0.90 x 0.85
table    1.50 x 0.90 x 0.75
dresser  1.20 x 0.50 x 0.90
tv       1.10 x 0.10 x 0.70
generic  1.00 x 1.00 x 1.00
```

All stored as:

```text
width x depth x height in meters
```

The user can correct them.

---

# 12. Viewer Rendering Rules

> **Legacy (ADR-0013):** this section describes the desktop Unity Viewer, which is frozen and retires in task `R10`. Its geometry rules (walls, openings, furniture parts) carry over to the phone-side exporter in `R6`; the browser viewer (`R8`) only displays and measures.

The viewer renders **semantic geometry**, not scanner camera imagery.

## 12.1 Floor

For four corners, generate a quad mesh.

If winding is wrong, reverse the triangle order.

Use floor collider so measurement raycasts work.

## 12.2 Ceiling

Generate same footprint at `room.heightM`.

Dollhouse mode hides ceiling renderer/collider.

## 12.3 Walls

For each consecutive corner pair:

```text
length = distance in XZ
height = room.heightM
thickness = 0.10 m
```

Walls must support rectangular openings without CSG/boolean meshes.

### Opening-safe wall segmentation

For a wall:

1. collect horizontal cut positions:
   - 0
   - wall length
   - each opening start
   - each opening end
2. collect vertical cuts:
   - 0
   - room height
   - each opening sill
   - each opening top
3. sort/unique cuts;
4. create each rectangular cell between adjacent cuts;
5. evaluate cell center;
6. if center lies inside any opening, skip;
7. otherwise render a cuboid wall segment.

This works for multiple non-overlapping doors/windows and avoids runtime mesh booleans.

## 12.4 Furniture

Do not render only anonymous boxes.

Build clean primitive-based parametric objects:

### Bed
- mattress;
- base/frame;
- headboard.

### Desk
- desktop;
- four legs.

### Chair
- seat;
- back;
- four legs.

### Couch
- base;
- back;
- left arm;
- right arm;
- seat cushions.

### Table
- top;
- four legs.

### Dresser
- cabinet body;
- visual drawer fronts.

### TV
- thin display;
- stand.

`generic` may be a box.

Every furniture root has one collider representing its full bounding box.

---

# 13. Viewer Interaction Rules

> **Legacy (ADR-0013):** this section describes the desktop Unity Viewer, which is frozen and retires in task `R10`. Its geometry rules (walls, openings, furniture parts) carry over to the phone-side exporter in `R6`; the browser viewer (`R8`) only displays and measures.

## 13.1 Camera

Implement orbit camera:

- left drag: orbit;
- right drag or middle drag: pan;
- scroll: zoom;
- `F`: frame whole room;
- `D`: dollhouse preset.

Do not let camera go below floor.

## 13.2 Selection

Click object collider.

Selected object:

- outline/highlight;
- inspector shows:
  - type;
  - position X/Z;
  - yaw;
  - width;
  - depth;
  - height.

## 13.3 Dragging

After finalization only:

- click selected furniture;
- drag on floor plane;
- update center X/Z;
- increment viewer-side revision;
- rerender object.

Do not allow moving walls/openings in MVP.

## 13.4 Resize/rotate

Inspector fields/sliders update:

- width;
- depth;
- height;
- yaw.

Validate before apply.

## 13.5 Measurement

Measurement mode:

1. click first world point via collider raycast;
2. click second world point;
3. display:
   - 3D distance;
   - horizontal XZ distance;
4. draw line between points;
5. allow clear/reset.

---

# 14. Saving and Loading

After scan finalization, viewer owns the editable copy.

Save JSON to:

```text
Application.persistentDataPath/GhostMap/Scenes/
```

Filename:

```text
<sessionId>.json
```

Save full `SceneSnapshot`.

Load:

1. parse;
2. validate schema version;
3. validate room;
4. clear current rendered scene;
5. set scene store;
6. render.

Never load invalid scene silently.

**After ADR-0013:** the phone keeps every finalized scan and its export bundle; the bundle on the computer is the saved room. The Viewer behaviour below is legacy until the Viewer is retired.

**As built:** V6 saves to one fixed slot,
`Application.persistentDataPath/ghostmap-scene.json`, and each Save overwrites
it. Save and Load are refused unless the scene is finalized and Viewer-owned
(ADR-0003). Loading goes through `ViewerEditableScene.LoadExternalSnapshot`,
which re-validates before replacing the displayed room.

---

# 15. Foundation Tasks

**Complete.** Full original task specifications: `git show f5a7d30:docs/plans/ghostmap-implementation-plan.md`.

| Task | Delivered | Commit |
| --- | --- | --- |
| F0 | Repository and collaboration scaffold | `9c28d7a` |
| F1 | Shared package and scene schema v1 | `33d63fe` |
| F2 | Geometry and validation | `23050c4` |
| F3 | Protocol v1, fixtures, tools | `db347c1` |
| F4 | Independent review of F0-F3 (one real defect fixed) | `47a6a0a` |

The foundation gate passed and is tagged `shared-v1-ready`.

---

# 16. Parallel Scanner Workstream

**Complete, every task verified on a physical iPhone.** Full original task
specifications: `git show f5a7d30:docs/plans/ghostmap-implementation-plan.md`.

| Task | Delivered | Commit | PR |
| --- | --- | --- | --- |
| S1 | Unity project and AR smoke test | `80b5a48` | #1 |
| S2 | Floor lock and Ghost coordinate frame | `b0fe6f0` | #2 |
| S3 | Four-corner capture and closure verification | `1e04d02` | #3 |
| S4 | Height capture with manual fallback | `1b8a6ce` | #4 |
| S5 | Doors, windows, furniture | `7274cfe` | #5 |
| S6 | TCP client, finalization, Reset | `f087aaa` | #6 |

Current behavior and known issues: `docs/status/scanner.md`.

---

# 17. Parallel Viewer Workstream

**Complete.** Full original task specifications: `git show f5a7d30:docs/plans/ghostmap-implementation-plan.md`.

| Task | Delivered | Commit | PR |
| --- | --- | --- | --- |
| V1 | Project, TCP server, fixture ingestion | `0cee857` | #7 |
| V2 | Floor, ceiling, walls | `197d4bd` | #8 |
| V3 | Wall openings | `79d49c0` | #9 |
| V4 | Parametric furniture and dollhouse camera | `4ed9c67` | #10 |
| V5 | Selection, editing, measurement | `660ad61` | #11 |
| V6 | Persistence and HUD | `b9fa1f5` | #12 |

Current behavior and known issues: `docs/status/viewer.md`.

---

# 18. Roadmap: Stand-in-Place GhostMap

Replaces the original integration tasks `I1`-`I4`. Direction: ADR-0012 (capture) and ADR-0013 (export, transfer, browser viewer). Record every attempt in `docs/status/integration.md`, failures included.

```text
R1 -> R2 -> R3
   \      \-> R4 -> R5
    \-> R6 -> R7
          \-> R8
R2, R7, R8 -> R9 -> R10
```

`R3`, `R4`, `R6` and `R8` can run in parallel once their inputs exist.

---

## Task R1: Bring in `GhostMapDublinHacks`

**Done 2026-10-11** (owner-approved merge of `0b7a106`). The merged code is byte-identical to the hackathon tree; the three suites still need one run in this repository. Research spikes moved to `docs/research/`. Record in `docs/status/integration.md`.

The owner's hackathon repository is this repository at `f5a7d30` plus 31 commits:

| Feature | ADR | Hackathon test result | Fate |
| --- | --- | --- | --- |
| Guided-scan UI overhaul and Simulator demo mode | — | passing | keep |
| Automatic room scan from ARKit planes (stand in place) | ADR-0012 | 27 tests passing | keep |
| Wall sweep capture | 0005 | passing | keep |
| Furniture surface detection and per-object `.glb` export | 0006 | passing | keep; exporter moves to shared in `R6` |
| Type from ARKit plane label | 0007 | passing | remove in `R4` |
| UDP quick-send discovery | 0009 | passing | superseded by ADR-0013; dormant, removed in `R10` |
| Footprint and perception research spikes | — | not production | move under `docs/research/` or drop |

Last recorded suites there: shared 202, viewer 548, scanner 577, all passing; unsigned iOS build succeeded. Nothing has run on an iPhone.

1. Merge `GhostMapDublinHacks/main` on an `integration/import-dublinhacks` branch. **The owner must approve this merge explicitly**; it brings in another contributor's code.
2. Keep its ADR numbers 0005-0009; this repository reserves them.
3. Resolve documentation conflicts in favour of this repository's current docs.
4. Regenerate both scenes, run all three suites, record the counts.

---

## Task R2: First device session

Build to the owner's iPhone, in a real furnished room:

1. Floor lock, then **Scan Room** standing in one spot. Record how many walls ARKit found and whether the room closed.
2. Repeat with the sweep fallback and the walked-corner fallback.
3. Height from the ceiling plane, then the aim and typed fallbacks.
4. Furniture surface detection on a bed, desk and table.
5. Record the scanner's frame rate during each step.

Done when every step has a recorded result in `docs/status/integration.md`, failures included.

---

## Task R3: Accuracy benchmark

Tape-measure one room: four walls, ceiling height, one door. Do five stand-in-place scans and record:

```text
scan | wall A-D error | height error | door width error | walls found automatically | fallback used
```

Targets: median wall absolute error <= 0.12 m, max wall error <= 0.20 m, height error <= 0.15 m, no self-crossing rooms. If targets fail, coach the user to stand nearer the room centre before changing thresholds. Never hide errors to pass.

---

## Task R4: On-device furniture identification (ADR-0011)

1. Export YOLO11n to Core ML with NMS (`yolo export model=yolo11n.pt format=coreml nms=True`); commit the `.mlpackage` and its SHA-256.
2. Native iOS plugin (Swift, Vision + Core ML) taking ARKit camera images about five times a second, returning class, confidence and box.
3. `IObjectDetector` C# interface with a fake for EditMode tests.
4. Pure C# pipeline: box bottom-centre ray to the floor, match to a measured ARKit surface, class-to-type map, multi-frame tracker (same class within 0.5 m; propose after 5 frames at >= 0.5 confidence).
5. UI: "Found: ..." list, **Add All**, remove per item; manual placement as fallback.
6. Remove the ARKit-label type guesser (hackathon ADR-0007) and its tests.
7. Device test: precision and recall on the `R3` room's furniture; latency per frame.

---

## Task R5: Keep the detector's label in the scene

Shared-contract change (`AGENTS.md` rule 8): add an optional `label` string to `SceneObjectModel` so a `generic` object can carry `"potted plant"`. Additive within schema v1. Update `scene-schema-v1.md`, shared tests, `inspect_snapshot.py` and the fixtures. `room.html` shows the label.

---

## Task R6: The phone builds the export bundle (ADR-0013)

1. Move `GlbExporter`, `FurnitureFactory.BuildParts` (part table and colours only) and `WallSliceGenerator` from the Viewer into `shared/com.ghostmap.shared/Runtime/Export/`, with their tests. The Viewer uses them from there until it is retired.
2. Add a whole-room `.glb` writer: floor, ceiling and wall segments, so openings are real holes.
3. On finalize, write `GhostMap-<room>-<yyyyMMdd-HHmm>.zip` with `room.html` (from the `R8` template), `room.glb`, `objects/<id>.glb`, `scene.json` and `README.txt` (how to import into Unity with glTFast).
4. Keep every finalized scan on the phone, listed on the start screen, so it can be sent again.
5. Write `docs/contracts/export-bundle-v1.md`.
6. Verify: `npx gltf-validator` on every `.glb`; open in Blender; import into a fresh Unity 6 project with glTFast (`com.unity.cloud.gltfast`) and check scale, +Y up, furniture on the floor. Measure export time on the iPhone.

---

## Task R7: Send to Computer through the share sheet (ADR-0013)

1. Native iOS plugin that presents `UIActivityViewController` with the bundle zip. Written in this repository, no new package.
2. **Send to Computer** button on the finish screen and on every saved scan.
3. Device test: AirDrop to the owner's Mac three times in a row; also Save to Files and Mail. The zip opens and `room.html` works after unzipping.

---

## Task R8: Browser viewer, `room.html` (ADR-0013)

1. three.js app in `apps/web-viewer/`: load the embedded room and objects, orbit/pan/zoom, frame room, dollhouse (hide ceiling), click an object for its name, type, label and dimensions, two-click measuring with 3D and horizontal distance, an object list.
2. Build step that inlines everything (three.js, app code, styles) into one template HTML with placeholders for the embedded data; commit the built template where the scanner can ship it.
3. No network requests; works by double-click in Safari, Chrome and Firefox from a local folder.
4. Tests: the viewer's data loading and measuring logic run under Node; the template is checked for no external URLs.
5. The viewer only displays what the phone exported. It must not reimplement any shared rule.

---

## Task R9: Hardening and the guided flow

| Case | Expected |
| --- | --- |
| Poor light or fast motion | Capture blocked with a clear tracking message |
| ARKit finds fewer than four walls | Scanner names the missing direction, offers sweep |
| Detector finds nothing | Manual placement offered |
| Share sheet cancelled or AirDrop fails | Scan kept; Send to Computer can be tapped again |
| App backgrounded mid-scan | Tracking must recover before any capture |
| Phone storage nearly full | Export fails with a plain message, scan kept |

Polish: one guided flow on the phone with no debug text (remove the S1 diagnostics block and the `S6 diag:` line), large reticle, haptics on capture, progress steps Floor → Room → Height → Openings → Furniture → Send.

Every reproducible software failure gets a regression test.

---

## Task R10: Retire the Unity Viewer

Once the section 23 acceptance test passes with `room.html`:

1. Remove `apps/viewer/`, its test suite, and the UDP discovery code from `GhostMapDublinHacks` (ADR-0009).
2. Keep protocol v1 and the scanner's TCP client only if a developer still uses live streaming; otherwise remove them with an ADR.
3. Update `tools/run_unity_tests.sh`, the README, the architecture overview and the status pages.

---

# 19. Scanner UX Script

## Start

```text
GhostMap
Turn a room into a 3D model you can use in Unity.

[Start Scan]
```

## Floor

```text
Point at a clear area of the floor.

[Lock Floor]
```

## Room

```text
Stand near the middle of the room.
Slowly turn all the way around, pointing at the walls.

Walls found: 3 of 4
Look toward the wall on your left.
```

If walls cannot be found:

```text
Some walls are hard to see.
[Trace the walls instead]
```

## Height

```text
Ceiling found: 2.58 m          (or)   Point at where a wall meets the ceiling.
[Looks right]                         [Capture]   [Type it in]
```

## Openings

```text
Found 1 door.
[Add a door or window]   [Next]
```

## Furniture

```text
Found: bed, desk, 2 chairs
[Add All]   (tap any item to remove it)
[Add something it missed]
```

## Finish

```text
Room ready: 4.02 × 3.11 m, 2.58 m high
1 door · 4 pieces of furniture

[Send to Computer]
```

## Sending

Tapping **Send to Computer** opens the iOS share sheet with
`GhostMap-Bedroom-20261011-1430.zip`. The user picks their Mac under AirDrop;
iOS shows the progress and "Sent".

## Saved scans

```text
Your rooms
Bedroom · 11 Oct, 14:30        [Send to Computer]
Office  · 9 Oct, 10:05         [Send to Computer]
```

---

# 20. Browser Viewer UX Script

On the Mac the zip arrives in Downloads; double-click unzips it.

## `room.html`

```text
Bedroom · 4.02 × 3.11 m · 2.58 m high

[Frame]  [Dollhouse]  [Measure]

Objects
  Bed      1.52 × 2.03 × 0.60 m
  Desk     1.40 × 0.70 × 0.75 m
  Chair    0.50 × 0.50 × 0.90 m
  Plant    (generic) 0.40 × 0.40 × 1.10 m
```

Click an object to highlight it and show its name and size. In Measure mode,
two clicks show the 3D and horizontal distance.

## Into Unity

`README.txt` in the bundle: add glTFast to the Unity project, then drag
`room.glb` and the `objects/` folder into the Assets window.

---

# 21. Build Reproducibility

## Scanner build

Create a documented `BuildScanner` editor method if time permits.

Required output:

- Xcode project outside repository or under ignored `Builds/`.

Before device run:

- correct signing team;
- bundle ID fixed;
- camera usage string present (local-network only while the developer TCP stream remains);
- ARKit enabled;
- physical iPhone selected.

## Browser viewer build

`apps/web-viewer/` builds into one self-contained template HTML that the scanner ships and fills in at export time (task `R8`). Rebuild and commit the template after any viewer change.

No final presentation should depend on an untested last-minute build of either the scanner or the template.

---

# 22. Definition of Done for Every Task

A task is not done because code compiles.

It is done only when:

1. relevant automated tests pass;
2. no unrelated files changed;
3. app opens without Console exceptions;
4. status file updated;
5. handoff file created;
6. commit created;
7. physical test performed if task touches AR/device/network behavior;
8. docs/contracts updated if interface changed.

---

# 23. Acceptance Test

Run from a clean clone, on the owner's iPhone and Mac, in a furnished room.

## Setup

- install the pinned Unity version and build the scanner to the iPhone;
- nothing is installed on the Mac except a browser and, for step 13, a Unity project with glTFast;
- no IP address is typed anywhere.

## Test

1. Start a scan and lock the floor.
2. Stand in one spot and turn; the four walls are found, or found after the sweep fallback.
3. Room dimensions are within the section 18 `R3` targets of a tape measure.
4. Height is captured.
5. One door is captured.
6. A bed, a desk and a chair are identified on the phone and added with one tap.
7. Finalize; the room appears in the phone's saved scans.
8. Tap **Send to Computer** and AirDrop to the Mac; iOS reports it sent.
9. Unzip and double-click `room.html`; the room shows the door and the three objects in roughly the right places, with no network connection.
10. Dollhouse mode hides the ceiling.
11. Clicking the desk shows its name and dimensions.
12. Measure bed-to-desk distance.
13. Drag `room.glb` and `objects/` into a fresh Unity project with glTFast; the room is to scale, +Y up, furniture on the floor.

Pass requires all 13.

---

# 24. Performance Targets

Scanner:

- target 30+ FPS camera experience;
- pose debug transmission <= 5 Hz;
- snapshots only on mutation;
- no per-frame JSON snapshot generation;
- no scene-state mutation from background network thread.

Export and transfer:

- export bundle written in under 2 seconds on the iPhone for a typical room;
- `room.html` under 2 MB, opening in under 2 seconds on a laptop;
- `room.glb` plus objects under 1 MB for a typical bedroom;
- no dependency on internet at any step.

Developer TCP stream (legacy):

- snapshot under 100 KB; local update visible within 500 ms.

---

# 25. Stretch Features — Strict Order

Only start after the section 23 acceptance test passes.

## Stretch 1: Better furniture library

Prettier parametric models per type, still built from measured dimensions.

## Stretch 2: More detector classes

Fine-tune the YOLO-n model on indoor furniture (desk, dresser, wardrobe, shelf) so fewer objects fall back to `generic`. New ADR, licence check.

## Stretch 3: Arbitrary convex polygon rooms

More than four walls. Requires polygon validation, triangulation, a schema version decision and new tests.

## Stretch 4: Multi-room

Requires an explicit doorway transition and a shared global frame. Tracking drift must be addressed first.

## Stretch 5: Photoreal furniture meshes off the phone

Optional and outside the phone: send furniture photos to a GPU machine running an image-to-3D model (for example TRELLIS) and swap the parametric `.glb` for the generated mesh. Needs its own ADR because it breaks "everything runs on the phone".

## Stretch 6: Spatial queries

Deterministic first: nearest object to the door, clearance between furniture, room area and volume. Natural language only on top of those.

---

# 26. Debugging Checklist

When room geometry looks wrong, diagnose in this order:

1. Is AR session tracking?
2. Was floor locked correctly?
3. Is Ghost coordinate transform correct?
4. Does scanner local snapshot contain correct points?
5. Does serialized JSON match local snapshot?
6. Does viewer receive same revision/values?
7. Does viewer SceneStore contain same values?
8. Does renderer convert model to transforms correctly?

Never start by randomly adjusting renderer code.

When Send to Computer fails:

1. did the export bundle get written (saved scans list)?
2. AirDrop on both devices, set to receive from contacts or everyone?
3. Bluetooth and Wi-Fi on (AirDrop needs both, not the same network)?
4. try Save to Files to separate export problems from AirDrop problems;
5. does the zip open on the Mac, and does `room.html` load from the unzipped folder?

When dimensions drift:

1. check closure error;
2. check tracking warnings;
3. check floor lock;
4. compare raw captured points;
5. repeat scan slower;
6. verify physical measurement.

---

# 27. Shared Contract Change Procedure

If any worker believes schema/protocol must change:

Create:

```text
docs/decisions/ADR-XXXX-<change>.md
```

Include:

```markdown
# Context
Why current contract fails.

# Decision
Exact new fields/behavior.

# Compatibility
Breaking or additive.

# Scanner impact
...

# Viewer impact
...

# Tests
...
```

If breaking:

- increment protocol or schema version;
- update both contract docs;
- add old-version rejection test;
- merge shared change before app changes.

Do not let scanner and viewer "temporarily" disagree.

---

# 28. Recommended Parallel Schedule

## Stage A — one integrator, before split

Complete:

```text
F0
F1
F2
F3
```

Then tag/mark shared baseline:

```text
shared-v1-ready
```

Both developers update local main.

## Stage B — parallel

### Developer/worker A

```text
S1
S2
S3
S4
S5
S6
```

### Developer/worker B

```text
V1
V2
V3
V4
V5
V6
```

Both use fixture/shared package and do not wait on each other.

## Stage C — stand-in-place GhostMap

```text
R1 -> R2 -> R3
   \      \-> R4 -> R5
    \-> R6 -> R7
          \-> R8
R2, R7, R8 -> R9 -> R10
```

Section 18 defines each task. Use short-lived `integration/<task>` branches.

**Progress:** Stage A complete (tag `shared-v1-ready`). Stage B complete
(PRs #1-#12). Stage C: `R1` merged; next is `R2`.

---

# 29. First Commands in Any New Work Session

Every worker should begin with:

```bash
git status
git branch --show-current
git log -5 --oneline
git fetch origin
```

Then read:

```text
AGENTS.md
docs/status/shared.md
docs/status/<workstream>.md
docs/contracts/scene-schema-v1.md
docs/contracts/protocol-v1.md
latest relevant handoff
```

Before coding, confirm the branch does not overlap another active workstream.

At end:

```bash
git status
```

Run tests, update status/handoff, commit, then push branch.

---

# 30. Recovery if Two Parallel Branches Diverge

If scanner and viewer both need a shared change:

Do not duplicate the change.

Procedure:

1. choose one dedicated shared branch;
2. move shared implementation there;
3. add shared tests;
4. merge shared branch into main;
5. scanner branch updates from main;
6. viewer branch updates from main;
7. delete duplicated app-local workaround;
8. continue.

If Unity `.meta` GUID conflicts occur:

- do not regenerate both randomly;
- keep the version from the branch that owns that asset;
- reopen Unity;
- verify references;
- commit resolution separately.

---

# 31. "For Sure" Verification Gate

No one can guarantee camera-only reconstruction without testing the exact phone and room. Before presenting GhostMap as ready, require:

- 5 stand-in-place scans of the demo room within the `R3` targets;
- 3 scans in a second room;
- 3 consecutive end-to-end runs (scan → Send to Computer → `room.html` → Unity) with no restart;
- AirDrop tested three times in a row, plus Save to Files;
- fresh-install path tested (camera and local-network permission prompts);
- `room.html` opened offline in Safari and Chrome;
- exported files imported into a fresh Unity project;
- average scan-to-computer time recorded;
- a known-good exported room on the Mac in case the live scan fails.

The backup room is for presentation continuity, not for pretending it was scanned live.

---

# 32. Demo-Day Procedure

Before judges arrive:

1. phone charged > 70%, laptop on power;
2. AirDrop on both devices and tested once;
3. phone already granted camera permission;
4. one practice scan done in the demo room;
5. room well lit, walls visible, nobody walking through;
6. Unity open with an empty scene and glTFast installed;
7. a known-good exported room on the laptop.

Demo:

1. "We are not making a mesh. We are turning a room into structured data."
2. Lock the floor.
3. Stand still and turn; show the walls appearing.
4. Show height and the door.
5. Point at the furniture; show "Found: bed, desk, chair", tap Add All.
6. Finalize, Send to Computer, AirDrop to the laptop.
7. Double-click `room.html`: orbit, dollhouse, click the desk, measure.
8. Drag the room into Unity.

Core line:

> A camera normally gives software pixels. GhostMap gives software a room it can reason about.

---

# 33. Final Architecture Summary

Moved. The architecture diagram and module summary live in
[`docs/architecture/overview.md`](../architecture/overview.md), which also maps
every runtime class to its project and folder.

---

# 34. Plan Self-Review Result

Historical; removed. See `git show f5a7d30:docs/plans/ghostmap-implementation-plan.md`.

---

# 35. Completion Standard

GhostMap is complete when a clean clone can be turned into this real demo:

> A person stands in the middle of a normal room holding a standard non-Pro iPhone and turns around once. The phone finds the walls, identifies the furniture on device, and with one button sends the room to a computer, where it opens in a browser by double-click and drops straight into Unity.

If that sequence does not work reliably, do not spend time on stretch features.

The structured geometry pipeline is still the project; the detector and the automatic scan only make capturing it easier.
