# GhostMap Implementation Plan

> **For agentic workers:** Execute this plan task-by-task. Do not skip the dependency gates or acceptance tests. Maintain the repository handoff/status files exactly as described so another worker can take over with no chat history.

**Goal:** Build a reliable GhostMap MVP that uses a standard, non-LiDAR iPhone to capture one physical room and reconstruct it as a clean, editable, machine-readable 3D scene on a laptop.

**Architecture:** Use two separate Unity projects in one repository: an iPhone **Scanner** project and a desktop **Viewer** project. Both depend on one shared local Unity package containing the scene schema, geometry math, validation, and network protocol. The scanner is authoritative while a room is being captured; after finalization, the viewer becomes authoritative for editing and saving.

**Tech Stack:** Unity 6000.3.24f1, AR Foundation 6.3.x, Apple ARKit XR Plugin 6.3.x, C#, Unity Test Framework, Unity uGUI, raw TCP over the local network, newline-delimited JSON, Git/GitHub.

**Spec:** `docs/specs/ghostmap-project-spec.md`

## Where the project is

| Stage | Tasks | State |
| --- | --- | --- |
| A — Foundation | `F0`-`F4` | Complete |
| B — Parallel workstreams | `S1`-`S6`, `V1`-`V6` | Complete; scanner verified on a physical iPhone |
| C — Integration | `I1`-`I4` | **Not started. Next task: `I1`** |

Section numbers in this plan are stable and are cited from code comments and
handoffs. Sections whose content now lives in another canonical document are
kept as short pointers rather than renumbered. The full original text of the
completed task specifications (`F0`-`F3`, `S1`-`S6`, `V1`-`V6`) is in git:
`git show f5a7d30:docs/plans/ghostmap-implementation-plan.md`.
Notes marked **As built** record where the implementation deliberately differs
from the original text.

---

# 0. Read This Before Changing Anything

This is the execution contract for the project.

The first implementation target is deliberately narrower than the long-term vision. The MVP must work reliably before any automatic object recognition, cloud depth estimation, multi-room mapping, or photorealistic reconstruction is attempted.

## MVP promise

The MVP must do this:

1. Launch GhostMap on a standard iPhone.
2. Establish stable AR world tracking.
3. Let the user lock the floor.
4. Let the user capture the four floor corners of one rectangular/near-rectangular room.
5. Verify scan quality by re-capturing the first corner and measuring closure error.
6. Let the user capture room height.
7. Let the user add rectangular doors/windows to known walls.
8. Let the user place a limited set of furniture objects with clean parametric geometry.
9. Stream the current structured room snapshot to the laptop.
10. Reconstruct the room live on the laptop.
11. Finalize the scan.
12. Let the laptop user orbit the room, remove the roof, select furniture, drag it, resize it, rotate it, measure distances, save the scene, and reload it.

## MVP does NOT promise

Do not claim or implement these as required MVP functionality:

- dense 3D scanning from a normal RGB camera;
- automatic recovery of every visible surface;
- Gaussian splatting;
- NeRF reconstruction;
- LiDAR-like depth;
- fully automatic furniture dimensions;
- arbitrary curved rooms;
- multi-floor buildings;
- automatic semantic recognition of every object;
- centimeter survey-grade accuracy;
- simultaneous editing from both devices.

The project wins by producing **structured geometry that is editable**, not by pretending an ordinary camera is a depth sensor.

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

## 1.2 No hidden reliance on AR vertical-plane detection

AR Foundation can detect horizontal and vertical planes, but GhostMap must not require a perfect detected wall plane.

The scanner will:

- use AR plane detection to find/lock the floor;
- use the camera ray plus a mathematical floor plane to capture room corners;
- generate wall planes mathematically from captured corners;
- use those generated wall planes to capture ceiling height, doors, and windows.

