# GhostMap Project Specification

> A camera normally gives software pixels. GhostMap gives software a room it can reason about.

This is the **product** specification: what GhostMap is for and what it must do.
How it is built is in [`docs/architecture/overview.md`](../architecture/overview.md),
the wire and data formats are in [`docs/contracts/`](../contracts/), the
direction is set by [ADR-0012](../decisions/ADR-0012-stand-in-place-on-device-capture.md),
and the task order is in [`docs/plans/ghostmap-implementation-plan.md`](../plans/ghostmap-implementation-plan.md).

---

## 1. Overview

| | |
| --- | --- |
| **Product** | GhostMap |
| **Type** | iPhone capture app plus a desktop 3D viewer, editor and exporter |
| **Hardware** | A standard, non-Pro (non-LiDAR) iPhone and a computer running the Unity viewer (macOS today) |
| **Goal** | Stand in one spot, point the phone around a room, and get an editable 3D model of the room and its furniture that can be used in Unity |
| **Pitch** | GhostMap turns a physical room into an editable, machine-readable 3D world using only your phone. |

GhostMap does not produce a photorealistic scan or a mesh. It represents the room
as meaningful objects: floor, ceiling, walls, doors, windows and furniture. Each
object carries structured data (position, rotation, width, depth, height, type)
that people and programs can edit, measure, save, reload and export.

## 2. Problem

Cameras capture images but do not understand the structure of a room. Existing
room-scanning apps usually need LiDAR, produce meshes that are hard to edit,
focus on interior design, and do not expose the room as structured data.

GhostMap builds a lightweight digital twin on an ordinary iPhone from ARKit
tracking and plane detection, a small on-device object detector, and its own
geometry, then hands it to the user's computer as Unity-ready files.

## 3. Primary user workflow

```text
Lock the floor
  -> stand in one spot and turn          walls, size and height found
  -> point at the furniture              phone identifies it, one tap to add
  -> Finalize
  -> Send to Computer                    no IP address, no setup
  -> the room appears in the GhostMap Viewer
  -> Export for Unity                    scene.json, room.glb, one .glb per object
```

Everything up to Send to Computer runs on the phone. No cloud service, account
or internet connection is involved. When automatic detection misses something,
the user can always fall back to aiming at walls, corners and furniture by hand.

## 4. Target users

Students, developers, architects, interior designers, robotics and accessibility
researchers, emergency-response teams, AR/VR and game developers, and anyone
building digital twins.

## 5. User stories

- As a user, I want to stand in one place and point my phone around the room to capture it.
- As a user, I want GhostMap to work out the walls and room size without walking to every corner.
- As a user, I want the phone to recognise my furniture so I do not have to name each piece.
- As a user, I want to add doors and windows.
- As a user, I want to send the finished room to my computer with one button.
- As a user, I want to select, move, resize and rotate objects, and measure distances.
- As a user, I want to remove the ceiling and see a dollhouse view.
- As a user, I want to save and reopen a room.
- As a Unity developer, I want the room and each piece of furniture as 3D files I can drop into my own project.
- As a developer, I want structured spatial data instead of only a mesh.

## 6. Scope

**In scope:** one room with four walls, flat floor and ceiling (2.0 to 4.0 m),
rectangular non-overlapping openings, and furniture identified by the on-device
detector or placed by hand. GhostMap types: bed, desk, chair, couch, table,
dresser, tv, generic.

**Out of scope** (ADR-0012, `AGENTS.md` rule 6): LiDAR, dense depth
reconstruction, Gaussian splatting, NeRF, generative 3D models such as TRELLIS,
any cloud processing, photoreal or textured meshes, curved rooms, multi-room or
whole-building capture, people tracking, simultaneous editing from two devices.

## 7. Functional requirements

"Built" means implemented and covered by automated tests. "Hackathon" means it
exists in `GhostMapDublinHacks` and arrives in this repository with plan task
`R1`. Nothing has been verified end to end from iPhone to computer yet.

