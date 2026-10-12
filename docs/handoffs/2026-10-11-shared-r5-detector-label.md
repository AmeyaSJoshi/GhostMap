# Handoff

## Branch
`shared/r5-detector-label`, stacked on `integration/r11-object-photos` (PR #35).

## Base commit
`6f1055d` (R11 docs)

## Head commit
The commit that adds this file.

## What changed
Task `R5`: an object keeps what the on-device detector called it.

- **Shared (contract):** `SceneObjectModel.label`, optional, additive within
  schema v1. `FurnitureValidator.MaxLabelLength = 64` and `ValidateLabel`
  (no control characters). `docs/contracts/scene-schema-v1.md` sections 5 and
  9. `tools/inspect_snapshot.py` checks and prints it.
  `fixtures/room-with-door-window-v1.json`: the desk has
  `"label": "dining table"`.
- **Scanner:** `FurnitureIdentificationController.TryBuild` and the surface
  accept path set `label` from the COCO class (`LabelFor`); a surface the user
  retyped gets none; hand-placed objects get none.
  `ObjectPlacementController.Clone` copies it, so snapshots keep it.
- **Unity Viewer (frozen):** `ObjectEditController.Clone` copies `label`
  (one line, so an edit does not drop it).
- **Web viewer:** the label titles only `generic` objects; every labelled
  object shows "Detected as". Template rebuilt and copied to the scanner.

## Contract impact
Scene schema v1, additive (rule 8): contract doc, tests in shared, scanner and
web viewer, the contract change in its own commit, status pages, this handoff.
`schemaVersion` stays 1; older readers ignore the field, and a missing field
reads as no label.

## How to test
1. `./tools/run_unity_tests.sh shared viewer scanner`
2. `cd apps/web-viewer && npm test`
3. `python3 tools/inspect_snapshot.py fixtures/room-with-door-window-v1.json`
   (the desk prints `"dining table"`).

## Test results
Owner's Mac, 2026-10-11: shared **303/303**, viewer **580/580**, scanner
**821/821**, web viewer **48/48**, inspector on the fixtures as before plus a
copy with a 65-character label and a newline in another (both rejected).

## Known failures
None.

## Known limitations
- The label is the raw COCO name in English ("dining table", "potted plant").
- Not run on a device.

## Files most important to read next
- `docs/contracts/scene-schema-v1.md` section 5
- `shared/com.ghostmap.shared/Runtime/Validation/FurnitureValidator.cs`

## Next task
`R9` (hardening), the parts that need no device. Owner: `R2`.