This is significantly more reliable than waiting for ARKit to fully detect every wall.

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
│   ├── scanner/                 iOS Unity project
│   │   └── Assets/GhostMap/Scanner/{Runtime/{AR,Bootstrap,Capture,Networking,UI,Workflow},Editor,Tests/EditMode}
│   └── viewer/                  desktop Unity project
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

Do not attempt visual object reconstruction.

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

**As built:** V6 saves to one fixed slot,
`Application.persistentDataPath/ghostmap-scene.json`, and each Save overwrites
it. Save and Load are refused unless the scene is finalized and Viewer-owned
(ADR-0003). Loading goes through `ViewerEditableScene.LoadExternalSnapshot`,
which re-validates before replacing the displayed room.

---

# 15. Foundation Tasks

**Complete.** Full original task specifications: `git show f5a7d30:docs/plans/ghostmap-implementation-plan.md`.

| Task | Delivered | Commit | Handoff |
| --- | --- | --- | --- |
| F0 | Repository and collaboration scaffold | `9c28d7a` | `2026-09-12-foundation-f0-repo-scaffold.md` |
| F1 | Shared package and scene schema v1 | `33d63fe` | `2026-09-12-foundation-f1-scene-schema.md` |
| F2 | Geometry and validation | `23050c4` | `2026-09-12-foundation-f2-geometry-validation.md` |
| F3 | Protocol v1, fixtures, tools | `db347c1` | `2026-09-12-foundation-f3-protocol-fixtures.md` |
| F4 | Independent review of F0-F3 (one real defect fixed) | `47a6a0a` | `2026-09-12-foundation-f4-foundation-review.md` |

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

# 18. Integration Tasks

Do not begin full integration until at minimum:

```text
S3 complete
V2 complete
```

Full demo integration requires:

```text
S6 complete
V6 complete
```

---

## Task I1: Real iPhone -> viewer live room

Use the actual local network.

Procedure:

1. laptop launches Viewer;
2. note laptop LAN IP;
3. iPhone launches Scanner;
4. grant camera permission;
5. grant local-network permission;
6. type laptop IP;
7. connect;
8. lock floor;
9. capture four corners;
10. verify closure;
11. capture height;
12. observe room shell appear;
13. add one object;
14. confirm live appearance;
15. finalize;
16. disconnect/reconnect once;
17. confirm viewer keeps last scene and scanner resends snapshot.

Do not move to polish until this works three consecutive times.

Record failures in:

```text
docs/status/integration.md
```

---

## Task I2: Accuracy benchmark

Create a controlled test room.

Physically measure with tape:

- wall A;
- wall B;
- wall C;
- wall D;
- ceiling height;
- one door width/height.

Perform five independent GhostMap scans.

Record table:

```text
scan
wall A error
wall B error
wall C error
wall D error
height error
door width error
closure error
```

Success target for hackathon MVP:

- median wall absolute error <= 0.12 m;
- max normal wall error <= 0.20 m;
- closure accepted only <= 0.15 m;
- height error <= 0.15 m;
- no self-crossing rooms;
- no viewer crashes.

If target fails:

1. inspect tracking-loss periods;
2. improve scan coaching;
3. shorten scan duration;
4. require user to move more slowly;
5. improve floor-lock instructions;
6. do not "fix" bad measurements by hiding errors.

---

## Task I3: Failure-mode hardening

Test intentionally:

### Poor lighting
Expected:
- capture blocked;
- user sees tracking warning.

### Fast phone motion
Expected:
- capture blocked while tracking degraded.

### Network denied
Expected:
- scanner still works locally;
- clear connection error;
- user can retry after permission change.

### Laptop server not running
Expected:
- retry loop;
- no scanner crash.

### Wi-Fi disconnect
Expected:
- scanner retains scene;
- viewer retains last scene;
- reconnect resends full snapshot.

### Invalid opening
Expected:
- rejected before snapshot mutation.

### Bad closure
Expected:
- room cannot progress to height.

### App background/foreground
Expected:
- if AR relocalizes poorly, require tracking recovery before capture.

### Viewer receives stale revision
Expected:
- ignored.

