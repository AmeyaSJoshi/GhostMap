# R5: Keep the detector's label in the scene

**Decision:** ADR-0011 ("Classes"). **Workstream:** Shared/Integration (contract
change), with a one-line Scanner change to fill it.

> **Built 2026-10-11** on `shared/r5-detector-label` (handoff
> `docs/handoffs/2026-10-11-shared-r5-detector-label.md`). Differences from
> this brief:
>
> - **Fixture:** `room-with-door-window-v1.json` has no `generic` object, and
>   adding one would change every test that counts its objects and need a new
>   reference `.glb`. Its desk carries `"label": "dining table"` instead, which
>   is what the detector really calls a desk.
> - **Viewer title:** `room.html` titles an object by its label only when it is
>   `generic` ("Potted plant"); a labelled desk stays "Desk", with "Detected
>   as: dining table" in its details.
> - **Surface objects** get the label only when added as the type the detector
>   identified; one the user retyped gets none.
> - **Two copy helpers** that rebuild objects field by field now copy `label`:
>   the scanner's `ObjectPlacementController.Clone` and the frozen Unity
>   Viewer's `ObjectEditController.Clone` (a one-line integration fix so an
>   edit there does not drop it).

## Goal

A `generic` object keeps what the detector called it ("potted plant",
"refrigerator"), so `room.html`, `scene.json` and anyone importing into Unity
can tell a plant from a fridge.

## Change

`shared/com.ghostmap.shared/Runtime/Domain/SceneObjectModel.cs`:

```csharp
/// <summary>Optional. The detector's class name, e.g. "potted plant". Empty when placed by hand.</summary>
public string label;
```

This is **additive within schema v1**: older readers (`JsonUtility`) ignore an
unknown field, and a missing `label` reads as empty. No `schemaVersion` bump.

Follow `AGENTS.md` rule 8 in one dedicated commit:

1. `docs/contracts/scene-schema-v1.md` section 5: add the field to the code
   block and table; rules: optional, at most 64 characters, printable.
2. `FurnitureValidator.Validate`: reject a label longer than 64 characters.
   Add `MaxLabelLength = 64`.
3. `tools/inspect_snapshot.py`: mirror the 64-character rule and print the label
   in the summary.
4. Shared tests (`SceneSchemaTests`, `FurnitureValidatorTests`): label round
   trip; a snapshot without the field parses with an empty or null label (assert
   whichever `JsonUtility` actually produces, and treat both as "no label"
   everywhere); 65 characters rejected.
5. Fixtures: add `"label": "potted plant"` to one `generic` object in
   `room-with-door-window-v1.json` only if a `generic` object exists there;
   otherwise add a new object. Re-run `inspect_snapshot.py` on all three.
6. Scanner (`R4`): fill `label` with the COCO class name for every detected
   object. `R4` keeps it as `IdentifiedFurniture.ClassName`; set it in
   `FurnitureIdentificationController.TryBuild`, and for a surface added with
   an identified type, from the proposal on that surface.

## Done when

CI is green; `inspect_snapshot.py` shows labels; contract doc, status pages and
a handoff are updated.
