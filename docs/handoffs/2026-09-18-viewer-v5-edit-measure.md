# Handoff

## Branch
`viewer/v5-edit-measure`

## Base commit
`44b22a8` (merge of PR #10, `viewer/v4-objects-camera` -> `main`; Scanner
S1-S6 and Viewer V1-V4 complete)

## Head commit
V5 implementation (this branch, pending commit at hand-off time)

## What changed
Task V5 from `docs/plans/ghostmap-implementation-plan.md` section 17: object
selection, editing, and measurement (behaviour specified in sections
13.2-13.5), plus `ADR-0003`'s finalized ownership transition.

### Ownership layer
- `Runtime/Scene/IViewerSceneSource.cs` — **new**. `Current` + `event
  Changed`, the shape every "current accepted scene" producer exposes.
  `ViewerSceneStore` now implements it (additive, non-breaking: its own
  public API is unchanged).
- `Runtime/Scene/ViewerEditableScene.cs` — **new**. The requested
  `ViewerSceneStore -> ViewerEditableScene -> RoomRenderer` layer. Pure
  pass-through with `EditingEnabled = false` before finalization; on the
  first `finalized = true` snapshot for the tracked session, takes over —
  `EditingEnabled = true` and further same-session scanner traffic is
  ignored (protects local edits from a duplicate/reconnect resend of the
  same finalized revision, and defensively from anything else per
  `ADR-0003`: the scanner is read-only/finished after finalization). A
  different `sessionId` always replaces `Current` unconditionally and
  resets `EditingEnabled`, discarding prior local edits. `TryApplyLocalEdit`
  is the only way to mutate `Current` once editing is enabled: it validates
  through the shared `FurnitureValidator`, clones via a `JsonUtility`
  round-trip (never a hand-maintained field-by-field clone), replaces
  exactly the edited object, and bumps the viewer-owned `revision`.

### Selection
- `Runtime/Interaction/SelectionOutlineBuilder.cs` — **new**. Pure,
  deterministic 12-edge wireframe-box geometry (thin procedural cuboids, the
  same technique `FurnitureFactory`/`WallRenderer` already use) in the
  object's own local frame.
- `Runtime/Interaction/ObjectSelectionController.cs` — **new**. Real
  `Physics.Raycast`; only a hit carrying `SceneObjectBinding` counts, so
  floor/wall/ceiling colliders can never become a furniture selection.
  Re-resolves on `RoomRenderer.Rebuilt` (new event), clearing safely if the
  id is gone. Builds/destroys the selection highlight (via
  `SelectionOutlineBuilder`) parented under the selected root.

### Editing
- `Runtime/Interaction/ObjectEditController.cs` — **new**. `TryDragToFloorPoint`
  (via the shared `RayPlaneMath` against the y = 0 plane — Ghost space is
  Viewer world space, no ARKit/XR transform), `TrySetPositionXZ`,
  `TrySetYaw`, `TrySetWidth`, `TrySetDepth`, `TrySetHeight`. Every method
  takes explicit values, gates on `ViewerEditableScene.EditingEnabled`, and
  fails cleanly with no selection or an invalid value.

### Measurement
- `Runtime/Interaction/MeasurementController.cs` — **new**. Two
  `Physics.Raycast` points; distance from the shared `MeasurementMath.Distance`/
  `.DistanceXZ`, never screen-space pixels. A third click after a complete
  measurement starts a fresh one. A genuinely new Scanner session clears an
  in-progress/completed measurement; same-session edits/rebuilds never do.

### Input coordination
- `Runtime/Interaction/ViewerInteractionRouter.cs` — **new**. Resolves
  "camera + interaction conflicts": UI clicks are ignored
  (`EventSystem.IsPointerOverGameObject`); measurement mode intercepts every
  click; a mouse-down on the already-selected object's own collider starts a
  floor-plane drag and suspends `OrbitCameraController.InputEnabled` (new
  V5 flag) for its duration; otherwise a genuine click (under a small pixel
  threshold — `IsClick`, the one pure/tested piece) selects or clears.
  `M` toggles measurement mode.
