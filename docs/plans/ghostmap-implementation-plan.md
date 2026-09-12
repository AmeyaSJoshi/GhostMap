# GhostMap Implementation Plan

> **For agentic workers:** Execute this plan task-by-task. Do not skip the dependency gates or acceptance tests. Maintain the repository handoff/status files exactly as described so another worker can take over with no chat history.

**Goal:** Build a reliable GhostMap MVP that uses a standard, non-LiDAR iPhone to capture one physical room and reconstruct it as a clean, editable, machine-readable 3D scene on a laptop.

**Architecture:** Use two separate Unity projects in one repository: an iPhone **Scanner** project and a desktop **Viewer** project. Both depend on one shared local Unity package containing the scene schema, geometry math, validation, and network protocol. The scanner is authoritative while a room is being captured; after finalization, the viewer becomes authoritative for editing and saving.

**Tech Stack:** Unity 6000.3.24f1, AR Foundation 6.3.x, Apple ARKit XR Plugin 6.3.x, C#, Unity Test Framework, Unity uGUI, raw TCP over the local network, newline-delimited JSON, Git/GitHub.

**Spec:** `docs/specs/ghostmap-project-spec.md`

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

Create exactly this high-level structure:

```text
GhostMap/
├── AGENTS.md
├── README.md
├── .editorconfig
├── .gitignore
├── .gitattributes
│
├── docs/
│   ├── specs/
│   │   └── ghostmap-project-spec.md
│   ├── plans/
│   │   └── ghostmap-implementation-plan.md
│   ├── architecture/
│   │   └── overview.md
│   ├── contracts/
│   │   ├── protocol-v1.md
│   │   └── scene-schema-v1.md
│   ├── decisions/
│   │   ├── ADR-0001-two-unity-projects.md
│   │   ├── ADR-0002-snapshot-protocol.md
│   │   ├── ADR-0003-scanner-authority.md
│   │   └── ADR-0004-no-dense-depth-in-mvp.md
│   ├── status/
│   │   ├── integration.md
│   │   ├── scanner.md
│   │   ├── shared.md
│   │   └── viewer.md
│   └── handoffs/
│       └── README.md
│
├── shared/
│   └── com.ghostmap.shared/
│       ├── package.json
│       ├── Runtime/
│       │   ├── GhostMap.Shared.asmdef
│       │   ├── Domain/
│       │   │   ├── Vec3Dto.cs
│       │   │   ├── ValidationResult.cs
│       │   │   ├── WallDefinition.cs
│       │   │   ├── CornerModel.cs
│       │   │   ├── OpeningModel.cs
│       │   │   ├── SceneObjectModel.cs
│       │   │   ├── RoomModel.cs
│       │   │   └── SceneSnapshot.cs
│       │   ├── Geometry/
│       │   │   ├── GhostCoordinateFrame.cs
│       │   │   ├── RayPlaneMath.cs
│       │   │   ├── RoomGeometry.cs
│       │   │   ├── WallGeometry.cs
│       │   │   └── MeasurementMath.cs
│       │   ├── Protocol/
│       │   │   ├── ProtocolConstants.cs
│       │   │   ├── WireMessages.cs
│       │   │   └── ProtocolSerializer.cs
│       │   └── Validation/
│       │       ├── RoomValidator.cs
│       │       ├── OpeningValidator.cs
│       │       └── FurnitureValidator.cs
│       └── Tests/
│           └── Editor/
│               ├── GhostMap.Shared.Tests.asmdef
│               ├── CoordinateFrameTests.cs
│               ├── RayPlaneMathTests.cs
│               ├── RoomGeometryTests.cs
│               ├── OpeningValidatorTests.cs
│               └── ProtocolSerializerTests.cs
│
├── apps/
│   ├── scanner/
│   │   ├── Assets/
│   │   │   └── GhostMap/
│   │   │       └── Scanner/
│   │   │           ├── Runtime/
│   │   │           │   ├── Bootstrap/
│   │   │           │   ├── AR/
│   │   │           │   ├── Capture/
│   │   │           │   ├── Networking/
│   │   │           │   ├── UI/
│   │   │           │   └── Workflow/
│   │   │           ├── Editor/
│   │   │           └── Tests/
│   │   │               └── EditMode/
│   │   ├── Packages/
│   │   └── ProjectSettings/
│   │
│   └── viewer/
│       ├── Assets/
│       │   └── GhostMap/
│       │       └── Viewer/
│       │           ├── Runtime/
│       │           │   ├── Bootstrap/
│       │           │   ├── Networking/
│       │           │   ├── Scene/
│       │           │   ├── Rendering/
│       │           │   ├── Interaction/
│       │           │   ├── Persistence/
│       │           │   └── UI/
│       │           └── Tests/
│       │               └── EditMode/
│       ├── Packages/
│       └── ProjectSettings/
│
├── fixtures/
│   ├── valid-room-v1.json
│   ├── room-with-door-window-v1.json
│   └── malformed-room-v1.json
│
└── tools/
    ├── send_fixture.py
    └── inspect_snapshot.py
```

