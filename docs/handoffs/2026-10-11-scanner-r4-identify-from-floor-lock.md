# Handoff

## Branch
`scanner/r4-yolo-identification` (same branch as
`2026-10-11-scanner-r4-yolo-identification.md`; this is a follow-up commit).

## Base commit
`c2a724d` (R4 docs)

## Head commit
The commit that adds this file.

## What changed
Owner's request: furniture identification runs **from floor lock**, not only
in the furniture step. The camera passes every piece of furniture while the
user turns to scan the room, so "Found: ..." is ready when `AddObjects` opens.

- `ScanWorkflowController.IdentificationRuns`: floor locked and not yet
  `ReadyToFinalize`/`Finalized`. `UpdateFurnitureIdentification` and
  `FurnitureDetectionHud` use it.
- `FurnitureIdentificationController`: no room needed; floor points over
  `MaxRangeM` (8 m) are ignored; once the room closes, tracks outside it are
  dropped; proposals are no longer forgotten after 3 s (only unproposed
  sightings are); a proposal seen before surfaces existed takes the surface
  under its centre once measured.
- ADR-0011 "As built", the R4 brief and the status pages record the change.

## Contract impact
None.

## How to test
1. `./tools/run_unity_tests.sh scanner`
2. On the iPhone: after floor lock, scan the room by turning in place; at the
   furniture step "Found: ..." should already list what you turned past.

## Test results
Owner's Mac, 2026-10-11: scanner EditMode **779/779** (+6: refused before
floor lock, collected before the room closes, dropped outside the closed room,
8 m range, proposals kept, late surface match, and a whole workflow from
floor lock to Add All; three existing tests adjusted).

## Known failures
None.

## Known limitations
- The model now runs for the whole scan: more battery and heat than before.
  Watch for camera stutter in `R2`.
- A false sighting that reaches 5 frames stays proposed until Add All (then
  Undo removes it).

## Files most important to read next
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Capture/FurnitureIdentificationController.cs`

## Next task
`R11` (a photo per object) on a stacked branch; owner: `R2`.