- `Runtime/Interaction/OrbitCameraController.cs` — `InputEnabled` flag
  added; `Attach` now takes `IViewerSceneSource`.

### Rendering/UI wiring
- `Runtime/Rendering/RoomRenderer.cs` — `Attach` now takes
  `IViewerSceneSource`; new `event Rebuilt`, fired at the end of every
  rebuild, after the new `Objects/Object_<type>_<id>` roots exist.
- `Runtime/UI/InspectorPanelController.cs` — **new**. Selected-object/
  editing-status text, six numeric `InputField`s (position X/Z, yaw, width,
  depth, height), measurement toggle/clear buttons and readout. Repaints
  from the live model every frame except a field the user is actively
  typing into.
- `Runtime/UI/ViewerHudController.cs` — reads `bootstrap.EditableScene.Current`
  (the effective scene) instead of the raw `ViewerSceneStore.Current`; new
  `Editing: ENABLED`/`disabled` line; control-scheme line extended with
  click/drag/measure.
- `Runtime/Bootstrap/ViewerBootstrap.cs` — owns the one
  `ViewerEditableScene`; wires `RoomRenderer`, `OrbitCameraController`,
  `ObjectSelectionController`, `ObjectEditController`, `MeasurementController`
  to it in `Awake()`; detaches all of them in `OnDestroy()`.
- `Editor/ViewerSceneBuilder.cs` — builds and wires every new component and
  UI element; `VerifyScene()` asserts all of it.
