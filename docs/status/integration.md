# Integration Status

Workstream: Shared / Integration. Tracks the stand-in-place roadmap, tasks
`R1`-`R10` in plan section 18 (direction: ADR-0012 and ADR-0013).

## Current state

**`R1` done: merged, and all three Unity suites pass in this repository (shared 202, viewer 548, scanner 577, run on the owner's Mac on 2026-10-11). Next: `R2` on the iPhone; `R8` can start in parallel.**

| Task | What | State |
| --- | --- | --- |
| R1 | Bring `GhostMapDublinHacks` into this repository | **Merged** (owner-approved, 2026-10-11). Suites confirmed here 2026-10-11 at `69af8eb`: 202 / 548 / 577, all passing |
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

`69af8eb` (main after PR #29): shared 202, viewer 548, scanner 577, all passing (EditMode, owner's Mac). Nothing since R1 has run on an iPhone.

## Tests run

| Date | Where | Suites | Result |
| --- | --- | --- | --- |
| 2026-10-11 | Agent cloud container | shared, viewer, scanner | **Not run.** The container has no Unity and its network policy blocks Unity's download and licensing hosts (`download.unity3d.com`, `public-cdn.cloud.unity3d.com`, `unity.com`); Unity also needs the owner's licence. Added GitHub Actions CI instead |
| 2026-10-11 | Agent cloud container | Python tools job (`inspect_snapshot.py` on the three fixtures, `send_fixture.py --dry-run`, `bash -n tools/run_unity_tests.sh`) | Passed |
| 2026-10-11 | GitHub Actions, run 1 (`7859878`) | tools; Unity shared, viewer, scanner | tools **passed**. All three Unity jobs **stopped at "Unity licence secrets are missing"**, as designed: the secrets are not set yet. Unity never started |
| 2026-10-11 | GitHub Actions, run 5 on `main` (`04550b9`, merge of PR #25) | tools; Unity shared, viewer, scanner | Same as run 1: tools **passed**, Unity jobs stopped at the missing-secrets check. Artifact upload now skips cleanly |
| 2026-10-11 | Owner's Mac, Unity 6000.3.24f1, `./tools/run_unity_tests.sh` at `69af8eb` | shared, viewer, scanner (EditMode) | **All passed: shared 202/202, viewer 548/548, scanner 577/577**, 0 failed, 0 skipped. First Unity run in this repository; confirms `R1` |
| 2026-10-11 | Owner's Mac, `69af8eb` | Python tools checks (`inspect_snapshot.py --quiet` on the three fixtures, `send_fixture.py --dry-run`, `bash -n tools/run_unity_tests.sh`) | Passed: the two valid fixtures valid, the malformed one rejected on the height rule alone |

The counts match those recorded in `GhostMapDublinHacks` at `0b7a106`. CI's Unity jobs still stop at the missing-secrets check, so for now the suites are confirmed only on the owner's Mac.

## Open decisions for the owner

1. **Unity or native Swift for the phone app: decided "Unity for now",
   revisit after `R2`** (owner, 2026-10-11). Reasoning in `docs/onboarding.md`,
   "Where Unity fits, and why"; rule in `AGENTS.md` rule 15. When `R2` is recorded, put the question back to the
   owner with the result.
2. **Unity licence secrets** for CI (`docs/ci.md`). Until they exist, Unity
   results come only from local runs on the owner's Mac (`./tools/run_unity_tests.sh`).
3. **Should furniture arrive in Unity already placed?** Each `objects/<id>.glb`
   is written at the origin with no node transform (checked in
   `GlbExporter.Build`), so dragged into Unity every piece lands at the room's
   origin; its position and yaw are only in `scene.json`. `room.html` places
   them from `scene.json` (`R8`). If the owner wants placed furniture in Unity,
   `R6` must change, for example by also writing a `room-with-furniture.glb` or
   giving each object node its translation and rotation. Decide before `R6`
   starts.
4. **20 stale remote branches.** Agent sessions cannot delete branches. From a
   clone:

   ```bash
   git push origin --delete docs/one-button-computer-transfer \
     feature/drag-drop-furniture feature/nl-asset-editing feature/peer-to-peer-transport \
     integration/furniture-detect-and-export integration/sweep-and-furniture \
     scanner/s1-unity-project-ar-smoke-test scanner/s2-floor-lock-coordinate-frame \
     scanner/s3-corner-capture-closure scanner/s4-room-height-capture \
     scanner/s5-openings-furniture scanner/s6-network-finalization scanner/sweep-wall-capture \
     shared/status-docs-refresh \
     viewer/v1-desktop-network-foundation viewer/v2-room-geometry viewer/v3-wall-openings \
     viewer/v4-objects-camera viewer/v5-edit-measure viewer/v6-persistence-hud
   ```

   Their work is either on `main` already or was dropped on purpose during the
   2026-10-10/11 cleanup; nothing on them is needed.

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

1. **Owner: `R2`**, the first device session on the iPhone
   (`docs/tasks/R2-first-device-session.md`). The Unity suites are green at
   `69af8eb`, so the build is ready to try.
2. **Agent, in parallel: `R8`** (`docs/tasks/R8-browser-viewer.md`), which
   needs neither Unity nor the iPhone; then the code parts of `R6` and `R4`.
   See `docs/tasks/README.md`.
3. **Owner, when convenient:** add `UNITY_LICENSE`, `UNITY_EMAIL`,
   `UNITY_PASSWORD` as repository secrets (`docs/ci.md`) so CI can run the
   Unity suites on every push. Until then, run `./tools/run_unity_tests.sh` on
   the Mac before merging Unity changes.

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
