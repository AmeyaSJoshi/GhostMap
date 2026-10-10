# GhostMap Project Specification

> A camera normally gives software pixels. GhostMap gives software a room it can reason about.

This is the **product** specification: what GhostMap is for and what it must do.
How it is built is in [`docs/architecture/overview.md`](../architecture/overview.md),
the wire and data formats are in [`docs/contracts/`](../contracts/), and the task
breakdown is in [`docs/plans/ghostmap-implementation-plan.md`](../plans/ghostmap-implementation-plan.md).

Revised 2026-10-10. The original spec also contained early UML, CRC cards, a
class list and an event-message list (`CORNER_ADDED`, `OBJECT_UPDATED`, ... over
WebSocket). Those were superseded before implementation by ADR-0001 to ADR-0004
and have been removed from this document; they remain in git history.

---

## 1. Overview

| | |
| --- | --- |
| **Product** | GhostMap |
| **Type** | Mobile spatial-capture app plus desktop 3D viewer and editor |
| **Hardware** | A standard, non-Pro (non-LiDAR) iPhone and a computer running the Unity viewer |
| **Goal** | Turn one real room into a structured, editable 3D model using only an ordinary iPhone |
| **Pitch** | GhostMap turns a physical room into an editable, machine-readable 3D world using only your phone. |

GhostMap does not produce a photorealistic scan or a mesh. It represents the room
as meaningful objects: floor, ceiling, walls, doors, windows and furniture. Each
object carries structured data (position, rotation, width, depth, height, type,
and which wall it belongs to) that people and programs can edit, measure, save
and reload.

## 2. Problem

Cameras capture images but do not understand the structure of a room. Existing
room-scanning apps usually need LiDAR, produce meshes that are hard to edit,
focus on interior design, and do not expose the room as structured data.

GhostMap builds a lightweight digital twin from ARKit world tracking, a single
floor-plane detection, user-assisted aiming, and procedural reconstruction.

## 3. Primary user workflow

```text
Scan the room -> Finalize -> Send to Computer -> the room appears in the GhostMap Viewer
```

GhostMap finds the user's computer on the local network by itself. A normal
user never types an IP address or port. After a first-use pairing step the
computer is remembered. No cloud service, account or internet connection is
involved. See ADR-0007.

## 4. Target users

Students, developers, architects, interior designers, robotics and accessibility
researchers, emergency-response teams, AR/VR developers, and anyone building
digital twins.

## 5. User stories

- As a user, I want to scan a room with my normal iPhone.
- As a user, I want to mark room corners so the system knows the room's shape.
- As a user, I want walls generated automatically from those corners.
- As a user, I want to add doors, windows and furniture.
- As a user, I want to send the finished room to my computer with one button.
- As a user, I want to select, move, resize and rotate objects.
- As a user, I want to measure distances.
- As a user, I want to remove the ceiling and see a dollhouse view.
- As a user, I want to save and reopen the reconstructed room.
- As a developer, I want structured spatial data instead of only a mesh.

## 6. MVP scope

One room, four ordered floor corners, flat floor, flat ceiling (2.0 to 4.0 m),
rectangular non-overlapping openings, and parametric furniture in eight types:
bed, desk, chair, couch, table, dresser, tv, generic.

**MVP example.** The user scans a bedroom, records four corners and the ceiling
height, and GhostMap generates four walls, a floor and a ceiling. The user adds a
door, a bed, a desk and a chair. The computer shows the same room. The user
removes the ceiling, moves the desk, and measures between the desk and the bed.

**Out of scope until the MVP acceptance test passes** (ADR-0004, `AGENTS.md`
rule 6): automatic furniture recognition or dimensions, monocular depth, LiDAR,
Gaussian splatting, NeRF, photorealistic reconstruction or texturing,
multi-room or whole-building capture, curved rooms, people tracking, AI spatial
queries, simultaneous editing from two devices.

## 7. Functional requirements

Status as of 2026-10-10. "Built" means implemented and covered by automated
tests; scanner items were also verified on a physical iPhone. Nothing has yet
been verified end to end from iPhone to viewer (Integration `I1`).