- `Runtime/GhostMap.Viewer.Runtime.asmdef` — added an explicit
  `UnityEngine.UI` reference (needed by `ViewerInteractionRouter`'s
  `EventSystem` check and `InspectorPanelController`'s UGUI types).

## Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**`, `docs/decisions/**` was touched, and no
`apps/scanner/**` file was touched. Every edit is validated by the existing
`FurnitureValidator` (never a Viewer reimplementation of the rules), no new
wire message was invented, and there is still no viewer-to-scanner scene
channel — `ADR-0003`/protocol v1 are unchanged. `SceneSnapshot.revision` is
the same field the scanner already owns during scanning; V5 only starts
incrementing it locally *after* the same field's `finalized` flip, per the
frozen schema's own documented semantics ("the viewer owns editable scene
state" after finalization).

## How to test
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/viewer \
  -runTests -testPlatform EditMode \
  -testResults /tmp/viewer-tests.xml -logFile /tmp/viewer-tests.log

/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/shared-tests.xml -logFile /tmp/shared-tests.log
```
(Omit `-nographics` for rendering/screenshot work — see "Known issues".)

## Test results
- **Viewer project: 430 tests, 430 passed, 0 failed, 0 skipped.** Unity exit
  code `0`. That is 156 embedded `GhostMap.Shared.Tests` (unchanged) + 210
  V1-V4 Viewer tests (unchanged) + **64 new V5 tests**:
  - `ViewerEditableSceneTests` (17): pre-finalization pass-through, editing
    disabled/enabled, every field edit (position/yaw/width/depth/height),
    invalid/non-finite dimension rejection, editing one object never
    touches another, revision increments, and the full **A-K** regression
    sequence — duplicate finalized resend, stale snapshot, and a genuinely
    new session, all driven against a *real* `ViewerSceneStore`.
  - `SelectionOutlineBuilderTests` (5): exactly 12 edges, positive sizes,
    deterministic, correct inflated-box bounds, floor placement.
  - `ObjectSelectionControllerTests` (11): correct id via
    `SceneObjectBinding` (including a deliberately weird id, proving no
    name parsing), single selection, empty-space and floor clicks clear
    safely, a removed object clears the selection, selection survives a
    non-removing rebuild, `IsPointerOverSelected` correctness.
  - `ObjectEditControllerTests` (11): disabled pre-finalization, enabled
    post-finalization, no-selection failure, position/yaw/width/depth/height
    updates, drag preserves Y, invalid/oversized dimensions rejected,
    editing one object never touches another.
  - `MeasurementControllerTests` (16): inactive placement no-op, first/second
    click states, horizontal/vertical/diagonal-3D distance (checked against
    `MeasurementMath` directly), zero-length safety, clear/deactivate reset,
    a third click starting fresh, markers/line reflect the real endpoints, a
    new session clears the measurement while a same-session edit does not.
  - `ViewerInteractionRouterTests` (4): the click/drag pixel threshold.
- **Shared `TestProject`: 156 tests, 156 passed, 0 failed.** Exit code `0`
  — confirms no `shared/**` regression, as expected since nothing there was
  touched.
- `ViewerSceneBuilder.BuildScene` and `.VerifyScene` re-verified end to end
  via a throwaway EditMode test (not committed), since `-executeMethod`
  still hangs in this sandbox. `VerifyScene` now also asserts
  `ObjectSelectionController`, `ObjectEditController`,
  `MeasurementController`, `ViewerInteractionRouter`,
  `InspectorPanelController` (and its 14 wired fields/buttons/text), and
  `ViewerBootstrap`'s three new references.
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.

## Edit ownership semantics
Exactly `ADR-0003`, implemented in `ViewerEditableScene`:

| State | Editing | Who can change `Current` |
| --- | --- | --- |
| `finalized == false` (any session) | disabled | scanner-originated snapshots only, applied as-is |
| First `finalized == true` snapshot for a session | *transition* | becomes `EditingEnabled = true` |
| `finalized == true`, same session | enabled | **only** `TryApplyLocalEdit` — further scanner traffic for this session is ignored |
| A different `sessionId` arrives | resets | replaces `Current` unconditionally; `EditingEnabled` reset from the incoming snapshot (normally `false`) |

Duplicate and stale resends of the *same session/revision* never even reach
`ViewerEditableScene` — `ViewerSceneStore`'s own revision arbitration
(frozen since V1) filters them out before its `Changed` fires — so the "same
finalized revision" protection above is defense in depth, proved directly
against the real pipeline in `ViewerEditableSceneTests`.

## Selection controls
- Click a furniture object (its single V4 bounding-box collider) to select
  it; click empty space (floor/wall/ceiling/nothing) to clear.
- Works before or after finalization — only editing is gated.
- Highlight: an amber 12-edge wireframe box parented on the selected root
  (survives camera motion and follows position/yaw edits for free).
- Only one object selected at a time; selecting a new object replaces the
  old selection.
- If the authoritative scene drops the selected object (pre-finalization
  live update, or a new session), the selection clears safely.

## Editing behaviour
- Enabled only when `snapshot.finalized == true` for the current session.
- Drag: click-and-hold the selected object, move the mouse — the object
  follows the cursor's floor-plane intersection (X/Z only; height is always
  preserved). Suspends camera orbit for the drag's duration.
- Inspector: Position X/Z, Yaw, Width, Depth, Height as numeric fields.
  Submitting an invalid value (e.g. negative width, or one outside
  `FurnitureValidator`'s `0.05-5.0 m`) is rejected and the field snaps back.
- Every accepted edit bumps the viewer-owned `revision` and rebuilds the
  room (the task brief's documented fallback: "otherwise rebuild scene").

## Measurement controls
- `M` or the on-screen **Measure** button toggles measurement mode.
- Click a first point, then a second, on floor/wall/furniture (openings
  behave naturally as holes — no collider changes were needed). A third
  click starts a fresh measurement.
- Shows 3D distance and horizontal (XZ) distance; a cyan line and two
  marker spheres mark the real world-space endpoints.
- **Clear Measurement** button resets without leaving measurement mode.
- Intercepts every click while active — never selects furniture underneath.

## Visual/interaction verification
Same throwaway-EditMode-test technique as V2-V4 (real production code,
`FixtureLoader` + real `ViewerSceneStore`/`ViewerEditableScene`, offscreen
`RenderTexture` at 1600x900, run *without* `-nographics`), driving
`room-with-door-window-v1.json` (bed yaw 90, desk yaw 0, chair yaw 180 — the
"at least one rotated object" case) end to end:

1. Applied a synthetic **non-finalized** snapshot first: confirmed
   `EditingEnabled == false` and that `ObjectEditController.TrySetWidth`
   is rejected even with an object selected.
2. Applied the finalized fixture (a new session): confirmed
   `EditingEnabled == true`, and that the old selection did not carry over.
3. Selected the bed via `TrySelectAt` — resolved to `bed-1` correctly.
4. Changed position (`TrySetPositionXZ`), yaw (`TrySetYaw` to 45°), and
   width (`TrySetWidth`) — confirmed each took effect on the live model.
5. Confirmed the desk (`desk-1`) was untouched by the bed's edits.
6. Orbited 70° and zoomed in via the real `OrbitCameraRig` — confirmed both
   moved the camera; selection highlight followed the bed through the
   rotate/resize and stayed correctly oriented in the diamond footprint.
7. Entered measurement mode, placed two floor points, confirmed
   `HasMeasurement`, a positive finite `DistanceM`, then cleared it.
8. Confirmed room geometry survived: 4 walls, the door wall (`c0_c1`) and
   window wall (`c1_c2`) both still segmented.

Captures (in `/tmp/ghostmap-v5-visual/`, not committed):
- **`v5-00-fixture-dollhouse-finalized`** — the finalized room, ceiling
  hidden, before any edit.
- **`v5-01-bed-selected-highlight`** — the bed selected; the amber wireframe
  is visible along its right/bottom edges from this near-overhead angle
  (full box visible once orbited, per known issues).
- **`v5-02-bed-edited-position-yaw-width`** — the bed moved, rotated 45°,
  and resized; the highlight box is clearly a rotated diamond that tracks
  it exactly, while the desk and chair are unchanged.
- **`v5-03-orbited-and-zoomed`** — confirms orbit/zoom work with an active
  selection and highlight.
- **`v5-04-measurement-floor-diagonal`** — the cyan measurement line and one
  endpoint marker visible mid-measurement.

## Known issues
- **Selection highlight is visually subtle from directly overhead** (only
  ~2 of 12 edges face the camera at a steep dollhouse angle); orbiting
  reveals the full wireframe clearly. Not a functional defect — the
  geometry is correct and unit-tested.
- **No undo/redo, no delete/add object** — not required by the V5 task
  brief or the implementation plan's Task V5 description; scope was not
  expanded to add them.
- **Every accepted edit rebuilds the whole `RenderedRoom`**, exactly the
  task brief's documented fallback ("rerender only object if easy,
  otherwise rebuild scene") and the same pattern V2-V4 already use for
  every other scene change. Fine at MVP room sizes.
- `Physics.Raycast` in EditMode does not reliably reflect a `Transform`
  position/scale change made outside Play mode until
  `Physics.SyncTransforms()` is called. Hit in `MeasurementControllerTests`'
  own ad hoc test colliders (fixed there); production code was never
  affected, since every real collider is moved via a `SceneObjectBinding`
  root's `Transform.position` together with `BoxCollider.size`/`.center` —
  already proven raycast-safe by all of `ObjectSelectionControllerTests`.
- Carried over from V2-V4, unchanged: `-executeMethod` hangs in this
  sandbox (use `-runTests`); `-nographics` segfaults on `Camera.Render()`;
  `Resources.GetBuiltinResource` logs an editor assert in batchmode, which a
  test calling `BuildScene` must ignore (not a build failure).
- No physical-device testing applies to this workstream (desktop app).

## Files most important to read next
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Scene/ViewerEditableScene.cs`
  — the ownership rule every future editing/persistence feature must respect.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Interaction/ObjectEditController.cs`
  — V6's save/load will serialize exactly `ViewerEditableScene.Current`.
- `docs/status/viewer.md` — full V5 design notes and known issues.

## Next task
- **V6 — Persistence and a polished Viewer HUD** (implementation plan
  section 17, Task V6). Not started. Do not begin Integration work from here.
