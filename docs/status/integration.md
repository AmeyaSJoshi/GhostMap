# Integration Status

Workstream: Shared / Integration. Tracks the stand-in-place roadmap, tasks
`R1`-`R10` in plan section 18 (direction: ADR-0012 and ADR-0013).

## Current state

**`R1` merged; its Unity test run is still to do. CI (`.github/workflows/tests.yml`) is ready to run it once the owner adds three secrets (`docs/ci.md`). Then `R2`.**

| Task | What | State |
| --- | --- | --- |
| R1 | Bring `GhostMapDublinHacks` into this repository | **Merged** (owner-approved, 2026-10-11). Code is byte-identical to the hackathon tree that passed 202 / 548 / 577; re-run here pending |
| R2 | First device session: every capture path on a real iPhone | Not started |
| R3 | Accuracy benchmark against a tape measure | Not started |
| R4 | YOLO-n furniture identification on the phone (ADR-0011) | Not started |
| R5 | Optional `label` on `SceneObjectModel` (additive schema change) | Not started |
| R6 | The phone builds the export bundle (`room.html`, `room.glb`, objects, `scene.json`) and keeps saved scans | Not started |
| R7 | Send to Computer through the iOS share sheet | Not started |
| R8 | Browser viewer `room.html` (three.js, view and measure) | Not started |
| R9 | Failure hardening and one guided scan flow | Not started |
| R10 | Retire the Unity Viewer and the discovery code | Not started |

Nothing has gone from the phone to a computer yet. The S6 network test used a
Python listener.

### What `R1` brought in

`GhostMapDublinHacks` at `0b7a106`: the guided-scan UI overhaul and Simulator
demo mode, the automatic room scan, wall sweep (ADR-0005), furniture surface
detection and per-object `.glb` export (ADR-0006), plane-label type guessing
(ADR-0007, removed by `R4`), peer-to-peer scoping (ADR-0008), UDP quick-send
discovery (ADR-0009, superseded by ADR-0013, removed in `R10`), and two research
spikes now under `docs/research/`.

How it was merged:

- Code (`apps/`, `shared/`, `fixtures/`) merged without conflicts and is
  byte-identical to the hackathon tree apart from READMEs and removed
  placeholder files.
- Documentation conflicts were resolved in favour of this repository's docs;
  the status pages now describe the imported features.
- The hackathon's eight handoffs and older status text stay in git history
  (`0b7a106`), not in the working tree.
- `tools/run_unity_tests.sh` runs the scanner suite without `-buildTarget iOS`,
  since `ScannerIosPostBuild` is now guarded by `UNITY_IOS`.

## Last verified commit

None in this repository. Hackathon suites last passed at `0b7a106`.

## Tests run

| Date | Where | Suites | Result |
| --- | --- | --- | --- |
| 2026-10-11 | Agent cloud container | shared, viewer, scanner | **Not run.** The container has no Unity and its network policy blocks Unity's download and licensing hosts (`download.unity3d.com`, `public-cdn.cloud.unity3d.com`, `unity.com`); Unity also needs the owner's licence. Added GitHub Actions CI instead |
| 2026-10-11 | Agent cloud container | Python tools job (`inspect_snapshot.py` on the three fixtures, `send_fixture.py --dry-run`, `bash -n tools/run_unity_tests.sh`) | Passed |

No Unity count has been observed in this repository yet. Expected after `R1`:
shared 202, viewer 548, scanner 577.

## Known risks

From reading the code, not from a failed run:

- **Automatic walls depend on ARKit.** How many vertical planes ARKit reports in
  a real furnished room on the owner's iPhone is unknown. The sweep and walked
  fallbacks exist for this.
- **Standing in one spot means aiming far.** Floor-line error grows with
  distance; stand near the room centre (ADR-0005's error table).
- **Phone-side export cost.** Writing several `.glb` files and a ~1 MB
  `room.html` at the end of a scan has not been measured on an iPhone (`R6`).
- **AirDrop needs both devices' Bluetooth and Wi-Fi on** and receiving enabled;
  Save to Files is the fallback (`R7`).
- **Local `room.html` limits.** Browsers block a local page from reading files
  beside it, so all geometry must be embedded (`R8`).
- **Detector licence.** YOLO-n is AGPL-3.0 (ADR-0011).

## Next safe task

1. **Owner, once:** add `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` as
   repository secrets (`docs/ci.md`), then **Actions → tests → Run workflow**.
   An agent then records the three counts here from the run. Expected: shared
   202, viewer 548, scanner 577. Fallback: `./tools/run_unity_tests.sh` on the
   Mac.
2. Then **`R2`**: the first device session (`docs/tasks/R2-first-device-session.md`).
3. In parallel, an agent can start **`R8`** (`docs/tasks/R8-browser-viewer.md`),
   which needs neither Unity nor the iPhone, and the code parts of `R6` and
   `R4`. See `docs/tasks/README.md`.

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
