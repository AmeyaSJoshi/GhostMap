# ADR-0007: Natural-language add/remove of furniture in the viewer

- **Status:** Proposed — under discussion. **Nothing here is decided.**
- **Date:** 2026-10-03
- **Task:** LLM-driven asset editing (branch `feature/nl-asset-editing`)

## Context

The request: "add an LLM that lets us add and remove assets easily."

This ADR exists to hold that discussion. It records what the codebase can
actually do today, which project rules the feature collides with, and the
options — not a decision.

### The viewer cannot add or remove objects at all

This is the finding that reframes the request. `ViewerEditableScene`'s entire
post-finalization mutation surface is one method:

```csharp
public bool TryApplyLocalEdit(SceneObjectModel updated, out string error)
```

It takes an **existing** object, matched by `id`, validates it with the shared
`FurnitureValidator`, and bumps the viewer-owned revision. Everything the UI
offers is built on it — `ObjectEditController.TrySetPositionXZ`, `TrySetYaw`,
`TrySetWidth`, `TrySetDepth`, `TrySetHeight`, `TryDragToFloorPoint`.

There is **no add and no remove anywhere viewer-side.** Objects can only be
created during the scan, by `ObjectPlacementController` (tap to place) or
`FurnitureDetectionController` (`ADR-0006`). Once the scanner finalizes, the set
of objects is fixed and only their numbers can change.

There is also **no undo.**

So "an LLM that lets us add and remove assets" is not primarily an LLM feature.
The capability it would drive does not exist. An LLM bolted onto today's viewer
could move and resize furniture; it could not add or remove a single piece.

### Which rules this collides with

| Rule | Text | Bearing |
| --- | --- | --- |
| `AGENTS.md` rule 6 | "Do not add LiDAR, Gaussian splatting, NeRFs, **cloud inference**, or automatic object recognition to the MVP unless all MVP acceptance tests already pass." | **Direct hit.** A hosted LLM is cloud inference. Not an interpretation — the words are in the rule. The MVP acceptance tests have **not** passed; neither `ADR-0005` nor `ADR-0006` has been on a phone yet. |
| Plan section 25 | "Only start after full MVP acceptance test passes." Six stretches in **strict order**. | This feature is on none of the six. It is new scope ahead of a queue that has not started. |
| Plan section 25, Stretch 2 | "Do not let model output directly mutate room without confirmation." | Directly applicable and worth adopting whatever else is decided. |
| Plan section 25, Stretch 6 | "Because scene is structured, add **deterministic** queries first." | The plan's own instinct: build the deterministic version before the inference one. |
| `ADR-0003` | Authority is time-sliced once at `scan.finalized`; the viewer owns state afterwards. | Favourable. Viewer-side editing is where this belongs. |
| `AGENTS.md` rule 3 | The shared package is the only source of truth for domain models and validation. | Add/remove must reuse `FurnitureValidator`, never reimplement the rules. |
| `AGENTS.md` rules 11, 13 | Tests required. | An LLM is nondeterministic. Whatever ships, the testable seam has to be deterministic — see "Testability" below. |

`ADR-0006` already overrode the section 25 gate once, deliberately and on the
record. That override was for work whose mechanism — reading horizontal planes —
the project already depended on. This one would be a second override, of a rule
that names the mechanism explicitly. That is a different weight of decision and
is the user's to make, not something to slide past.

## The shape being proposed

Two independent pieces. The ordering is the substantive recommendation.

### Piece 1 — add/remove in `ViewerEditableScene`. No LLM.

```csharp
public bool TryAddObject(SceneObjectModel created, out string error)
public bool TryRemoveObject(string id, out string error)
```

Both mirroring `TryApplyLocalEdit` exactly: gated on `EditingEnabled`, validated
by the shared `FurnitureValidator`, bumping the viewer-owned revision, failing
without side effects. Placement additionally gated on
`RoomGeometry.ContainsPointXZ` — added and tested on the current branch for
`ADR-0006`, and the same containment question applies here.

Plus a bounded undo stack. `Clone` already exists, so undo is a depth-limited
list of prior snapshots and little else.

This piece is deterministic, fully testable, needs no network, no API key and no
rule override, and it is **the actual blocker**. It is worth building whether or
not an LLM is ever added, and a palette-and-click UI on top of it may well be
the whole feature.

### Piece 2 — the LLM, as a thin tool layer

Four tools, mapping one-to-one onto Piece 1 plus a read:

```text
list_objects()                      -> ids, types, dimensions, positions
add_object(type, placement)         -> Piece 1's TryAddObject
remove_object(id)                   -> Piece 1's TryRemoveObject
edit_object(id, fields)             -> existing TryApplyLocalEdit
```

Three constraints that matter more than the model choice:

1. **The LLM never emits coordinates.** It emits room-relative intent — "against
   the wall from `c0` to `c1`, 0.4 m from `c0`, facing in" — and a deterministic
   resolver converts that to `center` and `yawDeg` using `RoomGeometry.BuildWalls`
   and `WallGeometry.ToWallLocal`/`FromWallLocal`. Language models are poor at
   metric geometry and GhostMap already has exact, tested wall math. Keeping
   coordinates out of the model is the single highest-value decision here.
2. **Preview, then confirm. Never direct mutation.** This is plan section 25
   Stretch 2's explicit instruction, and it is what makes nondeterminism
   tolerable: a wrong interpretation costs a glance, not a corrupted scene.
3. **Every call goes through `FurnitureValidator`.** A hallucinated nine-metre
   desk is then rejected by code that already exists and already has tests.
   Treat model output as untrusted input, because that is what it is.

### Testability

The LLM cannot be unit-tested meaningfully. The design above is chosen so that
almost nothing depends on it:

- the **resolver** (intent to coordinates) is pure geometry — fully testable;
- the **tool layer** is tested with a stubbed model, asserting that a given tool
  call sequence produces the expected mutations and that invalid ones are
  refused;
- the **model** itself gets a small recorded-transcript suite at most, and is
  never on the path of a correctness guarantee.

If the layer is thick, none of this holds. That is the argument for keeping it
thin, not elegance.

## Open questions — the actual discussion

1. **Is the LLM worth it for this scene size?** A finalized GhostMap room holds
   roughly three to eight boxes. A palette and a click is instant, offline,
   deterministic, testable and keyless. The LLM earns its place on *compound and
   relational* commands — "put a chair either side of the table", "clear
   everything except the bed", "move the desk under the window" — and on Stretch
   6's spatial queries. Is that the real need, or is Piece 1 with a decent UI?
2. **Does it ship, or is it a dev tool?** An editor-only tool sidesteps rule 6
   almost entirely (tooling is not "in the MVP"), ships no API key, and would
   show whether the interaction is even pleasant before any product scope is
   committed.
3. **Rule 6 and section 25.** Override again, or hold until the device session?
   The device session is the next task either way.
4. **"Assets" — which meaning?** Three readings, materially different work:
   furniture *objects* in a scene (assumed throughout this ADR); the exported
   `.glb` files from `ADR-0006`; or furniture *types* in the shared schema, which
   is a contract change under rule 8 and where an LLM helps least.
5. **Where does the key live, if it ships?** Not in the repo, and not in a
   committed `ProjectSettings` asset.

## Decision

None yet. This ADR is a discussion artifact and must not be treated as accepted.