The scanner and viewer manifests reference the shared package from their own `Packages/manifest.json` files with:

```json
"com.ghostmap.shared": "file:../../../shared/com.ghostmap.shared"
```

The path is relative to each project's `Packages` folder.

Do not duplicate shared models inside either app.

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

Create `AGENTS.md` with the following instructions:

```markdown
# GhostMap Repository Instructions

1. Read the project spec, implementation plan, shared contract docs, and your workstream status before modifying code.
2. Do not invent alternate scene schemas or network messages inside an app.
3. `shared/com.ghostmap.shared` is the only source of truth for domain models, geometry utilities, validation, and wire contracts.
4. During scanning, the scanner owns scene state. After finalization, the viewer owns editable scene state.
5. Protocol v1 sends full scene snapshots after structural changes. Do not replace it with delta/event replay without an ADR and contract tests.
6. Do not add LiDAR, Gaussian splatting, NeRFs, cloud inference, or automatic object recognition to the MVP unless all MVP acceptance tests already pass.
7. Do not edit another workstream's directory unless the task explicitly requires integration.
8. Shared contract changes require:
   - contract documentation update,
   - tests,
   - dedicated commit,
   - status/handoff update.
9. Never modify generated Unity folders (`Library`, `Temp`, `Logs`, `obj`, build output).
10. Do not upgrade Unity or package versions without a dedicated dependency-change PR.
11. Every bug fix must include a regression test when the bug is testable without a physical device.
12. Real-device behavior must never be declared fixed until verified on a real iPhone.
13. At the end of every meaningful task:
   - run tests,
   - update the workstream status,
   - create a handoff file,
   - commit.
```

---

# 6. Shared Scene Schema v1

The schema must remain boring and explicit.

All distances are **meters**.

Coordinate system:

- +Y = up;
- floor = Y 0 after normalization;
- +Z = forward from the floor-lock moment;
- +X = right from the floor-lock moment.

## 6.1 Vec3Dto

```csharp
[Serializable]
public struct Vec3Dto
{
    public float x;
    public float y;
    public float z;

    public Vec3Dto(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public Vector3 ToVector3() => new Vector3(x, y, z);

    public static Vec3Dto FromVector3(Vector3 value)
        => new Vec3Dto(value.x, value.y, value.z);
}
```

## 6.2 CornerModel

```csharp
[Serializable]
public sealed class CornerModel
{
    public string id;
    public Vec3Dto position;
}
```

Rules:

- ordered clockwise or counter-clockwise;
- order must not change after finalization;
- Y must be 0 within tolerance.

## 6.3 OpeningModel

```csharp
[Serializable]
public sealed class OpeningModel
{
    public string id;
    public string type; // "door" or "window"

    public string wallStartCornerId;
    public string wallEndCornerId;

    public float offsetM;
    public float widthM;
    public float sillHeightM;
    public float heightM;
}
```

Definition:

- `offsetM` is distance along the wall from start corner;
- door normally has `sillHeightM = 0`;
- window has positive sill;
- opening must fit completely inside the wall.

## 6.4 SceneObjectModel

```csharp
[Serializable]
public sealed class SceneObjectModel
{
    public string id;
    public string type; // bed, desk, chair, couch, table, dresser, tv, generic

    public Vec3Dto center;

    public float yawDeg;

    public float widthM;
    public float depthM;
    public float heightM;
}
```

MVP furniture remains axis-aligned to its own yaw around +Y.

## 6.5 RoomModel

```csharp
[Serializable]
public sealed class RoomModel
{
    public string id;
    public string name;

    public float heightM;

    public CornerModel[] corners;
    public OpeningModel[] openings;
    public SceneObjectModel[] objects;
}
```

Do **not** serialize walls separately.

Walls are derived from consecutive corners. This prevents duplicated state from disagreeing.

For four corners:

```text
wall 0 = corner 0 -> corner 1
wall 1 = corner 1 -> corner 2
wall 2 = corner 2 -> corner 3
wall 3 = corner 3 -> corner 0
```

## 6.6 SceneSnapshot

```csharp
[Serializable]
public sealed class SceneSnapshot
{
    public int schemaVersion;
    public string sessionId;
    public int revision;
    public string scanPhase;
    public bool finalized;
    public float closureErrorM;
    public RoomModel room;
}
```

Rules:

