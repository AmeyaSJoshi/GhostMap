# ADR-0006: Detect furniture from horizontal planes, and export each object as a glTF asset

- **Status:** Accepted
- **Date:** 2026-10-03
- **Task:** Furniture detection + asset export (branch `integration/furniture-detect-and-export`)

## Context

Task S5 places furniture by hand: the user picks a type, aims the center-screen
ray at the floor, taps, and then nudges width, depth, height and yaw with `+`/`-`
buttons in 0.10 m and 15-degree steps. It works and it is device-verified, but it
is slow, the dimensions are the type's MVP defaults rather than the real
object's, and the scanner status already records that adjustment "always acts on
the most recently placed object; there is no way to select and re-edit an earlier
one."

Two things are wanted:

1. the app should **detect** furniture and measure its **real-world
   dimensions**, instead of the user typing in a guess;
2. each detected object should become a **separate asset** — a 3D model file per
   piece, generated from those measurements.

### What GhostMap already has, and what it does not

There are **no assets anywhere in GhostMap today**. No models, no prefabs, no
meshes on disk. Furniture is seven numbers — `SceneObjectModel`'s `type`,
`center`, `yawDeg`, `widthM`, `depthM`, `heightM` and `id` — and the viewer
generates geometry from them at runtime out of `PrimitiveType.Cube` parts
(`FurnitureFactory`). Persistence is one JSON file for the whole scene.

So "a separate asset per object" cannot mean importing or authoring models.
It means **generating** a model file from the measured dimensions.

### Why not camera inference

The obvious reading of "detect furniture" is a model that looks at the camera
feed and says "that is a bed". `AGENTS.md` rule 6 prohibits exactly that:

> Do not add LiDAR, Gaussian splatting, NeRFs, cloud inference, or automatic
> object recognition to the MVP unless all MVP acceptance tests already pass.

The MVP acceptance test has not been run — Integration `I1`-`I4` are not
started. Implementation plan section 25 additionally places "automatic object
suggestion" at Stretch 2 and specifies its shape: "use camera inference only to
**suggest type**. User still confirms placement/dimensions."

**No inference of any kind is added by this ADR.** Nothing here recognizes what
an object is.

### Why horizontal planes are available, and are not what ADR-0004 rejected

`ADR-0004` rejected *depending* on AR vertical-plane detection for walls, because
detecting every wall is "slow, partial, and unreliable on a non-Pro device —
especially for blank walls". That reasoning is about blank, untextured, vertical
surfaces viewed edge-on.

Horizontal furniture surfaces are the opposite case. A desk top, bed, table,
dresser or TV stand is textured, viewed from above at a favourable angle, and
usually well lit. It is also **the same detection the floor lock already depends
on and has device-verified**: `ARPlaneManager` is already in the scanner scene
with `PlaneDetectionMode.Horizontal` requested, and
`ScannerSceneBuilder.VerifyScene` already asserts it.

And the key geometric fact: **a horizontal plane at Ghost-space height `y` is the
top surface of an object `y` tall.** The height falls out of the detection
directly, which is the single hardest number for the user to supply by hand.

## Decision

### 1. Furniture is detected from horizontal planes; the user confirms the type

The scanner enumerates detected horizontal-up planes during the existing
`AddObjects` phase. For each, in Ghost space:

```text
heightM  = plane centre's Ghost y          (top surface height above the floor)
widthM   = plane extent along its local X  (observed footprint)
depthM   = plane extent along its local Z
yawDeg   = plane's local X axis, projected into Ghost XZ
center   = plane centre projected to y = 0 (SceneObjectModel.center is on the floor)
```

A candidate is offered to the user only if it passes every gate:

| Gate | Reason |
| --- | --- |
| alignment is `HorizontalUp` | a ceiling or a downward face is not furniture |
| Ghost `y` within `0.20 m` - `1.40 m` | below is the floor or a rug, above is a ceiling or a shelf out of MVP scope |
| both extents `>= 0.30 m` | smaller is clutter, and matches the opening-width floor |
| footprint centre inside the room polygon | a plane detected through a doorway belongs to the next room |
| `FurnitureValidator.Validate` passes | the shared rules, unchanged |
| not already accepted | a plane grows as ARKit observes more of it, so it must not be added twice |

