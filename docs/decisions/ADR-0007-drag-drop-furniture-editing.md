# ADR-0007: Add and remove furniture by drag and drop, not by language model

- **Status:** Accepted
- **Date:** 2026-10-03
- **Task:** Drag-and-drop furniture editing (branch `feature/drag-drop-furniture`)

## Context

The request began as "add an LLM that lets us add and remove assets easily."
Scoping it surfaced a finding that reframed the whole thing, and the chosen
interface changed as a result. Both are recorded here, because the rejected
option is the more instructive half.

### The viewer cannot add or remove objects at all

`ViewerEditableScene`'s entire post-finalization mutation surface is one method:

```csharp
public bool TryApplyLocalEdit(SceneObjectModel updated, out string error)
```

It takes an **existing** object, matched by `id`, validates it with the shared
`FurnitureValidator`, and bumps the viewer-owned revision. Everything the UI
offers is built on it — `ObjectEditController.TrySetPositionXZ`, `TrySetYaw`,
`TrySetWidth`, `TrySetDepth`, `TrySetHeight`, `TryDragToFloorPoint`.

There is **no add and no remove anywhere viewer-side.** Objects are created only
during the scan, by `ObjectPlacementController` (tap to place) or
`FurnitureDetectionController` (`ADR-0006`). Once the scanner finalizes, the set
of objects is fixed and only their numbers can change. There is also **no undo.**

So the missing capability was never the language model. Whatever drives it,
`ViewerEditableScene` needs two new operations first.

### Most of drag and drop already exists

The viewer can already drag an existing object across the floor:
`ObjectEditController.TryDragToFloorPoint` projects a screen ray onto `y = 0`
with `RayPlaneMath.TryIntersectHorizontalPlane` and moves the selection there.
`ViewerInteractionRouter` already separates a click from a drag with
`IsClick(down, up, thresholdPx)`.

What is missing is narrow: something to drag *from*, somewhere to drop *to* that
means "create", a way to delete, and undo.

## Decision

**Add and remove furniture by direct manipulation: a palette of the eight
supported types, dragged into the room to create, and a delete affordance on the
selection. No language model.**

Two layers:

### 1. Scene layer — `ViewerEditableScene`

```csharp
public bool TryAddObject(SceneObjectModel created, out string error)
public bool TryRemoveObject(string id, out string error)
```

Both mirror `TryApplyLocalEdit` exactly, because that method already establishes
the contract every viewer-side mutation follows:

- refuse unless `EditingEnabled` (that is, unless the scan is finalized — `ADR-0003`'s
  authority hand-off);
- validate with the **shared** `FurnitureValidator`, never a viewer
  reimplementation of the rules (`AGENTS.md` rule 3);
- additionally gate placement on `RoomGeometry.ContainsPointXZ`, so an object
  cannot be dropped outside the room's own footprint;
- bump the viewer-owned revision (implementation plan sections 13.3/13.4);
- fail without side effects.

Plus a bounded undo stack. `Clone` already exists, so undo is a depth-limited
list of prior snapshots and little else. Undo is **not** optional here: direct
manipulation makes destructive actions one gesture away, which is exactly the
case `TryApplyLocalEdit`'s callers never had.

### 2. Interaction layer

- a palette listing `FurnitureValidator.SupportedTypes`, each entry showing the
  type's default dimensions from `TryGetDefaultDimensions`;
- drag from palette into the room: the drop point comes from the existing
  `RayPlaneMath.TryIntersectHorizontalPlane`, the dimensions from the type's
  defaults, and the result goes through `TryAddObject` — which may refuse it, at
  which point nothing is created and the HUD says why;
- delete on the current selection, routed through `TryRemoveObject`;
- both reuse `ViewerInteractionRouter`'s existing click-versus-drag threshold
  rather than introducing a second notion of what a drag is.

## Why not a language model

Not rejected as unworkable — rejected as the wrong tool for this scene, and
blocked by a rule besides.

1. **Scale.** A finalized GhostMap room holds roughly three to eight boxes. For
   "add a chair, delete that desk", a palette and a click beats a sentence on
   every axis that matters: instant, offline, deterministic, no key, no
   latency, no interpretation to get wrong.
2. **`AGENTS.md` rule 6 names it.** "Do not add LiDAR, Gaussian splatting,
   NeRFs, **cloud inference**, or automatic object recognition to the MVP unless
   all MVP acceptance tests already pass." A hosted model is cloud inference —
   the rule's own words, not a reading of them. The MVP acceptance tests have
   not passed; neither `ADR-0005` nor `ADR-0006` has been on a phone.
   `ADR-0006` overrode the plan's section 25 gate once, deliberately and on the
   record; this would have been a second override, of a rule that names the
   mechanism rather than merely implying it. Choosing direct manipulation makes
   the question moot instead of answering it, which is the better outcome.
3. **Testability.** `AGENTS.md` rules 11 and 13 require tests. Direct
   manipulation is fully deterministic and testable at the scene layer. A model
   is not, and keeping it honest would have required a thin tool layer, a
   deterministic coordinate resolver, and a stubbed-model test harness — real
   machinery whose only purpose is to contain the nondeterminism it introduces.
4. **Plan section 25 says so first.** Stretch 6: "Because scene is structured,
   add **deterministic** queries first." Stretch 2: "Do not let model output
   directly mutate room without confirmation."

### What would make a model worth revisiting

Compound and relational commands, which a palette genuinely cannot express:
"put a chair either side of the table", "clear everything except the bed", "move
the desk under the window". If that need appears, the design to reach for is the
thin layer described above — four tools mapping onto this ADR's operations, the
model emitting room-relative intent rather than coordinates, a deterministic
resolver doing the geometry with `RoomGeometry.BuildWalls` and
`WallGeometry.ToWallLocal`/`FromWallLocal`, preview-then-confirm on every
mutation, and `FurnitureValidator` treating model output as the untrusted input
it is. It would need its own ADR and, by rule 6, a passing MVP.

## Consequences

- `ViewerEditableScene` gains its first operations that change the *set* of
  objects rather than their values. Snapshot revision semantics are unaffected:
  protocol v1 already sends whole snapshots (`ADR-0002`), and the viewer already
  owns revision post-finalization (`ADR-0003`).
- No schema change, no protocol change, no scanner change. `SceneObjectModel`
  already carries everything an added object needs.
- No new dependency, no network, no API key, no rule override.
- Undo is new surface area and the main place to be careful: it must capture
  state around *every* mutation path, including the pre-existing
  `TryApplyLocalEdit`, or it will silently fail to undo edits made the old way.
- The eight-type limit stands. Adding a ninth type is a shared contract change
  under `AGENTS.md` rule 8 and plan section 25's Stretch 1, and is out of scope
  here.