### Viewer receives malformed JSON
Expected:
- log/reject;
- keep current scene.

Every reproducible software failure receives a regression test when possible.

---

## Task I4: Demo polish

Do this only after reliability.

Scanner polish:

- large center reticle;
- one primary action button;
- explicit instructions;
- progress:
  - Floor
  - Corners
  - Height
  - Openings
  - Objects
  - Finish
- haptics on successful capture if easy;
- green/yellow/red quality status;
- no debug spam visible in demo mode.

Viewer polish:

- neutral professional environment;
- grid optional;
- soft lighting;
- readable labels;
- smooth orbit;
- immediate dollhouse transition;
- selected object obvious;
- room hierarchy panel if time permits.

Demo should take under 2–3 minutes from start to finished room.

---

# 19. Scanner UX Script

The scanner should coach the user exactly.

## Start

```text
GhostMap
Turn a room into an editable 3D map.

[Start Scan]
```

## Waiting

```text
Move your phone slowly so tracking can initialize.
```

## Floor

```text
Point at a clear area of the floor.

[Lock Floor]
```

## Corners

```text
Aim at the floor where two walls meet.

Corner 1 of 4
[Capture Corner]
```

After capture:

```text
Corner captured.
Move clockwise around the room.
```

## Verify

```text
Return to Corner 1 and aim at it again.
This checks scan drift.

[Verify]
```

Results:

```text
Excellent — 5 cm closure error
```

or:

```text
Tracking drift is too high — 22 cm.
Redo the corners for a reliable map.

[Redo Corners]
```

## Height

```text
Select a wall, then aim at where that wall meets the ceiling.

[Capture Height]

Can't capture it?
[Enter Height Manually]
```

## Details

```text
Add room details

[Door]
[Window]
[Furniture]
[Finish]
```

## Finish

```text
Room ready.

Closure error: 7 cm
Objects: 4
Openings: 2

[Finalize GhostMap]
```

---

# 20. Viewer UX Script

## Before connection

```text
GhostMap Viewer

Listening on:
192.168.x.x : 47831

Waiting for scanner...
```

## During scan

```text
LIVE SCAN
Corners: 3 / 4
Tracking: Good
Revision: 5
```

Objects appear as snapshots arrive.

## Finalized

```text
SCAN COMPLETE

[ Dollhouse ]
[ Measure ]
[ Save ]
```

Object click opens:

```text
Desk

Position X
Position Z
Rotation
Width
Depth
Height
```

---

# 21. Build Reproducibility

## Scanner build

Create a documented `BuildScanner` editor method if time permits.

Required output:

- Xcode project outside repository or under ignored `Builds/`.

Before device run:

- correct signing team;
- bundle ID fixed;
- camera/local-network usage strings present;
- ARKit enabled;
- physical iPhone selected.

## Viewer build

Hackathon may run Viewer directly in Unity Editor for faster iteration.

Still verify macOS standalone build once before demo day.

No final presentation should depend on an untested last-minute standalone build.

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

# 23. MVP Acceptance Test

Run this from a clean clone.

## Setup

- install pinned Unity version;
- open scanner project;
- open viewer project;
- packages resolve with shared relative package;
- no manual source copying.

## Test

1. Build Scanner to standard iPhone.
2. Launch Viewer on laptop.
3. Connect over same Wi-Fi.
4. Start scan.
5. Lock floor.
6. Capture four corners.
7. Verify closure <= 0.15 m.
8. Capture room height.
9. Add one door.
10. Add bed.
11. Add desk.
12. Add chair.
13. Observe every update on Viewer.
14. Finalize.
15. Enter dollhouse mode.
16. Select desk.
17. Drag desk.
18. Rotate desk.
19. Resize desk.
20. Measure bed-to-desk distance.
21. Save.
22. Close Viewer.
23. Reopen Viewer.
24. Load saved scene.
25. Confirm same semantic room appears.

Pass requires all 25.

---

# 24. Performance Targets

Scanner:

