# Handoff

## Branch
`claude/adoring-archimedes-kle6ph`

## Base commit
`1ff72c9` (merge of PR #21)

## Head commit
The commit that adds this file. Documentation only.

## What changed

The owner set the project's direction, and every document now describes it.

**The product now:** stand in one spot in a room and point a standard non-LiDAR
iPhone around it. The phone finds the walls, dimensions and height, identifies
the furniture on device with a YOLO-n detector, and with one button (**Send to
Computer**) sends the room to the computer. The Viewer shows and edits it and
exports Unity-ready files: `scene.json`, `room.glb`, one `.glb` per object.
Everything runs on the phone; nothing goes to the cloud.

| Document | Change |
| --- | --- |
| `docs/decisions/ADR-0012-stand-in-place-on-device-capture.md` | **New.** The direction, the capture table (primary and fallback for each step), the Unity export folder, what it supersedes in ADR-0004, and why TRELLIS was rejected |
| `docs/decisions/ADR-0011-on-device-yolo-furniture-identification.md` | **New.** YOLO11n via Core ML in a native iOS plugin; detection → floor ray → matched ARKit surface → multi-frame tracker → one-tap Add All; COCO-to-GhostMap class map; optional `label` field; AGPL-3.0 note |
| `docs/decisions/ADR-0010-one-button-computer-transfer.md` | Renumbered from 0007 so `GhostMapDublinHacks`' ADR-0005 to 0009 import without clashing |
| `docs/decisions/README.md` | Index of 0001-0012, showing which live in `GhostMapDublinHacks` |
| `AGENTS.md` | Rule 6 rewritten: everything on the phone; only the YOLO-n detector is allowed, and only to name objects. Rule 14 points to ADR-0010 |
| Plan | New goal and promise (section 0), section 1.2 "use AR wall detection, never depend on it", section 11 detector-first furniture, **section 18 roadmap `R1`-`R8`** replacing `I1`-`I4`, new UX scripts (19, 20), new 13-step acceptance test (23), stretch list (25), schedule (28), verification gate (31), demo (32), completion standard (35). Section numbers unchanged |
| Spec | Rewritten around the stand-in-place workflow; functional requirements FR-01 to FR-18 with real status (built here, in the hackathon repo, or not built) |
| Architecture overview | Capture model (section 4) as primary/fallback with where each piece is built; discovery (6.1); Unity export folder (10.1); scope (11) |
| README | Describes the new product and where each piece is today |
| Status pages | Next tasks point at the roadmap; `integration.md` is now the `R1`-`R8` tracker with device and benchmark log tables |
| Handoffs | All earlier handoffs removed from the working tree (kept in git at `1ff72c9`); this is the only current one |

## Where each piece of the product is

| Piece | Location | Verified on iPhone |
| --- | --- | --- |
| Floor lock, walked corners, closure, manual height, manual openings, manual furniture, TCP streaming, finalize | This repo | Yes (S1-S6) |
| Viewer: render, dollhouse, edit, measure, save/load | This repo | n/a (desktop) |
| Stand-in-place room scan from ARKit planes | `GhostMapDublinHacks` | No |
| Wall sweep fallback | `GhostMapDublinHacks` | No |
| Furniture surface measurement, per-object `.glb` export | `GhostMapDublinHacks` | No |
| Send to Computer (UDP discovery on port 47832) | `GhostMapDublinHacks` | No |
| YOLO-n identification | Not built (`R4`) | — |
| Whole-room `.glb`, export folder | Not built (`R6`) | — |
| Delivery confirmation, remembered computer | Not built (`R7`) | — |

## Contract impact
None now. `R5` will add an optional `label` to `SceneObjectModel`, and `R7` a
Viewer-to-scanner acknowledgement; both go through the contract procedure.
`R1` brings the discovery contract (UDP 47832) from `GhostMapDublinHacks`.

## How to test
Documentation only. Every relative link resolves.

## Test results
No code changed. Last recorded suites: here shared 156, viewer 475, scanner 361;
in `GhostMapDublinHacks` shared 202, viewer 548, scanner 577.

## Known failures
None.

## Decisions the owner has made
- Stand-in-place capture is the primary flow; manual paths are fallbacks.
- Furniture is identified by YOLO-n on the phone, not by ARKit plane labels and
  not by TRELLIS (needs a large NVIDIA GPU, cannot run on a phone).
- Furniture is exported as 3D files; the computer is used in Unity.
- One-button Send to Computer, no IP typing.

## Files most important to read next
1. `docs/decisions/ADR-0012-stand-in-place-on-device-capture.md`
2. Plan section 18 (roadmap)
3. `docs/decisions/ADR-0011-on-device-yolo-furniture-identification.md`
4. `docs/status/integration.md`

## Next task
**`R1`: bring `GhostMapDublinHacks` into this repository.** Merge its `main` on
an `integration/import-dublinhacks` branch. The owner must approve the merge
explicitly; an automated session was blocked from doing it without that. Keep
ADR numbers 0005-0009, resolve docs conflicts in favour of these documents,
regenerate both scenes, and run all three suites.