| ID | Requirement | Status |
| --- | --- | --- |
| FR-01 | **Start scan.** Create a scan session; ARKit establishes world tracking. | Built (S1, S2) |
| FR-02 | **Device tracking.** Track the phone's position and orientation; block captures while tracking is degraded. | Built. Pose is not streamed to the viewer (`phone.pose` unused) |
| FR-03 | **Floor detection.** Detect a horizontal floor plane and lock it as the vertical reference. | Built (S2) |
| FR-04 | **Corner marking.** Aim a crosshair at a corner and capture it as a 3D floor point. | Built (S3) |
| FR-05 | **Room footprint.** Connect the corners into a validated floor polygon and check scan drift by re-aiming at the first corner. | Built (S3), exactly four corners |
| FR-06 | **Room height.** Aim at a wall/ceiling junction, or type the height. | Built (S4) |
| FR-07 | **Walls.** One wall between each pair of neighbouring corners. | Built. Walls are derived, never stored |
| FR-08 | **Ceiling.** Generated from the footprint and height. | Built (V2) |
| FR-09 | **Doors.** Pick a wall, mark two opposite corners; store width, height, offset and parent wall. | Built (S5, V3) |
| FR-10 | **Windows.** As doors, plus height above the floor. | Built (S5, V3) |
| FR-11 | **Furniture.** Choose a category, place it on the floor, adjust its size and yaw. | Built (S5). Uses per-type default dimensions the user adjusts; the user does not mark object boundaries |
| FR-12 | **Live preview (optional).** The computer shows the room updating while the phone scans. Not required to use GhostMap. | Built (S6, V1), but needs the computer's IP typed in. Untested end to end |
| FR-13 | **Object editing.** Select, move, rotate, resize; delete and hide. | Select, move, rotate, resize built (V5). Delete and hide not built |
| FR-14 | **Measurement.** Pick two points and show the distance. | Built (V5): 3D and horizontal distance between clicked surface points |
| FR-15 | **Dollhouse mode.** Hide the ceiling and orbit from above. | Built (V4) |
| FR-16 | **Save scene.** | Built (V6), single save slot |
| FR-17 | **Load scene.** | Built (V6) |
| FR-18 | **Export** as JSON, glTF/GLB or Unity scene data. | Not built. The saved JSON snapshot is the only format |
| FR-19 | **Send to Computer.** After finalizing, one button finds the user's computer and delivers the complete room; no IP or port entry; the computer is remembered; clear success or plain-language retry. | Designed (ADR-0007), not built. Integration `I1B` |
| FR-20 | **Remove walls and view a floor plan.** | Not built |

## 8. Non-functional requirements

| Area | Requirement |
| --- | --- |
| Accuracy | Room dimensions within about 5 to 15 cm in controlled indoor tests. Measured gates: closure ≤ 0.15 m, median wall error ≤ 0.12 m, height error ≤ 0.15 m (Integration `I2`) |
| Performance | Viewer reflects a change within about one second on a local network (plan target 500 ms) |
| Usability | A first-time user can scan a simple bedroom without technical knowledge |
| Compatibility | Any ARKit-capable non-Pro iPhone |
| Reliability | Losing the network never destroys scan data on either side |
| Modularity | Capture, networking, reconstruction and editing are separate modules |
| Privacy | No facial recognition, no identity data, no cloud dependency |

## 9. Assumptions

The room is rectangular or near-rectangular with vertical walls and a
horizontal floor. The user moves slowly, the room has enough visual texture for
ARKit, the phone and computer share a local network, and the user corrects
mistakes by hand.

## 10. Data model

The scene is one `SceneSnapshot` holding one `RoomModel`:

```text
Room
├── heightM
├── corners[4]     ordered floor points, Y = 0     (walls derived from these)
├── openings[]     door | window, on wall (startCornerId -> endCornerId)
│                  offsetM, widthM, sillHeightM, heightM
└── objects[]      type, center, yawDeg, widthM, depthM, heightM
```

Exact field definitions, units and rules: [`docs/contracts/scene-schema-v1.md`](../contracts/scene-schema-v1.md).

## 11. MVP acceptance criteria

The MVP is complete when, without LiDAR:

- a non-Pro iPhone starts an AR scan;
- the user marks four room corners and the footprint is recognisable;
- the user sets the room height;
- the viewer generates floor, walls and ceiling;
- the user adds at least one door and three furniture objects;
- objects appear approximately where they are in reality;
- the user can remove the ceiling, select, move and resize an object, and
  measure between two objects;
- the scene can be saved.

The step-by-step acceptance test is plan section 23.

## 12. Demo sequence

Start with an empty viewer. Open GhostMap on the iPhone, lock the floor, mark
four corners and the height, and watch the room appear on the computer. Add a
door, a bed, a desk and a chair. Finish scanning, switch to dollhouse mode,
rotate the room, click the desk, move it, measure the space between desk and
bed, and show the structured scene data. Close with:

> A camera sees pixels. GhostMap understands spaces.

The demo-day checklist is plan section 32.

## 13. Vision

```text
Physical environment
        ↓
   Normal iPhone
        ↓
Spatial understanding
        ↓
  Structured scene
        ↓
┌───────┼─────────┐
↓       ↓         ↓
Humans  Robots    Software
↓       ↓         ↓
AR/VR   Navigation Simulation
```

GhostMap is not just a room scanner. It converts the physical world into
structured spatial information that people, programs, robots and simulations
can use. Spatial queries, pathfinding, object suggestion, multi-room capture
and export are the natural next layers; their order is fixed by plan section 25.
