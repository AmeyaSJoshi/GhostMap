# Handoff

## Branch
`claude/adoring-archimedes-kle6ph`

## Base commit
`24b4720` (merge of PR #22)

## Head commit
The commit that adds this file. Documentation only.

## What changed

The owner chose to replace the desktop Unity Viewer as the destination with a
**phone-built export bundle, the iOS share sheet, and a browser viewer**
(ADR-0013). This supersedes the network-discovery transfer in the previous
handoff (`2026-10-10-shared-project-direction.md`); the capture direction there
(stand in place, YOLO-n on device) is unchanged.

**The product now:**

```text
Phone:    scan -> finalize -> phone writes GhostMap-<room>-<time>.zip
            (room.html, room.glb, objects/<id>.glb, scene.json, README.txt)
          -> Send to Computer = iOS share sheet -> AirDrop / Files / iCloud / Mail
Computer: double-click room.html (any browser, offline): orbit, dollhouse,
          click for size, measure
          drag the .glb files into Unity (glTFast)
```

| Document | Change |
| --- | --- |
| `ADR-0013-phone-export-and-browser-viewer.md` | **New.** The bundle, the share sheet, `room.html`, editing moves to Unity, the Unity Viewer frozen then retired, protocol v1 kept as a developer tool |
| ADR-0003, ADR-0010, ADR-0012 | Status lines mark the parts ADR-0013 supersedes |
| ADR index | 0009 superseded, 0013 added, next is 0014 |
| `AGENTS.md` | Rule 4: after finalization the bundle is the record. Rule 14: Send to Computer is the share sheet; TCP is developer-only. Viewer workstream owns `apps/web-viewer/` |
| Plan | Goal, architecture and promise rewritten; roadmap now **`R1`-`R10`**: R6 phone builds the bundle, R7 share sheet, R8 browser viewer, R9 hardening, R10 retire the Unity Viewer. New UX scripts, acceptance test, transfer checklist, performance targets, demo. Sections 12-13 and 2.3 marked legacy |
| Spec | Workflow, FR-12 to FR-19, NFRs, acceptance and demo rewritten for the bundle, share sheet and browser |
| Architecture overview | New topology diagram, modules (web viewer planned, Unity Viewer frozen), product transfer (6.1), authority (7), export bundle (10.1) |
| `protocol-v1.md` | Marked as the developer live stream |
| README, `apps/viewer/README.md`, status pages | Match the above; `integration.md` tracks `R1`-`R10` |

## Contract impact
None now. `R6` adds `docs/contracts/export-bundle-v1.md` and the shared
`Runtime/Export/` namespace; `R5` adds the optional `label` field.

## How to test
Documentation only. Every relative link resolves.

## Test results
No code changed.

## Known failures
None.

## Decisions the owner has made
- Stand-in-place capture; YOLO-n identification on the phone (unchanged).
- The phone builds the Unity files and `room.html`; Send to Computer is the iOS
  share sheet; nothing to install on the computer.
- The browser viewer is view and measure only; furniture is edited in Unity.
- The Unity Viewer is frozen and will be removed.

## Files most important to read next
1. `docs/decisions/ADR-0013-phone-export-and-browser-viewer.md`
2. Plan section 18 (roadmap `R1`-`R10`)
3. `docs/status/integration.md`

## Next task
**`R1`: bring `GhostMapDublinHacks` into this repository** (owner approval
required for the merge). `R6` and `R8` can be designed in parallel since they
do not depend on device results.
