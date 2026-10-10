# Integration Status

Workstream: Shared / Integration. Tracks the stand-in-place roadmap, tasks
`R1`-`R8` in plan section 18 (direction: ADR-0012).

## Current state

**Roadmap not started. Next: `R1`.**

| Task | What | State |
| --- | --- | --- |
| R1 | Bring `GhostMapDublinHacks` into this repository | Not started. Needs the owner's explicit approval to merge |
| R2 | First device session: every path on a real iPhone with the Viewer on the Mac | Not started |
| R3 | Accuracy benchmark against a tape measure | Not started |
| R4 | YOLO-n furniture identification on the phone (ADR-0011) | Not started |
| R5 | Optional `label` on `SceneObjectModel` (additive schema change) | Not started |
| R6 | Export for Unity: whole-room `.glb` and the export folder | Not started |
| R7 | Send to Computer: delivery confirmation, remembered computer, Viewer liveness | Not started |
| R8 | Failure hardening and one guided scan flow | Not started |

The scanner has never been connected to the Viewer. Its S6 network test used a
Python listener.

### What `R1` brings in

`GhostMapDublinHacks` is this repository at `f5a7d30` plus 31 commits from
2026-10-03: the guided-scan UI overhaul, automatic room scan from ARKit planes,
wall sweep (ADR-0005), furniture surface detection and per-object `.glb` export
(ADR-0006), plane-label type guessing (ADR-0007, to be removed by `R4`),
peer-to-peer scoping (ADR-0008), UDP quick-send discovery (ADR-0009), and two
research spikes. Last recorded suites there: shared 202, viewer 548, scanner
577, all passing; unsigned iOS build succeeded. None of it has run on an iPhone.

## Last verified commit

None.

## Tests run

None yet.

## Known risks

From reading the code, not from a failed run:

- **Automatic walls depend on ARKit.** How many vertical planes ARKit reports in
  a real furnished room on the owner's iPhone is unknown. The sweep and walked
  fallbacks exist for this.
- **Standing in one spot means aiming far.** Floor-line error grows with
  distance; stand near the room centre (ADR-0005's error table).
- **Viewer liveness.** The Viewer has no heartbeat timeout, so a phone that
  drops off Wi-Fi without closing the socket leaves it on `Connected` (`R7`).
- **No delivery confirmation.** The hackathon Send to Computer cannot tell the
  phone the room arrived; protocol v1 has no Viewer-to-scanner message (`R7`).
- **UDP discovery can be blocked** by some networks. Test on Wi-Fi and on the
  iPhone hotspot (`R2`).
- **Detector licence.** YOLO-n is AGPL-3.0 (ADR-0011).

## Next safe task

**`R1`**: merge `GhostMapDublinHacks/main` on an `integration/import-dublinhacks`
branch once the owner approves, then run all three suites.

## Do not touch

`apps/scanner/**` and `apps/viewer/**`, except on a short-lived, explicitly
scoped `integration/<task>` branch.

---

## R2 device log

| Date | Step | Result | Notes |
| --- | --- | --- | --- |
| — | — | — | — |

## R3 accuracy benchmark

Targets: median wall absolute error ≤ 0.12 m, max wall error ≤ 0.20 m, height
error ≤ 0.15 m.

| Scan | Wall A err | Wall B err | Wall C err | Wall D err | Height err | Door width err | Walls found automatically | Fallback used |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| — | — | — | — | — | — | — | — | — |