| ID | Requirement | Status |
| --- | --- | --- |
| FR-01 | **Start scan.** ARKit world tracking; captures blocked while tracking is degraded. | Built, device-verified |
| FR-02 | **Floor lock.** Lock a detected horizontal floor as the reference plane. | Built, device-verified |
| FR-03 | **Stand-in-place room capture.** While the user turns in one spot, detected wall planes become four walls and the room's corners and dimensions are derived. Says which way to look when a wall is missing. | Hackathon (automatic room scan) |
| FR-04 | **Fallback room capture.** Sweep each wall's floor line, or walk to each corner. | Sweep: hackathon. Walked corners: built, device-verified |
| FR-05 | **Validated footprint.** Reject self-crossing, too-small or implausible rooms; closure check on the manual paths. | Built, device-verified |
| FR-06 | **Room height.** From the ceiling plane when seen; otherwise aim at the wall/ceiling line or type it. | Ceiling plane: hackathon. Aim and type: built, device-verified |
| FR-07 | **Walls and ceiling.** Derived from the footprint and height. | Built |
| FR-08 | **Doors and windows.** Automatically from labelled planes; otherwise mark two corners on a wall. | Automatic: hackathon. Manual: built, device-verified |
| FR-09 | **Furniture identification on the phone.** A YOLO-n detector names furniture in the camera image; each item is matched to its measured surface for size and position; one tap adds them all. | Not built (plan task `R4`, ADR-0011) |
| FR-10 | **Furniture measurement.** Width, depth, height and yaw from the ARKit surface the object sits on; adjustable. | Hackathon (surface detection) |
| FR-11 | **Manual furniture fallback.** Pick a type, aim at its floor position, adjust size and yaw. | Built, device-verified |
| FR-12 | **Send to Computer.** After finalizing, one button finds the user's computer and delivers the room; no IP or port; the phone reports success only after the computer confirms; the computer is remembered. | Discovery and send: hackathon. Confirmation and remembering: not built (`R7`) |
| FR-13 | **Live preview (optional).** The computer shows the room while the phone scans. | Built, needs a typed IP today |
| FR-14 | **Viewer.** Orbit, dollhouse mode, select, move, rotate, resize, measure 3D and horizontal distance. | Built |
| FR-15 | **Save and load** a room on the computer. | Built (single save slot) |
| FR-16 | **Export for Unity.** One folder per room: `scene.json`, a whole-room `.glb` with openings, one `.glb` per object; imports into Unity with glTFast. | Per-object `.glb`: hackathon. Whole room and folder: not built (`R6`) |
| FR-17 | **Detector label kept.** A `generic` object keeps the detector's class name, such as "potted plant". | Not built (`R5`, additive schema change) |
| FR-18 | Delete and hide objects; remove walls; floor-plan view. | Not built, not scheduled |

## 8. Non-functional requirements

| Area | Requirement |
| --- | --- |
| Accuracy | Median wall error ≤ 0.12 m, max ≤ 0.20 m, height error ≤ 0.15 m against a tape measure (plan task `R3`) |
| On device | Capture and recognition run entirely on the phone. Nothing leaves it except the finished room sent to the user's own computer |
| Speed | Detector runs about five times a second without dropping the camera below a smooth frame rate; Send to Computer completes within a few seconds on a local network |
| Usability | A first-time user can scan a furnished bedroom by standing in it and turning, with no technical knowledge |
| Compatibility | Any ARKit-capable non-Pro iPhone; macOS computer (Windows intended, not claimed) |
| Reliability | Losing the network never destroys scan data on either side |
| Portability | Exported files open in Unity (glTFast) and Blender, in metres, +Y up |
| Privacy | No faces, no identity data, no cloud; camera images never leave the phone |
| Licensing | The YOLO-n detector is AGPL-3.0 (ADR-0011) |

## 9. Assumptions

The room is rectangular or near-rectangular with vertical walls and a
horizontal floor. The user can stand somewhere with a view of all four walls,
moves slowly, and the room has enough visual texture for ARKit. Phone and
computer share a network, or the computer joins the phone's hotspot.

## 10. Data model

The scene is one `SceneSnapshot` holding one `RoomModel`:

```text
Room
├── heightM
├── corners[4]     ordered floor points, Y = 0     (walls derived from these)
├── openings[]     door | window, on wall (startCornerId -> endCornerId)
│                  offsetM, widthM, sillHeightM, heightM
└── objects[]      type, center, yawDeg, widthM, depthM, heightM
                   (+ optional label from the detector, task R5)
```

Exact field definitions, units and rules: [`docs/contracts/scene-schema-v1.md`](../contracts/scene-schema-v1.md).

## 11. Acceptance criteria

GhostMap is complete when, on a non-Pro iPhone and without LiDAR:

- the user stands in one spot, turns, and gets a recognisable four-wall room within the accuracy targets;
- height and at least one door are captured;
- a bed, a desk and a chair are identified on the phone and added with one tap;
- Send to Computer delivers the room with no IP typed, and the phone confirms it;
- the computer shows the room; the user can remove the ceiling, select, move, rotate and resize an object, and measure between two objects;
- the room saves and reloads;
- the exported files import into a fresh Unity project at the right scale.

The step-by-step test is plan section 23.

## 12. Demo sequence

Open the Viewer on the laptop. On the phone, lock the floor, stand in the middle
of the room and turn: the walls appear. Show the height and the door. Point at
the furniture: "Found: bed, desk, chair", Add All. Finalize, tap Send to
Computer, and the room appears on the laptop. Switch to dollhouse mode, move the
desk, measure, then export and drop the room into a Unity scene. Close with:

> A camera sees pixels. GhostMap understands spaces.

The demo-day checklist is plan section 32.

## 13. Vision

```text
Physical environment
        ↓
   Normal iPhone           stand, turn, point
        ↓
Spatial understanding      walls, height, openings, named furniture
        ↓
  Structured scene
        ↓
┌───────┼─────────┐
↓       ↓         ↓
Humans  Robots    Software
↓       ↓         ↓
AR/VR   Navigation Unity, simulation
```

GhostMap converts the physical world into structured spatial information that
people, programs, robots and game engines can use. More detector classes,
spatial queries, multi-room capture and optional photoreal furniture are the
next layers; their order is fixed by plan section 25.
