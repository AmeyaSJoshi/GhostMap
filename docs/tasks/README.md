# Task briefs

Implementation-ready briefs for the roadmap in plan section 18. Each brief
names the files to touch, the existing code to reuse (checked against the
source at the time of writing), the tests to write, what "done" means, and what
only the owner can do (Unity runs, iPhone sessions, approvals).

Read `docs/onboarding.md` first. When a brief and the plan disagree, the plan
and the ADRs win; fix the brief.

| Task | Brief | Depends on | Who can do it |
| --- | --- | --- | --- |
| R1 | Merge `GhostMapDublinHacks` | — | Done 2026-10-11; suites still to run (CI or owner) |
| R2 | [First device session](R2-first-device-session.md) | R1 | **Owner** on an iPhone; an agent prepares the checklist |
| R3 | [Accuracy benchmark](R3-accuracy-benchmark.md) | R2 | **Owner** with a tape measure |
| R4 | [YOLO-n furniture identification](R4-yolo-furniture-identification.md) | R2 | Agent writes code and tests; owner builds and device-tests |
| R5 | [Detector label in the schema](R5-detector-label.md) | R4 design | Agent; CI runs tests |
| R6 | [Phone-built export bundle](R6-export-bundle.md) | R1 | Agent; CI runs tests; owner checks files on the Mac |
| R7 | [Share-sheet Send to Computer](R7-share-sheet.md) | R6 | Agent writes plugin and tests; owner device-tests |
| R8 | [Browser viewer `room.html`](R8-browser-viewer.md) | — (template), R6 (embedding) | Agent, fully testable with Node |
| R9 | [Hardening and guided flow](R9-hardening.md) | R2, R7, R8 | Agent plus owner device checks |
| R10 | [Retire the Unity Viewer](R10-retire-unity-viewer.md) | Acceptance test passes | Agent |

**Can start right now without the owner:** R8 (entirely), R6 (code and tests),
R5 (once R4's class list is settled), R4 (code and tests).