**The user picks the type** from S5's existing eight categories and confirms. A
detection never mutates the scene on its own. This keeps the spirit of Stretch
2's rule — "do not let model output directly mutate room without
confirmation" — while inverting which half is automatic: GhostMap measures the
geometry and the human names the thing, rather than the reverse.

Detected dimensions **replace** the type's MVP defaults. That is the point: the
defaults exist only because nothing measured the real object.

### 2. The S5 manual path is retained, unchanged

Plane detection can fail: a glass table, a black couch in low light, a bed under
a duvet with no clear edge. When it does, the user places the object by hand
exactly as in S5. Detection is **additive** — a second input method for the same
`AddObjects` phase, not a replacement.

This is also why no new `ScanPhase` is introduced. Detection happens inside
`AddObjects`, which already exists, so the state machine is unchanged.

### 3. Each object is exported as a binary glTF (`.glb`) asset

The viewer writes one `.glb` per furniture object, into a
`ghostmap-assets/` directory beside the scene JSON under
`Application.persistentDataPath` so it resolves in a standalone player build and
not only in the Editor.

**Furniture only.** A whole-room `.glb` is *not* implemented: the room's walls
are a segmented grid with openings cut out (`WallSliceGenerator`) and the floor
and ceiling are triangulated separately, so exporting the room means
re-expressing three more geometry sources. That is a larger and riskier job than
the furniture the request asked for, and claiming it here without building it
would be worse than leaving it out.

glTF 2.0 binary was chosen over OBJ: it carries materials and colour in the file
itself, is the standard interchange format for Blender, Unreal, three.js and
every modern viewer, and needs no sidecar `.mtl`. It is written by hand — a 12
byte header, a JSON chunk and a binary chunk — with no new package dependency,
because the geometry involved is axis-aligned boxes.

**The exporter calls `FurnitureFactory.BuildParts`**, the same pure function the
renderer uses to lay out a type's boxes. It is not a second description of what a
bed looks like: the exported file matches what is on screen by construction, and
a change to the part table changes both together.

## Compatibility

**Additive. No schema change, no protocol change, no new scan phase.**

| Contract | Impact |
| --- | --- |
| `SceneObjectModel` | None. Detected objects are ordinary parametric furniture — same seven fields, same eight types. |
| `SceneSnapshot`, `RoomModel` | None. Still `schemaVersion` 1. |
| Protocol v1 | None. A detected object reaches the viewer in the same `scene.snapshot` as a hand-placed one, and is indistinguishable from it. |
| `ScanPhase` | **None.** Detection lives inside the existing `AddObjects`. |
| `scene-schema-v1.md`, `protocol-v1.md` | No change required. |
| Saved scene JSON | None. `.glb` files are written alongside it, never instead of it. |

New shared geometry: `RoomGeometry.ContainsPointXZ`, needed for the
"inside the room" gate. Purely additive.

### Deviation from implementation plan section 25

Section 25 opens with "Only start after full MVP acceptance test passes", and the
MVP acceptance test has not been run.

**This ADR starts stretch-adjacent work before that gate, deliberately, at the
project owner's explicit direction.** Recording it rather than quietly doing it
is the point of this section.

Two things limit the damage:

- `AGENTS.md` rule 6 is **not** overridden. No LiDAR, no splatting, no NeRF, no
  cloud inference, no object recognition is added. Rule 6's list is untouched.
- Nothing existing is removed. S5 manual placement, the schema, the protocol and
  the state machine are all unchanged, so if this work is shelved the MVP path is
  exactly as it was.

Section 25's own ordering — Stretch 1 (furniture library) before Stretch 2
(object suggestion) — is also not followed, because the requested work is
neither: it is measurement plus generation, with no authored model library and
no type inference.

## Scanner impact

- `ISpatialProvider` gains `TryGetHorizontalPlanes`, so plane enumeration is
  mockable and every detection rule stays testable off-device.
