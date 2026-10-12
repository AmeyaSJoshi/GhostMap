# Task briefs

Implementation-ready briefs for the roadmap in plan section 18. Each brief
names the files to touch, the existing code to reuse (checked against the
source at the time of writing), the tests to write, what "done" means, and what
only the owner can do (Unity runs, iPhone sessions, approvals).

Read `docs/onboarding.md` first. When a brief and the plan disagree, the plan
and the ADRs win; fix the brief.

| Task | Brief | Depends on | Who can do it |
| --- | --- | --- | --- |
| R1 | Merge `GhostMapDublinHacks` | — | Done; suites green at `69af8eb` (202 / 548 / 577) |
| R2 | [First device session](R2-first-device-session.md) | R1 | **Owner** on an iPhone; an agent prepares the checklist |
| R3 | [Accuracy benchmark](R3-accuracy-benchmark.md) | R2 | **Owner** with a tape measure |
| R4 | [YOLO-n furniture identification](R4-yolo-furniture-identification.md) | R2 | Built 2026-10-11; owner's device check in `R2` |
| R5 | [Detector label in the schema](R5-detector-label.md) | R4 design | Built 2026-10-11 |
| R6 | [Phone-built export bundle](R6-export-bundle.md) | R1 | Built 2026-10-11; owner checks it on the iPhone in `R2` |
| R7 | [Share-sheet Send to Computer](R7-share-sheet.md) | R6 | Built 2026-10-11; owner's AirDrop check in `R2` |
| R8 | [Browser viewer `room.html`](R8-browser-viewer.md) | — (template), R6 (embedding) | Built 2026-10-11; owner's Safari check left |
| R9 | [Hardening and guided flow](R9-hardening.md) | R2, R7, R8 | Agent plus owner device checks |
| R10 | [Retire the Unity Viewer](R10-retire-unity-viewer.md) | Acceptance test passes | Agent |
| R11 | [A photo of each object](R11-object-photos.md) | R4, R6, R8 | Built 2026-10-11; owner's device check in `R2` |

**Can start right now without the owner:** the parts of R9 that need no
device.
