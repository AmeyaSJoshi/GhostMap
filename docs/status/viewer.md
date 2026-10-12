# Viewer Status

Workstream: Viewer. Owns `apps/web-viewer/**`, `apps/viewer/**` and this file.
Full task-by-task history (V1-V6 design notes, visual captures, test breakdowns):
`git show f5a7d30:docs/status/viewer.md`.

## Current state

**`R8` built: the browser viewer `room.html` (`apps/web-viewer/`).** A three.js
app built into one self-contained template, `dist/room-template.html` (643 KB,
no network access, CSP `default-src 'none'`). It reads the room the phone embeds
(contract `docs/contracts/export-bundle-v1.md` section 2) and offers orbit, pan,
zoom, Frame room (`F`), Dollhouse (`D`, hides the `ceiling` node), click or list
to select an object (type, optional label, W × D × H in m and cm, id, outline),
two-click Measure (`M`, 3D and along the floor, `Esc` cancels), a furniture list
and a room summary. Since `R11`, the selected object's panel shows the photo the
phone took of it, when there is one (`objectPhotos`, contract section 5). Since
`R5`, an object's detector label shows as "Detected as", and titles a `generic`
object ("Potted plant"). The Unity Viewer's edit clone keeps `label` too. View
and measure only. The phone does not fill it yet
(`R6`); until then `npm run demo` fills it from a fixture.

- Node tests: **48 pass** (`npm test`; 41 after `R8`, +5 for photos in `R11`, +2 for labels in `R5`). Placement is checked against a bed
  `.glb` written by the real C# exporter (now the shared `GlbWriter`), and a flipped yaw sign makes
  that test fail.
- Checked by an agent in headless Chrome 154 from `file://` (desktop 1280×800 and phone 390×844):
  the demo loads with the page as its only request and no console errors;
  select by click and from the list, Frame room, Dollhouse, Measure (bed top to
  chair seat 2.30 m) and the unfilled-template message all work. **Not yet
  opened by the owner in Safari, Firefox or Chrome by double-click**; that is the
  manual check left in the `R8` brief.

**Unity Viewer: V1-V6 complete, frozen by ADR-0013.** The phone now builds the
Unity files and `room.html` itself, so this desktop app is no longer part of
the product. Bug fixes only; it stays as the developer tool for watching a scan
live until roadmap task `R10` removes it.

| Task | What works |
| --- | --- |
| V1 | TCP server on 47831, one scanner at a time, line framing with the 262144-byte cap, revision arbitration, validation, "Load fixture" dev button |
| V2 | Floor and ceiling meshes, four 0.10 m walls with colliders, deterministic hierarchy |
| V3 | Doors and windows cut as real wall openings by grid segmentation (no CSG) |
| V4 | Parametric furniture for all 8 types (one bounding-box collider per object), orbit camera, `F` frame, `D` dollhouse |
| V5 | Select, drag on the floor, edit X/Z, yaw, W/D/H in the inspector, two-point measurement (3D and horizontal) |
| V6 | Save and Load of the finalized scene, HUD with connection, session, revision, phase and editing state |
| ADR-0006 (imported, `R1`) | **Export Assets** writes one `.glb` per furniture object to `Application.persistentDataPath/ghostmap-assets/` via `FurnitureAssetExporter`, using the shared `GlbWriter` |
| `R6` | The pure geometry moved to `shared/com.ghostmap.shared/Runtime/Export/`: `FurnitureParts` (part table, colours), `WallSliceGenerator`, `WallLayout` (was `WallRenderer`), `FootprintMesh`, `GlbWriter` (was `GlbExporter`), `RoomPalette`. `FurnitureFactory`, `WallRenderer` and `FloorCeilingRenderer` remain as thin wrappers, so rendering is unchanged. 69 tests moved with the code |
| ADR-0009 (imported, `R1`) | `ViewerDiscoveryResponder` answers UDP discovery on 47832. Superseded by ADR-0013; removed in `R10` |

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

- Viewer sources: `0b7a106` from `GhostMapDublinHacks`, merged in task `R1`;
  EditMode suite passes at `69af8eb`.
  V6 itself was `b9fa1f5` (PR #12).

## Tests run

Browser viewer (`apps/web-viewer`):

```bash
npm ci && npm test && npm run build
```

2026-10-11, owner's Mac, Node 24: **41 tests, 41 passed**; rebuild
byte-identical to the committed template. CI's `web-viewer` job runs the same.
After `R11` (2026-10-11): **46 tests, 46 passed**; headless Chrome showed an
embedded JPEG in the bed's panel and no photo for the desk, which had none.

Unity Viewer:

```bash
./tools/run_unity_tests.sh viewer
```

Last recorded result, in `GhostMapDublinHacks` at the quick-send commit:
**548 tests, 548 passed** (viewer plus 202 embedded shared). **Confirmed in this
repository on 2026-10-11 at `69af8eb`: 548/548**, 0 failed (owner's Mac).
**After `R6`: 569 tests, 569 passed** (277 viewer + 292 embedded shared; 69
viewer tests moved to the shared suite). A
standalone macOS build was verified once during V6.

## Interfaces consumed

From `com.ghostmap.shared`: `SceneSnapshot`, `RoomModel`, `OpeningModel`,
`SceneObjectModel`, `ProtocolConstants`, the wire message types,
`ProtocolSerializer`, `SnapshotRevisionPolicy`, `RayPlaneMath`,
`MeasurementMath`, `RoomGeometry`, `RoomValidator`, `OpeningValidator`,
`FurnitureValidator`.

## Known issues

**Browser viewer**
- The demo and Node tests still use the box stand-in (`scripts/standin-glb.mjs`)
  for `room.glb`. The real one now exists (`R6`); a phone-built bundle opened
  in headless Chrome renders it correctly, including a rotated room.
- Near walls can hide furniture standing against them from low angles, as in
  the Unity Viewer; the start view is high to avoid it.
- On a phone-width screen the panels sit below the 3D view, so the details of
  a selected object need a scroll.
- The browser pane in the Claude desktop app cannot open `file://` pages;
  agents check the page with headless Chrome (see the `R8` handoff).

**Unity Viewer**

None blocking `R1`.

**Behavior**
- Exported `.glb` files pass the Khronos validator (0 errors, warnings, hints)
  and import into Unity with glTFast 6.20.0 (`R6`). Not yet opened in Blender.
- **No liveness detection.** The server sets no read timeout and ignores
  heartbeats, so a phone that drops off Wi-Fi without closing the socket leaves
  the HUD on `Connected`. "Disconnected — displaying last snapshot." appears
  only after a clean close. Not worth fixing: the app is frozen.
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

1. **Owner:** `npm run demo` in `apps/web-viewer`, then double-click
   `demo/room.html` in Safari (and Firefox if installed) with Wi-Fi off, and
   check each tool. Record the result here.
2. After any viewer change: `npm run build`, then copy
   `dist/room-template.html` to
   `apps/scanner/Assets/GhostMap/Scanner/Resources/GhostMapRoomTemplate.html`
   (a scanner test fails while the copy is stale).
3. `R10`: remove the Unity Viewer once `room.html` passes the acceptance test.

See `docs/status/integration.md` for progress.

## Do not touch

`shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
`docs/decisions/**` (Shared/Integration) and `apps/scanner/**` (Scanner).