- `schemaVersion = 1`;
- `revision` increments for every structural mutation;
- viewer ignores a snapshot if its revision is lower than the latest accepted revision for the same session;
- finalized snapshot cannot be mutated by scanner-side UI.

---

# 7. Network Protocol v1

Use TCP, port:

```text
47831
```

Transport:

```text
TCP + UTF-8 + one JSON object per line
```

Every serialized message ends with:

```text
\n
```

Maximum accepted line length:

```text
262144 bytes
```

If exceeded, reject the connection/message.

## 7.1 Message header

```csharp
[Serializable]
public class WireMessageHeader
{
    public int protocolVersion;
    public string type;
    public string sessionId;
    public long sequence;
    public long unixTimeMs;
}
```

`protocolVersion = 1`.

## 7.2 Required message types

Only these are required for MVP:

```text
hello
heartbeat
phone.pose
scene.snapshot
scan.finalized
```

### hello

```csharp
[Serializable]
public sealed class HelloMessage : WireMessageHeader
{
    public string appVersion;
    public string deviceName;
}
```

### heartbeat

```csharp
[Serializable]
public sealed class HeartbeatMessage : WireMessageHeader
{
}
```

### phone.pose

Debug/display only.

```csharp
[Serializable]
public sealed class PhonePoseMessage : WireMessageHeader
{
    public Vec3Dto position;
    public float yawDeg;
    public string trackingState;
    public string notTrackingReason;
}
```

Send at no more than 5 Hz.

Do not build the room from these poses.

### scene.snapshot

```csharp
[Serializable]
public sealed class SceneSnapshotMessage : WireMessageHeader
{
    public SceneSnapshot snapshot;
}
```

Send immediately after:

- floor lock;
- corner add/remove;
- room height update;
- opening add/remove/edit;
- furniture add/remove/edit;
- scan finalization.

### scan.finalized

```csharp
[Serializable]
public sealed class ScanFinalizedMessage : WireMessageHeader
{
    public int finalRevision;
}
```

The scanner must also send the final snapshot immediately before this message.

## 7.3 Serialization

Use `JsonUtility`.

Deserialization algorithm:

1. parse JSON into `WireMessageHeader`;
2. validate `protocolVersion`;
3. switch on `type`;
4. parse again into the concrete message class;
5. validate fields;
6. dispatch.

Signature:

```csharp
public static class ProtocolSerializer
{
    public static string Serialize(object message);

    public static bool TryDeserialize(
        string json,
        out object message,
        out string error);
}
```

Do not use reflection-heavy polymorphic serialization.

## 7.4 Reconnection

Scanner behavior:

1. keep latest `SceneSnapshot` in memory;
2. if connection drops, show disconnected state;
3. retry every 2 seconds while app is foregrounded;
4. after reconnect:
   - send hello;
   - send current scene snapshot immediately;
   - resume heartbeats.

Viewer behavior:

- accept one active scanner connection;
- when a new connection arrives, replace old disconnected session;
- never erase displayed scene simply because network disconnects;
- show "Disconnected — displaying last snapshot."

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

```text
STANDARD IPHONE
┌───────────────────────────────┐
│ AR Foundation / ARKit         │
│                               │
│ camera pose                   │
│ floor detection               │
│ screen rays                   │
│                               │
│ Assisted Capture              │
│ floor                         │
│ 4 corners                     │
│ closure verification          │
│ height                        │
│ openings                      │
│ furniture                     │
│                               │
│ SceneSnapshot rev N           │
└───────────────┬───────────────┘
                │
                │ TCP / NDJSON
                │ full snapshots
                ▼
LAPTOP VIEWER
┌───────────────────────────────┐
│ Protocol Receiver             │
│        ↓                      │
│ SceneStore                    │
│        ↓                      │
│ Validator                     │
│        ↓                      │
│ Semantic Renderer             │
│                               │
│ floor / ceiling               │
│ segmented walls               │
│ doors / windows               │
│ parametric furniture          │
│        ↓                      │
│ Edit / Measure / Save         │
└───────────────────────────────┘

SHARED PACKAGE
┌───────────────────────────────┐
│ schema                        │
│ protocol                      │
│ coordinate transforms         │
│ ray-plane math                │
│ room geometry                 │
│ validation                    │
│ measurement math              │
└───────────────────────────────┘
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

---

# 35. Completion Standard

GhostMap MVP is complete only when a clean clone can be turned into the following real demo:

> A person with a standard non-Pro iPhone scans a normal bedroom using guided geometric capture. A laptop receives the structured room live, renders a recognizable 3D digital twin with door/furniture, and then allows the room to be viewed in dollhouse mode, edited, measured, saved, and reloaded.

If that sequence does not work reliably, do not spend time on AI, photorealism, multi-room mapping, or additional features.

Build the boring, reliable geometry pipeline first.

That pipeline is the project.
