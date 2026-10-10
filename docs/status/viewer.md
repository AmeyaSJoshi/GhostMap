# Viewer Status

Workstream: Viewer. Owns `apps/viewer/**` and this file.
Full task-by-task history (V1-V6 design notes, visual captures, test breakdowns):
[`docs/archive/status-history/viewer-through-2026-10-03.md`](../archive/status-history/viewer-through-2026-10-03.md).

## Current state

**V1-V6 complete.** The Viewer MVP is done. Desktop app, so no device test applies.

| Task | What works |
| --- | --- |
| V1 | TCP server on 47831, one scanner at a time, line framing with the 262144-byte cap, revision arbitration, validation, "Load fixture" dev button |
| V2 | Floor and ceiling meshes, four 0.10 m walls with colliders, deterministic hierarchy |
| V3 | Doors and windows cut as real wall openings by grid segmentation (no CSG) |
| V4 | Parametric furniture for all 8 types (one bounding-box collider per object), orbit camera, `F` frame, `D` dollhouse |
| V5 | Select, drag on the floor, edit X/Z, yaw, W/D/H in the inspector, two-point measurement (3D and horizontal) |
| V6 | Save and Load of the finalized scene, HUD with connection, session, revision, phase and editing state |

### Scene ownership layers

```text
ViewerTcpServer -> ViewerSession -> ViewerSceneStore        scanner authority, revision rules
                                        |
                                 ViewerEditableScene         pass-through until finalized,
                                        |                    then Viewer-owned and editable
          RoomRenderer, OrbitCameraController, selection, edit, measurement
```

Editing, Save and Load are gated on a finalized scene (ADR-0003). A new scanner
`sessionId` always replaces the displayed room, local edits included.

### Where the Viewer deliberately differs from the plan

| Plan says | Viewer does |
| --- | --- |
| Save to `persistentDataPath/GhostMap/Scenes/<sessionId>.json` (section 14) | One fixed slot, `persistentDataPath/ghostmap-scene.json`; each Save overwrites it |
| Rerender only the edited object if easy (V5) | Every accepted snapshot or edit rebuilds the whole room |
| Viewer UX script shows the LAN IP and live tracking (section 20) | HUD shows connection state and remote endpoint, not the Viewer's own IP; no tracking line because the scanner sends no `phone.pose` |

## Last verified commit

- Viewer sources: `b9fa1f5` (V6 persistence-authority fix), merged in PR #12.

## Tests run

```bash
./tools/run_unity_tests.sh viewer
```

Last recorded result at `b9fa1f5`: **475 tests, 475 passed** (319 viewer + 156
embedded shared). A standalone macOS build was verified once during V6.

## Interfaces consumed

From `com.ghostmap.shared`: `SceneSnapshot`, `RoomModel`, `OpeningModel`,
`SceneObjectModel`, `ProtocolConstants`, the wire message types,
`ProtocolSerializer`, `SnapshotRevisionPolicy`, `RayPlaneMath`,
`MeasurementMath`, `RoomGeometry`, `RoomValidator`, `OpeningValidator`,
`FurnitureValidator`.

## Known issues

None blocking `I1`.

**Behavior**
- **No liveness detection.** The server sets no read timeout and ignores
  heartbeats, so a phone that drops off Wi-Fi without closing the socket leaves
  the HUD on `Connected`. "Disconnected — displaying last snapshot." appears
  only after a clean close. Expect `I3`'s Wi-Fi-drop test to hit this.
- The Viewer cannot add, delete or hide objects, and has no undo. Walls and
  openings are not editable.
- Dollhouse hides only the ceiling; the near wall can hide furniture against it.
- Single save slot, no overwrite confirmation.
- "Load fixture" resolves `fixtures/` relative to the repo, so it only works in
  the Editor, not in a standalone build.
- Room area, volume and perimeter exist in `MeasurementMath` but are not shown.

**Visual**
- Flat `Unlit/Color` materials: windows are hard to read from outside, the
  selection outline is subtle from straight above.
- Openings have no jambs; wall corners are butt-jointed, not mitered.
- Mouse sensitivity is untuned; expect one tuning pass when someone drives it.

**Tooling**
- There is no Viewer scene test, so a green suite does not prove `Viewer.unity`
  matches `ViewerSceneBuilder`. Rebuild the scene after changing the builder.
- In headless Unity, `-executeMethod` can hang at "Start Indexing"; use
  `-runTests`. `-nographics` crashes `Camera.Render()`; visual captures need a
  GUI session.
- `room-with-door-window-v1.json`'s bed overhangs a wall by ~1.5 cm (fixture
  data, not a renderer bug).

## Next safe task

None in this workstream. Next is Integration `I1` (see
`docs/status/integration.md`).

## Do not touch

`shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
`docs/decisions/**` (Shared/Integration) and `apps/scanner/**` (Scanner).