- target 30+ FPS camera experience;
- pose debug transmission <= 5 Hz;
- snapshots only on mutation;
- no per-frame JSON snapshot generation;
- no scene-state mutation from background network thread.

Viewer:

- room rebuild < 100 ms for typical MVP scene;
- < 100 rendered primitive child objects for normal bedroom target;
- no garbage-heavy rebuild every frame;
- only rebuild on snapshot/edit.

Network:

- snapshot under 100 KB target;
- local update visible within 500 ms target;
- no dependency on internet.

---

# 25. Stretch Features — Strict Order

Only start after full MVP acceptance test passes.

## Stretch 1: Better furniture library

More parametric categories and prettier models.

## Stretch 2: Automatic object suggestion

Use camera inference only to **suggest type**.

User still confirms placement/dimensions.

Do not let model output directly mutate room without confirmation.

## Stretch 3: Arbitrary convex polygon rooms

Change 4-corner constraint to N corners.

Requires:

- robust polygon validation;
- triangulation;
- new tests.

## Stretch 4: Multi-room

Requires explicit doorway transition and shared global frame.

Do not simply keep capturing indefinitely; tracking drift must be addressed.

## Stretch 5: Cloud monocular depth

Treat as enhancement layer, not source of truth.

Never replace structured capture with uncertain dense geometry.

## Stretch 6: Spatial queries

Because scene is structured, add deterministic queries first:

```text
nearest object to door
distance between objects
room area
room volume
clearance between furniture
```

Only then add natural-language translation on top.

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

When networking fails:

1. viewer listening?
2. correct laptop LAN IP?
3. same Wi-Fi?
4. local-network permission granted?
5. TCP port reachable?
6. scanner retrying?
7. line terminated with `\n`?
8. protocol version correct?

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

## Stage C — together

```text
I1
I2
I3
I4
```

During Stage C, use short-lived integration branches only.

**Progress:** Stage A complete (tag `shared-v1-ready`). Stage B complete
(PRs #1-#12). Stage C not started.

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

No one can honestly guarantee camera-only spatial reconstruction without testing the exact phone/environment.

Therefore GhostMap uses measurable gates instead of pretending certainty.

Before presenting the project as ready, require:

- 5 successful scans of the intended demo room;
- 3 successful scans in a second room;
- 3 consecutive end-to-end live demos with no restart;
- network disconnect recovery tested;
- local-network permission fresh-install path tested;
- closure rejection tested;
- save/load tested;
- average demo completion time recorded;
- backup fixture/demo scene available in Viewer if hardware/network fails.

The backup fixture is for presentation continuity, not for pretending it was scanned live.

---

# 32. Demo-Day Procedure

Before judges arrive:

1. phone charged > 70%;
2. laptop connected to power;
3. same stable Wi-Fi;
4. Viewer already launched;
5. laptop LAN IP confirmed;
6. scanner already granted camera/local-network permissions;
7. one practice scan completed;
8. demo area well lit;
9. room corners visible;
10. no people walking through capture line;
11. saved known-good room present;
12. screen recording disabled if it hurts performance.

Demo:

1. explain: "We are not generating a mesh. We are converting space into structured data."
2. start fresh room.
3. lock floor.
4. capture corners.
5. show closure quality.
6. capture height.
7. add one door and 2–3 objects.
8. show laptop updating live.
9. finalize.
10. hit Dollhouse.
11. move desk.
12. measure path/clearance.
13. show semantic hierarchy/data.

Core judge line:

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

GhostMap MVP is complete only when a clean clone can be turned into the following real demo:

> A person with a standard non-Pro iPhone scans a normal bedroom using guided geometric capture. A laptop receives the structured room live, renders a recognizable 3D digital twin with door/furniture, and then allows the room to be viewed in dollhouse mode, edited, measured, saved, and reloaded.

If that sequence does not work reliably, do not spend time on AI, photorealism, multi-room mapping, or additional features.

Build the boring, reliable geometry pipeline first.

That pipeline is the project.