- **New** `FurnitureDetectionController` (plain C#): enumerates candidates,
  applies the gates, tracks which planes have been accepted.
- **New** `FurnitureDetectionHud`.
- `ScanWorkflowController` gains the accept/dismiss/rescan methods and publishes
  a snapshot per accepted object, exactly as manual placement does.
- The S2 frame is read, never written.

## Viewer impact

- **New** `GlbExporter` (plain C#): `SceneObjectModel` -> `.glb` bytes, and the
  whole room -> `.glb` bytes.
- **New** export controls on `ViewerHudController`, gated by `ADR-0003` the same
  way Save already is: exporting is only offered for a finalized, Viewer-owned
  scene.
- `FurnitureFactory` gains a public `ColorFor(string type)` so the exporter uses
  the one colour table rather than a copy.
- No rendering change. No change to what the viewer displays.

## Tests

Shared:

- `ContainsPointXZ` for inside, outside, on an edge, at a vertex, and for a
  concave footprint; unaffected by winding direction.

Scanner:

- each gate refuses in isolation: wrong alignment, too low, too high, too small,
  centre outside the room, already accepted;
- height comes from the plane's Ghost `y`, not from a type default;
- yaw is recovered from the plane's axis through the Ghost frame;
- a candidate failing `FurnitureValidator` is refused;
- accepting publishes exactly one revision; dismissing publishes none;
- a plane that grows between frames does not produce a duplicate object;
- detection is refused outside `AddObjects`, before floor lock, and with bad
  tracking;
- the S5 manual path still works with detection present.

Viewer:

- the `.glb` byte stream has the right magic, version, total length, and two
  correctly aligned and padded chunks;
- the JSON chunk pads with spaces and the binary chunk to a 4-byte boundary;
- numbers are formatted invariantly, so a comma-decimal locale cannot emit
  unparseable JSON;
- its JSON parses, and declares one mesh, one material and the expected
  accessor/bufferView counts;
- `POSITION` accessor `min`/`max` equal the object's real bounding box in meters;
- geometry comes from `FurnitureFactory.BuildParts`, proved by asserting the
  vertex count tracks the part count for several types;
- a bed exports with its real measured dimensions, not the type default;
- export is refused for an unfinalized, Scanner-owned scene.

**Physical-device test is required** for the detection half, per `AGENTS.md`
rule 12. Plane detection quality in a real room cannot be established off-device.

## Consequences

**Positive**

- Real measured dimensions instead of per-type guesses, and `heightM` — the
  hardest number to supply by hand — falls out of the detection directly.
- Far less tapping: no `+`/`-` nudging of four parameters per object.
- Each object becomes a real 3D model that any other tool can open, which is the
  first time GhostMap's structured data leaves as geometry.
- Reuses detection already shipped, already in the scene, already device-verified
  for floor lock. No new AR capability, no new dependency.
- Detection failure is not fatal: the S5 manual path is still there.

**Negative**

- **ARKit reports the surface it has observed, not the object.** A plane's extent
  grows as the user looks around and can stop short of a table's edge or run past
  it onto an adjacent surface. Measured width and depth are therefore a lower
  bound in practice, and the user must be able to correct them — S5's adjustment
  controls remain for exactly this.
- **Height is the top surface, which is not always the object's height.** It is
  right for a desk, table, dresser or bed. It is wrong for a chair, where the
  detected plane is the seat and the back rises above it, and for a couch for the
  same reason. The user corrects these.
- A plane under a rug, or a low windowsill, can present as furniture. The
  `0.20 m` floor and the in-room gate catch most of it, not all.
- Glass and dark surfaces may not be detected at all.
- `.glb` files are generated geometry, not scans. They are exactly as accurate as
  the measured dimensions and the part table, and no more.
- Exported assets are a point-in-time copy. Editing an object in the viewer after
  exporting leaves a stale `.glb` until the user exports again; there is no
  reconciliation.
- There is no whole-room `.glb`, only per-object ones. See the Decision above.
- Undoing a detected object in the scanner does not un-dismiss its surface, so
  the same surface is not re-offered until the user clears detection. Accepting
  is cheap to repeat; un-resolving on undo would need the object store to
  remember which surface produced which object, which the schema has no field
  for.

## Compliance

- `AGENTS.md` rule 6 remains in force. Adding any inference, recognition, LiDAR,
  splatting, NeRF or cloud call requires the MVP acceptance test to pass first,
  and a new ADR.
- `FurnitureValidator` remains the only authority on whether a furniture object
  is legal. Detection must not introduce a second copy of any dimension rule.
- `FurnitureFactory.BuildParts` remains the only description of a type's part
  layout. The exporter must not grow its own.
- Removing the S5 manual placement path requires a new ADR.
