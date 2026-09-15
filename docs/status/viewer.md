# Viewer Status

## Current state
- **V5 complete: object selection, editing, and measurement**
  (implementation plan section 17 Task V5; interaction rules in sections
  13.2-13.5).
- **`Runtime/Scene/ViewerEditableScene.cs` is the new ownership layer** the
  task brief asked for, sitting between the scanner-authoritative
  `ViewerSceneStore` (unchanged since V1) and every renderer/camera/
  interaction consumer:
  ```text
  ViewerSceneStore            (scanner authority; frozen)
      -> accepted scanner snapshots
  ViewerEditableScene          (V5)
      -> the effective scene: live pre-finalization, locally edited after
  RoomRenderer / OrbitCameraController / selection & edit controllers
  ```
  Both producers implement a new `Runtime/Scene/IViewerSceneSource`
  interface (`Current` + `Changed`), so `RoomRenderer.Attach` and
  `OrbitCameraController.Attach` now take that interface instead of the
  concrete `ViewerSceneStore` — a source-compatible, non-breaking change:
  every V1-V4 test that calls `renderer.Attach(store)` with a raw
  `ViewerSceneStore` still compiles and passes unchanged, because
  `ViewerSceneStore` implements the interface too.
- **Ownership rule, exactly `ADR-0003`:** while `finalized == false`,
  `ViewerEditableScene` is a pure pass-through of the scanner's live
  snapshots and `EditingEnabled` is `false`. The instant a snapshot with
  `finalized == true` arrives for the tracked session, `EditingEnabled`
  becomes `true` and `ViewerEditableScene` stops accepting further
  scanner-originated updates *for that same session* — protecting local
  edits from a duplicate/reconnect resend of the same finalized revision
  (defense in depth: `ViewerSceneStore`'s own revision arbitration already
  filters those out before they would even reach `ViewerEditableScene`). A
  **different** `sessionId` always wins immediately and unconditionally,
  discarding any local edits from the room it replaces — a Scanner Reset
  can never silently merge with the prior room's edits. All of this is
  proved against the *real* `ViewerSceneStore` pipeline (not a fake) in
  `ViewerEditableSceneTests`, covering the full A-K regression sequence the
  task brief specifies.
- **Local edits are validated by the same shared `FurnitureValidator`** the
  scanner's own snapshots are checked against
  (`ViewerEditableScene.TryApplyLocalEdit`) — never a Viewer
  reimplementation of the dimension/finite-value rules. Every accepted edit
  clones the current snapshot via `JsonUtility` round-trip (the same
  serializer the wire protocol uses), replaces exactly the edited object,
  and bumps the viewer-owned `revision` by one.
- **Selection (`Runtime/Interaction/ObjectSelectionController.cs`)** is a
  real `Physics.Raycast` against the room's real colliders. Only a hit
  whose collider carries a `SceneObjectBinding` — the furniture root's
  single bounding-box collider from V4 — counts; floor/ceiling/wall
  colliders have none, so they can never become an accidental furniture
  selection. Identity comes from `SceneObjectBinding.ObjectId`, never
  `GameObject` name parsing. Selection works at any time, before or after
  finalization (section 13.2 has no finalized gate); only editing does.
  Re-resolves after every `RoomRenderer.Rebuilt` (a new event, fired at the
  end of every rebuild) rather than racing `IViewerSceneSource.Changed`
  directly, and clears safely if the selected id no longer exists in the
  newly rendered room — whether because the authoritative scene dropped it
  pre-finalization or a new session replaced the room entirely.
- **Selection highlight** is a deterministic 12-edge wireframe box
  (`Runtime/Interaction/SelectionOutlineBuilder.cs`, pure and unit-tested)
  built from thin procedural cuboids — the same "primitive cubes" technique
  `FurnitureFactory`/`WallRenderer` already use, not a `LineRenderer` path
  or a third-party outline package. Parented under the selected object's
  root, so it inherits yaw/position for free and needs no per-frame upkeep;
  its own colliders are stripped so the highlight itself is never
  raycast-hittable.
- **Editing (`Runtime/Interaction/ObjectEditController.cs`)** exposes
  `TryDragToFloorPoint`, `TrySetPositionXZ`, `TrySetYaw`, `TrySetWidth`,
  `TrySetDepth`, `TrySetHeight` — every one takes explicit values (a `Ray`
  or a number), never reads `Input` itself, so the whole surface is
  EditMode-testable without a Play-mode loop (the same split V4's
  `OrbitCameraRig`/`OrbitCameraController` established). Drag intersects the
  cursor ray with the y = 0 floor plane using the shared `RayPlaneMath`
  (Ghost space is Viewer world space; no ARKit/XR transform), updates X/Z
  only, and always preserves the object's existing `center.y`. Every method
  is gated on `ViewerEditableScene.EditingEnabled` and fails cleanly
  (leaving the model untouched) with no selection, no finalized scan, or an
  invalid value.
- **Measurement (`Runtime/Interaction/MeasurementController.cs`)** places
  two points from real `Physics.Raycast` hits — floor, walls, furniture.
  Openings behave naturally as holes because `WallSliceGenerator` (V3)
  never builds a solid segment across one; no Viewer collider changes were
  needed. Distance always comes from the shared `MeasurementMath.Distance`/
  `.DistanceXZ` over the two real world-space hit points, never from
  screen-space pixels. A third click after a completed measurement starts a
  fresh one (`TryPlacePoint`'s documented lifecycle). A genuinely new
  Scanner session clears an in-progress/completed measurement (it would
  otherwise reference a room that no longer exists); edits and rebuilds
  *within* the same session never touch it.
- **`Runtime/Interaction/ViewerInteractionRouter.cs`** is the single place
  that reads the mouse each frame and resolves the "camera + interaction
  conflicts" the task brief calls out: a click that starts over UI
  (`EventSystem.IsPointerOverGameObject`) is ignored entirely; while
  measurement mode is active every click places a measurement point and
  never selects furniture ("measurement mode intercepts click placement");
  a mouse-down on the already-selected object's own collider (editing
  enabled, not measuring) begins a floor-plane drag and suspends
  `OrbitCameraController.InputEnabled` — a new V5 flag — for the duration,
  so a drag that starts on the selection never also orbits the camera
  underneath it; otherwise a genuine click (movement under a small pixel
  threshold — `ViewerInteractionRouter.IsClick`, the one pure/tested piece)
  selects whatever is under the cursor or clears the selection. `M` toggles
  measurement mode, mirroring V4's key-plus-button pattern for `F`/`D`.
  Deliberately thin and untestable in EditMode for the same reason
  `OrbitCameraController.Update()` is (V4 known issue): every command it
  dispatches to is covered directly by its own controller's tests with
  explicit rays.
- **`Runtime/UI/InspectorPanelController.cs`** shows the selected object's
  type/id, editing-enabled state, and editable position X/Z, yaw, width,
  depth, height as legacy UGUI `InputField`s, plus the measurement toggle/
  clear buttons and the live distance readout. Repaints from the live
  selected model every frame — never overwriting a field the user is
  actively typing into (`InputField.isFocused`) — so a value entered
  elsewhere (a drag, or the field itself) is always reflected without an
  extra event-wiring layer. A rejected submission leaves the model
  untouched and the next repaint snaps the field back to the real value.
- **`ViewerHudController`** now reads `bootstrap.EditableScene.Current` —
  the effective scene — rather than the raw `ViewerSceneStore.Current`, and
  prints an explicit `Editing: ENABLED` / `Editing: disabled (finalize scan
  to edit)` line plus the new click/drag/measure control summary.
- **`ViewerBootstrap`** now owns the one `ViewerEditableScene` and wires
  every new controller to it in `Awake()`; `OnDestroy()` detaches all of
  them. `ViewerSceneBuilder.BuildScene`/`.VerifyScene` build and assert the
  full new wiring: `ObjectSelectionController`, `ObjectEditController`,
  `MeasurementController`, `ViewerInteractionRouter`,
  `InspectorPanelController` and its Position X/Z, Yaw, Width, Depth,
  Height fields, the Measure/Clear Measurement buttons, and the
  measurement/selected-object/editing-status text.
- **V4 complete: parametric furniture and the orbit/dollhouse camera.**
  `Runtime/Rendering/FurnitureFactory.cs` builds each `SceneObjectModel` from
  primitive boxes per implementation plan section 12.4 ("do not render only
  anonymous boxes"): bed = headboard/frame/mattress, desk = top/4 legs/back
  panel, chair = seat/back/4 legs, couch = base/back/2 arms/2 cushions,
  table = top/4 legs, dresser = body/3 drawer fronts, tv = screen/stand/base,
  generic = one box. `BuildParts` is a pure function returning parts in the
  object's own local frame (origin at its centre on the floor, +x width, +y up,
  +z depth); `Create` is the thin Unity layer, with exactly the signature
  Task V4 specifies.
- **Every type's parts sum to exactly the declared W x D x H box, resting on the
  floor.** That invariant is asserted for all eight types and is what makes
  "correct dimensions" structural rather than incidental. Yaw and world
  placement are applied once, on the object root, so a rotated object's
  footprint is correct by construction.
- **One logical root per object**, named `Object_<type>_<id>` (Task V4: "named
  with type + ID"), carrying a `SceneObjectBinding` metadata component and
  **one** `BoxCollider` covering the full bounding box — section 12.4's "every
  furniture root has one collider"; the primitives' own colliders are removed
  so V5 selects objects, never chair legs.
- **Object types come only from `FurnitureValidator.SupportedTypes`.** An
  unsupported type renders nothing and is diagnosed; the Viewer never invents a
  semantic type outside the shared schema.
- **Furniture live-update is the same single rebuild V2/V3 already rely on.**
  `RoomRenderer` destroys and rebuilds the whole `RenderedRoom` per accepted
  snapshot, so removed objects disappear, new ones appear, changed
  position/yaw/dimensions update, nothing accumulates, and duplicate or stale
  revisions — already filtered by `ViewerSceneStore` — never reach the renderer.
  `FurnitureRenderer` is deliberately stateless; there is no diffing logic that
  could drift.
- **`Runtime/Interaction/OrbitCameraRig.cs` holds all the camera maths** as a
  plain C# class — no `MonoBehaviour`, no `UnityEngine.Input` — so every clamp
  is exhaustively testable in EditMode. Pitch is clamped to `[5, 89]` degrees
  and the orbit target to the floor plane, which together make section 13.1's
  "do not let camera go below floor" a structural guarantee rather than a
  runtime check. Zoom is multiplicative and clamped to `0.5-60 m`, so it can
  never reach or pass through the target.
- **Control scheme is exactly section 13.1**: left drag orbits, right or middle
  drag pans, scroll zooms, `F` frames the whole room, `D` toggles the dollhouse
  preset. On-screen **Reset View (F)** and **Dollhouse (D)** buttons mirror the
  keys, and the HUD prints the scheme.
- **`F` is both "frame whole room" and the reset/home view** — it restores the
  home yaw/pitch as well as re-centring and re-fitting, which makes reset
  completely deterministic.
- **Framing follows the actual room.** `Runtime/Rendering/RoomBounds.cs`
  computes bounds from the room's own corners and captured height, never a
  hard-coded 4 x 3 m footprint, and the fit uses the room's bounding *sphere*
  so the room stays framed at **every** orbit angle rather than only the one it
  was framed from. Small, large and rotated rooms all frame usably.
- **Only the first accepted scene auto-frames.** Later snapshots refresh the
  bounds but leave the camera alone, so a snapshot arriving mid-inspection
  never yanks the view out of the user's hands.
- **Dollhouse hides the ceiling renderer and collider** (section 12.2), and the
  hidden state is sticky across rebuilds — an incoming snapshot must not drop a
  ceiling back on top of the user mid-inspection.
- The camera listens to `ViewerSceneStore.Changed`, never to the TCP server —
  the same separation `RoomRenderer` has held to since V2.
- **V3 complete: doors and windows are rendered as real openings in the walls.**
  `Runtime/Rendering/WallSliceGenerator.cs` is the whole of V3's geometry
  logic — a pure static function
  (`BuildSlices(wallLengthM, wallHeightM, openings)`, exactly the signature
  implementation plan section 17 Task V3 specifies) implementing the grid-cut
  segmentation from section 12.3: horizontal cuts at `0`, wall length and each
  opening's near/far edge; vertical cuts at `0`, room height and each
  opening's sill/head; every cell between adjacent cuts kept unless its centre
  lies inside an opening. **No runtime CSG** — mesh booleans are fragile,
  platform-dependent and expensive to re-run per snapshot, and the plan calls
  for the deterministic decomposition instead. The slices tile the solid part
  of the wall exactly once: no overlaps, no gaps, no duplicate pieces, proved
  by a 61x61 interior-sample coverage assertion rather than by an area total
  alone.
- **An opening is bound to its wall by the ordered corner pair.** `offsetM` is
  measured from `wallStartCornerId`, so `WallRenderer` matches
  `(wallStartCornerId, wallEndCornerId)` against
  `(WallDefinition.StartCornerId, .EndCornerId)` in order. The reversed pair
  deliberately does **not** match — accepting it would silently mirror the
  opening to the far end of the wall — and is reported as a diagnostic instead.
- **A wall is now a container, not a cuboid.** The hierarchy is
  `Walls/Wall_<i>_<startId>_<endId>/Segment_<j>`; each segment is a
  `GameObject.CreatePrimitive(Cube)` and therefore keeps its `BoxCollider` for
  V5's measurement raycasts. A wall with no openings has exactly one segment
  whose world transform is byte-for-byte V2's full-wall transform, so V2
  rendering is preserved exactly where there is nothing to cut.
- **Rendering fails safe on bad opening data.** `ViewerSceneStore` already
  rejects a snapshot whose openings fail `OpeningValidator`, so the previously
  rendered room simply stays on screen. If malformed data reaches the renderer
  by some other route, the offending opening is dropped (null, non-finite,
  unsupported type, degenerate, off the end of the wall, negative sill, taller
  than the wall, or overlapping one already accepted), that wall renders solid,
  and the reason is surfaced through `Debug.LogWarning` rather than swallowed.
- **Partial-scan behaviour is unchanged from V2.** Before openings exist walls
  are solid; a snapshot with room geometry and zero openings renders exactly as
  V2 rendered it; each newly accepted opening during the Scanner's
  `AddOpenings` phase appears on the next `ViewerSceneStore.Changed`.
- **Note on `valid-room-v1.json`**: that fixture contains one door
  (`door-1`, wall `c0->c1`), so under V3 it correctly renders a doorway plus
  three solid walls. V2 rendered it fully solid only because V2 ignored
  openings entirely.
- **V2 complete: floor, ceiling, and solid walls rendered from the accepted scene.**
  `Runtime/Rendering/FloorCeilingRenderer.cs` and `Runtime/Rendering/WallRenderer.cs`
  are pure functions (`RoomModel` in, `Mesh`/`WallRenderSpec` out — no
  `MonoBehaviour`, fully unit-testable); `Runtime/Rendering/RoomRenderer.cs` is
  the `MonoBehaviour` that owns the actual GameObjects and is the only thing
  that listens to `ViewerSceneStore.Changed`.
- **Coordinates are rendered exactly as the shared `SceneSnapshot` supplies
  them.** No ARKit transform, no `CameraYOffset` compensation, no Scanner XR
  Origin transform, no extra origin normalization — Ghost space *is* the
  Viewer's Unity world space, by design (implementation plan section 17,
  Task V2).
- **Floor and ceiling** are triangulated by a fan from corner 0
  (`FloorCeilingRenderer.TryBuildFloorMesh` / `TryBuildCeilingMesh`), which is
  only correct for a convex polygon; `RoomValidator`'s 35-145 degree
  interior-angle rule (enforced once all four MVP corners exist) makes every
  closed room convex by construction, so this is a documented reliance, not an
  unchecked assumption. Winding is corrected per triangle against an explicit
  desired normal (`Vector3.up` for the floor, `Vector3.down` for the ceiling)
  by swapping index order only — vertex positions are never negated, so a
  wrong input winding can never come out as a mirrored footprint. Verified
  against both an axis-aligned fixture room and an independently-constructed
  37-degree-rotated room in tests.
- **Walls** are one solid cuboid per consecutive corner pair, from the shared
  package's own `RoomGeometry.BuildWalls` (never a Viewer reimplementation of
  wall derivation): centered on the wall's floor-to-ceiling midpoint, oriented
  with `Quaternion.FromToRotation(Vector3.right, wall.Tangent)`, thickness
  `0.10 m` per implementation plan section 12.3. Door/window cutting is V3's
  scope; V2's walls are always solid, including for the fixture that has a
  door and a window.
- **Live update is a pure function of `ViewerSceneStore.Changed`.**
  `RoomRenderer` never touches the TCP server or a raw socket message. Every
  rebuild destroys the previous `RenderedRoom` GameObject before creating the
  next one, so: a duplicate/stale revision (already filtered by
  `ViewerSceneStore` before `Changed` ever fires) never touches the renderer
  at all; a disconnect (which never touches the store) leaves the last
  rendered room untouched with zero extra bookkeeping; nothing can
  accumulate, because there is never more than one `RenderedRoom` alive.
- **Partial/degenerate scans render safely.** Floor needs `corners.Length >= 3`
  (a 3-corner partial scan gets a preview triangle); ceiling and walls
  additionally need `heightM > 0`. Zero, one, or two corners, or a height of
  `0` (its default before S4), produce an empty `Floor`/`Ceiling`/`Walls`
  container rather than a crash or invalid geometry.
- Hierarchy per implementation plan section 17 exactly:
  `RenderedRoom/{Floor, Ceiling, Walls/{Wall_0_<startId>_<endId>, ...}, Objects}`
  — `Objects` is an empty placeholder for V4.
- Added `com.unity.modules.physics` to `Packages/manifest.json` (was missing):
  required for `BoxCollider`/`MeshCollider`, which the implementation plan's
  V2 requirements ("colliders" on floor/ceiling/walls, floor collider for
  measurement raycasts) need and V1 never exercised.
- The Viewer scene now has a `RoomCamera` (`ViewerSceneBuilder.CreateRoomCamera`)
  — a fixed overview position framed for the ~4m x 3m fixture rooms, solid
  background, no orbit/dollhouse controls (that is V4's scope) — and a
  `RoomRenderer` GameObject wired into `ViewerBootstrap`'s new
  `roomRenderer` field, assigned in `ViewerSceneBuilder.BuildScene` and
  asserted by `VerifyScene`.
- **V1 complete: Viewer project, TCP server, and fixture ingestion.**
  `apps/viewer/` is now a real Unity `6000.3.24f1` project — `Packages/manifest.json`
  (shared local package, `com.unity.ugui`, `com.unity.test-framework`, and the
  legacy UI/IMGUI/jsonserialize modules), `ProjectSettings`, and
  `Assets/GhostMap/Viewer/`.
- `ViewerTcpServer` (`Runtime/Networking/`) listens on protocol v1's port
  `47831`, accepts one scanner connection at a time (a new connection always
  replaces the old one), and parses every newline-delimited message with the
  shared `ProtocolSerializer` — never a viewer-side reimplementation of the
  wire format. Reading uses a custom `LineReader`, not `StreamReader.ReadLine`,
  so a line over the 262144-byte cap is rejected without ever buffering
  unbounded memory, and framing correctly resyncs at the next newline so one
  bad line doesn't take down the connection.
- `ViewerSceneStore` (`Runtime/Scene/`) is the single place that decides
  whether an incoming `SceneSnapshot` replaces what's displayed: revision
  arbitration runs through the shared `SnapshotRevisionPolicy` (never
  reimplemented), and a snapshot must additionally pass `RoomValidator`,
  `OpeningValidator` (per opening) and `FurnitureValidator` (per object)
  before it is applied. Nothing in this type has a path that clears `Current`
  other than accepting a newer, valid snapshot — a network disconnect cannot
  touch it.
- `ViewerSession` (`Runtime/Bootstrap/`) is the plain-C# session state
  machine — deliberately not a MonoBehaviour, mirroring
  `GhostMap.Scanner.Networking.ScannerNetworkClient`'s own reasoning — that
  owns one `ViewerTcpServer` and one `ViewerSceneStore`, dispatches
  `hello`/`scene.snapshot`/`heartbeat`/`scan.finalized` messages via `Pump()`,
  and exposes `LoadFixture(path)` for the developer "Load fixture" button.
  `ViewerBootstrap` is the thin `MonoBehaviour` wrapper that owns one
  `ViewerSession` and ticks it every frame; `ViewerHudController`
  (`Runtime/UI/`) renders connection/session/revision/phase/finalized
  diagnostics from it and wires the Load Fixture button.
- `Editor/ViewerSceneBuilder.cs` builds `Assets/GhostMap/Viewer/Viewer.unity`
  from scratch (`GhostMap/Build Viewer Scene` menu item) and `VerifyScene()`
  asserts every component is present and wired, mirroring
  `GhostMap.Scanner.Editor.ScannerSceneBuilder`.
- `Runtime/Interaction/` and `Runtime/Persistence/` remain empty placeholders
  (V5/V6 own them). `Runtime/Rendering/` is now populated (V2).

## Last verified commit
- V5 implementation (this branch). Previous: `4ed9c67` (V4), `79d49c0` (V3),
  `197d4bd` (V2).

## Tests run
- Command:
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
    -batchmode -nographics -projectPath apps/viewer \
    -runTests -testPlatform EditMode -testResults <out>.xml -logFile <out>.log
  ```
- Result: **430 tests, 430 passed, 0 failed, 0 skipped.** Unity exit code `0`.
  (156 embedded `GhostMap.Shared.Tests`, unchanged; 210 V1-V4 Viewer tests,
  unchanged; **64 new V5 tests**: 17 `ViewerEditableSceneTests` (the full
  A-K ownership/regression sequence, plus per-field edits and validation
  rejection), 5 `SelectionOutlineBuilderTests`, 11
  `ObjectSelectionControllerTests`, 11 `ObjectEditControllerTests`, 16
  `MeasurementControllerTests`, 4 `ViewerInteractionRouterTests`.) The
  shared package's own `TestProject` was re-run standalone and is
  unaffected: **156 tests, 156 passed, 0 failed**, exit code `0` — no file
  under `shared/**` was touched.
- **Regression**: the complete Viewer suite was run, not only the new
  tests. V1 networking, V2 floor/ceiling/walls, V3 openings, V4 furniture
  and the orbit camera all pass unchanged.
- The **complete** Viewer suite was run for V4, not only the new tests: V1
  networking, V2 floor/ceiling/walls, V3 openings, reconnect behaviour and
  revision arbitration all pass unchanged, and two V4 tests assert the V2/V3
  shell explicitly alongside the new furniture.
- **Mutation check.** All 87 new V4 tests passed on their first execution
  against the implementation, so the central dimension invariant was verified
  to actually bite: shrinking the bed's headboard to half height was caught by
  four independent tests before being reverted.
- **V4 visual verification**: the real `RoomRenderer`, `FurnitureFactory`,
  `OrbitCameraController` and `OrbitCameraRig` were driven through
  `FixtureLoader` + `ViewerSceneStore` and rendered offscreen at 1600x900.
  `room-with-door-window-v1` in dollhouse mode shows the bed, desk and chair
  resting on the floor inside the room with V3's doorway and window intact;
  orbiting 70 degrees swings cleanly and resolves the desk's legs and the
  chair's back and legs individually; zooming visibly closes in; an
  eight-object room renders every MVP type distinctly (the tv correctly a thin
  slab); two identical desks at yaw 0 and yaw 90 read unambiguously as
  rotated 90 degrees; and an 11 x 9 m room and a 2.4 x 2.2 m room both frame to
  roughly the same on-screen size. Full detail in the V4 handoff.
- `ViewerSceneBuilder.BuildScene` / `.VerifyScene` re-verified end to end;
  `VerifyScene` now also asserts the `OrbitCameraController`, its
  `roomRenderer` wiring, `ViewerBootstrap.cameraController` and the HUD's two
  new buttons.
- Two V2 tests were deliberately updated for V3's wall-container hierarchy:
  `ValidRoomFixtureProducesExpectedGeometry` now reads segment transforms and
  expects the fixture's door wall to be segmented, and
  `DoorWindowFixtureStillRendersFourSolidWalls` — which asserted precisely the
  behaviour V3 exists to replace — became
  `DoorWindowFixtureRendersFourWallsMadeOfSolidSegments`.
- **V3 visual verification**: same throwaway-EditMode-test technique as V2
  (real production renderers, real fixture through `FixtureLoader` and
  `ViewerSceneStore`, offscreen `RenderTexture`, `-batchmode` without
  `-nographics`). From the scene's own saved `RoomCamera` transform,
  `room-with-door-window-v1.json` shows **both** openings — the door as a
  notch in wall `c0->c1` revealing the floor, the window as a slot in wall
  `c1->c2`. Interior captures confirm a clean full-height doorway with an
  intact header, a window with a **sill segment beneath it** (hole starts at
  0.9 m, ends at 2.0 m, wall continues to 2.5 m) and a header above, the two
  opening-free walls fully solid meeting at a clean corner, and no cracks or
  overlap seams anywhere. `valid-room-v1.json` shows its one doorway with the
  other three walls solid. Full detail in the V3 handoff.
- Real defect found by the V3 tests — **in the tests, not the
  implementation**: the first `AssertExactCoverage` sampled on a grid whose
  points landed exactly on cut lines, where adjacent slices legitimately share
  an edge, producing six spurious "covered 2 times" failures. The independent
  overlap and area assertions passing throughout is what identified it as a
  test artifact. Fixed by skipping samples within `1e-3` of any cut line and
  asserting a minimum interior sample count so the check cannot silently
  degrade.
- Scene build: `GhostMap.Viewer.Editor.ViewerSceneBuilder.BuildScene` and
  `.VerifyScene` were both re-verified end-to-end — via a throwaway EditMode
  test (`Assert.DoesNotThrow(() => ViewerSceneBuilder.BuildScene())` /
  `.VerifyScene()`, not committed) run through `-runTests`, **not**
  `-executeMethod` (see "Known issues": `-executeMethod` hangs in this
  sandbox). `VerifyScene` now also asserts a `Camera`, a `RoomRenderer`, and
  `ViewerBootstrap.roomRenderer` being wired.
- **Visual verification**: a throwaway EditMode test (not committed) built
  the real `RoomRenderer`/`FloorCeilingRenderer`/`WallRenderer` production
  code with the saved scene's actual `RoomCamera` transform, applied a real
  fixture via `FixtureLoader`, rendered offscreen to a `RenderTexture`, and
  wrote a PNG. `valid-room-v1.json` shows a beige box (four walls, exterior
  faces — the ceiling's downward normal makes it invisible from outside/above
  by design) with a visible brown floor rectangle inside;
  `room-with-door-window-v1.json` renders identically solid, confirming
  openings are correctly ignored (V3's scope) rather than crashing or
  partially rendering. See the V2 handoff for the images and the camera-angle
  bug this caught.
- **Outside the test runner**, the real `tools/send_fixture.py` was run
  against a real `ViewerSession` listening on `127.0.0.1:47831` (via a
  throwaway `-executeMethod`, not committed): the Python sender's process
  exited 0, and the Viewer's `ViewerSceneStore.Current` correctly held
  `sessionId=fixture-valid-room-v1`, `revision=12`, `finalized=true`,
  `4 corners / 1 opening / 3 objects` — exactly `fixtures/valid-room-v1.json`'s
  contents — confirming the real Python fixture sender and the real
  `ViewerTcpServer` interoperate over an actual TCP socket, not just the
  in-process `FakeScannerClient` test harness. (V1 result; not re-run for V2,
  which does not touch networking.)
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.
- Test-driven within V1: `ViewerTcpServer`'s bounded-line framing was
  exercised first with a hand-built oversized line and found to have a real
  off-by-one bug (the oversized check only ran *between* reads, so a final
  read chunk carrying both the overflow bytes and the terminating newline
  together skipped it) before the fix landed; see `LineReader.TryReadLine`.
- Real bug found and fixed during V2: the first draft of the geometry
  assertions in `RoomRendererTests` used
  `transform.GetComponent<MeshFilter>()?.sharedMesh` to assert "no mesh
  attached." A missing `GetComponent<T>()` result is a Unity "fake null" —
  its overridden `==` treats it as null, but the null-conditional operator's
  raw reference check does not, so it threw `MissingComponentException`
  instead of short-circuiting. Fixed by an explicit `!= null` check
  (`RoomRendererTests.HasMesh`); this is a test-code pitfall, not a defect in
  `RoomRenderer` itself, but worth flagging for whoever writes V3's opening
  tests against the same pattern.

## Interfaces consumed
- From `com.ghostmap.shared`: `SceneSnapshot`, `RoomModel`, `OpeningModel`,
  `SceneObjectModel` (`Domain`); `ProtocolConstants`, `WireMessageHeader`,
  `HelloMessage`, `HeartbeatMessage`, `SceneSnapshotMessage`,
  `ScanFinalizedMessage`, `ProtocolSerializer`, `SnapshotRevisionPolicy`,
  `SnapshotAcceptance` (`Protocol`); `RoomValidator`, `OpeningValidator`,
  `FurnitureValidator` (`Validation`).

## Interfaces published
- `GhostMap.Viewer.Networking.ViewerTcpServer` — `Start()`, `Stop()`,
  `TryDequeueMessage(out object)`, `State`, `RemoteEndpoint`, `LastError`.
- `GhostMap.Viewer.Scene.ViewerSceneStore` — `Current`, `Changed`,
  `TryApplyScannerSnapshot(SceneSnapshot, out string)`.
- `GhostMap.Viewer.Scene.FixtureLoader` — `TryLoadFromFile`, `TryLoadFromJson`.
- `GhostMap.Viewer.Bootstrap.ViewerSession` — the plain-C# session driving the
  two above; `Pump()`, `LoadFixture(string)`.
- `GhostMap.Viewer.Bootstrap.ViewerBootstrap` — the `MonoBehaviour` wrapper;
  `Session` property.
- `GhostMap.Viewer.UI.ViewerHudController` — diagnostics/Load-fixture HUD.
- `GhostMap.Viewer.Rendering.FloorCeilingRenderer` — static;
  `TryBuildFloorMesh(RoomModel, out Mesh)`, `TryBuildCeilingMesh(RoomModel, out Mesh)`.
- `GhostMap.Viewer.Rendering.WallSliceGenerator` — static;
  `BuildSlices(float wallLengthM, float wallHeightM, IReadOnlyList<OpeningModel>)`
  and an overload taking `IList<string> diagnostics`;
  `MinSliceExtentM` constant (`1e-4`). `WallSlice` (`MinU`, `MaxU`, `MinV`,
  `MaxV`, `WidthM`, `HeightM`, `CenterU`, `CenterV`) is the wall-local solid
  rectangle it returns.
- `GhostMap.Viewer.Rendering.WallRenderer` — static;
  `BuildWalls(RoomModel)` and `BuildWalls(RoomModel, IList<string> diagnostics)`
  `-> IReadOnlyList<WallRenderSpec>`;
  `WallRenderSpec` (`StartCornerId`, `EndCornerId`, `Position`, `Rotation`,
  `Scale`, `LengthM`, `Segments`);
  `WallSegmentSpec` (`Slice`, `Position`, `Rotation`, `Scale`);
  `WallThicknessM` constant (`0.10`).
- `GhostMap.Viewer.Rendering.RoomRenderer` — `MonoBehaviour`;
  `Attach(IViewerSceneSource)`, `Detach()`, `Root`, `CeilingVisible`,
  `SetCeilingVisible(bool)`, `event Rebuilt` (V5: fires after every rebuild,
  once the new hierarchy exists).
- `GhostMap.Viewer.Rendering.FurnitureFactory` — `IDisposable`; static
  `BuildParts(SceneObjectModel[, IList<string>])`;
  `Create(SceneObjectModel, Transform[, IList<string>])`.
  `FurniturePartSpec` (`Name`, `LocalCenter`, `LocalSize`).
- `GhostMap.Viewer.Rendering.FurnitureRenderer` — static;
  `Populate(RoomModel, Transform, FurnitureFactory, IList<string>) -> int`.
- `GhostMap.Viewer.Rendering.SceneObjectBinding` — `MonoBehaviour`; `Model`,
  `ObjectId`, `ObjectType`, `Bind(SceneObjectModel)`.
- `GhostMap.Viewer.Rendering.RoomBounds` — static;
  `TryCompute(RoomModel, out Bounds)`.
- `GhostMap.Viewer.Interaction.OrbitCameraRig` — plain C#; `Target`,
  `Distance`, `YawDeg`, `PitchDeg`, `FloorY`, `Position`, `Rotation`,
  `IsValid`; `Orbit`, `Zoom`, `Pan`, `Frame`, `Dollhouse`; constants
  `MinPitchDeg`/`MaxPitchDeg`/`MinDistanceM`/`MaxDistanceM`/`HomeYawDeg`/
  `HomePitchDeg`/`DollhousePitchDeg`.
- `GhostMap.Viewer.Interaction.OrbitCameraController` — `MonoBehaviour`;
  `Rig`, `DollhouseEnabled`, `InputEnabled` (V5: suspends orbit/pan/zoom/
  keys while a furniture drag is in progress), `Attach(IViewerSceneSource)`,
  `Detach()`, `SetRoomRenderer(RoomRenderer)`, `FrameRoom()`,
  `SetDollhouse(bool)`, `ToggleDollhouse()`, `ApplyToTransform()`.
- `GhostMap.Viewer.Scene.IViewerSceneSource` — **new**, V5. `Current`,
  `event Changed`. Implemented by both `ViewerSceneStore` and
  `ViewerEditableScene`.
- `GhostMap.Viewer.Scene.ViewerEditableScene` — **new**, V5. `Current`,
  `EditingEnabled`, `event Changed`, `Attach(IViewerSceneSource)`,
  `Detach()`, `TryApplyLocalEdit(SceneObjectModel, out string)`.
- `GhostMap.Viewer.Interaction.SelectionOutlineBuilder` — **new**, V5;
  static, pure. `BuildEdges(widthM, depthM, heightM[, thicknessM,
  marginM]) -> SelectionEdgeBar[12]`; `DefaultThicknessM`, `DefaultMarginM`.
- `GhostMap.Viewer.Interaction.ObjectSelectionController` — **new**, V5;
  `MonoBehaviour`. `SelectedObjectId`, `HasSelection`, `event
  SelectionChanged`, `SetRoomRenderer`, `SetCamera`, `Attach()`, `Detach()`,
  `TrySelectAt(Ray|Vector2)`, `IsPointerOverSelected(Ray)`,
  `ClearSelection()`, `GetSelectedModel()`.
- `GhostMap.Viewer.Interaction.ObjectEditController` — **new**, V5;
  `MonoBehaviour`. `EditingEnabled`, `SetSelectionController`,
  `Attach(ViewerEditableScene)`, `Detach()`, `TryDragToFloorPoint(Ray, out
  string)`, `TrySetPositionXZ`, `TrySetYaw`, `TrySetWidth`, `TrySetDepth`,
  `TrySetHeight` (all `(float, out string) -> bool`).
- `GhostMap.Viewer.Interaction.MeasurementController` — **new**, V5;
  `MonoBehaviour`. `IsActive`, `PointA`, `PointB`, `HasMeasurement`,
  `DistanceM`, `DistanceXZM`, `VerticalDistanceM`, `event Changed`,
  `Attach(IViewerSceneSource)`, `Detach()`, `SetActive(bool)`,
  `ToggleActive()`, `TryPlacePoint(Ray)`, `Clear()`.
- `GhostMap.Viewer.Interaction.ViewerInteractionRouter` — **new**, V5;
  `MonoBehaviour`. Wires camera/selection/edit/measurement/orbit together
  and reads the mouse each frame; `IsClick(Vector2, Vector2, float)` is the
  one pure/tested piece.
- `GhostMap.Viewer.UI.InspectorPanelController` — **new**, V5;
  `MonoBehaviour`. Wires the selected-object/editing-status text, the six
  numeric `InputField`s, and the measurement toggle/clear buttons.

## Known issues
- None blocking V6. `Runtime/Persistence/` is still empty — that is V6's
  scope, not a defect.
- **In dollhouse mode the near wall still occludes furniture standing against
  it.** Only the ceiling is hidden, which is exactly what section 12.2
  specifies; orbiting or raising the pitch reveals them. Wall fading is not
  requested by the plan and was not added.
- **Framing is deliberately conservative**: fitting the room's bounding sphere
  leaves visible screen margin for a wide, flat room. That is the price of the
  room staying framed at every orbit angle; a tighter box-projection fit would
  let corners clip when the user orbits.
- **Mouse sensitivity is not hardware-calibrated.** The orbit/pan/zoom
  constants are reasonable `[SerializeField]` defaults, but this sandbox has no
  Play-mode session to tune them by feel. Expect one tuning pass the first time
  someone drives the Viewer interactively. The `Update()` input path is the one
  part of V4 EditMode cannot execute; every command it dispatches to is covered.
- **`room-with-door-window-v1.json`'s bed overhangs the room by ~1.5 cm**
  (`bed-1`, centre x = 1.0, yaw 90, depth 2.03, crossing the `x = 0` wall
  centreline). That is the fixture's own data — Shared/Integration-owned, not a
  rendering defect — so the corresponding test allows one wall thickness of
  slack.
- Furniture materials are one shared `Unlit/Color` per type per
  `FurnitureFactory`, disposed with the factory. Flat unlit shading means
  objects read by silhouette and colour rather than by shading.
- `Resources.GetBuiltinResource` logs an editor assert in batchmode, so a test
  that calls `ViewerSceneBuilder.BuildScene` must set
  `LogAssert.ignoreFailingMessages`. Not a build failure and it predates V4.
- **A window seen from outside is hard to read** with the current flat
  `Unlit/Color` materials: through the hole you see the opposite wall in the
  same colour. The geometry is correct (proved by the interior captures and by
  the coverage/area tests); this is purely a shading limitation, and lit or
  two-tone materials are not in V3's scope.
- **Openings have no reveals/jambs.** The cut goes straight through the
  `0.10 m` wall thickness with square edges. Section 12.3 does not ask for
  returns.
- The grid decomposition is **not minimal** — one door yields five segments
  where three rectangles would do. That is exactly what the plan specifies, it
  is deterministic, and the extra draw calls are irrelevant at MVP room sizes.
  Do not collapse it into a merge pass without a reason.
- `WallSliceGenerator`'s own overlap rule is true rectangle overlap, so two
  openings sharing a horizontal span but with disjoint vertical spans are both
  cut. `OpeningValidator` is **stricter** (any horizontal overlap on the same
  wall is rejected) and runs first, so that case cannot arrive over the
  network. The generator is a rendering failsafe, never a second source of
  truth for validation.
- **Sandbox environment finding, not a code defect**: `-executeMethod` batch
  runs hang indefinitely in this sandbox's headless Unity at the
  `Start Indexing on Editor startup` step (confirmed twice; killed after
  26+ minutes and 100+ seconds with zero further log output despite 100%
  CPU). `-runTests` does not trigger that subsystem and completes in
  seconds — use it (wrapping the target method in a throwaway EditMode test)
  instead of `-executeMethod` for any future CLI batch work here. Also,
  `-nographics` disables the real graphics device and `Camera.Render()`
  segfaults under it (`NoGraphicsMain()` in the crash stack); rendering/
  screenshot work needs `-batchmode` without `-nographics` on a machine with
  an active GUI session. Full detail in the V2 handoff.
- The "Load fixture" button resolves its fixture path relative to the repo
  root via `Application.dataPath`, which only works for in-Editor development
  (as V1 explicitly targets); a standalone player build would need a
  different fixture-location strategy, which is not this task's concern.
- No physical-device testing applies to this workstream (desktop app).
- Wall cuboids are butt-jointed at each corner (no mitered corner geometry).
  For a `0.10 m` thickness this is a cosmetic detail invisible at the MVP's
  minimum wall length (`0.50 m`) and interior-angle range (35-145 degrees);
  V2's requirements do not ask for mitered corners and this does not affect
  wall length, position, or the floor/ceiling footprint.
- Wall/floor/ceiling materials are a single shared `Unlit/Color` instance per
  surface type, created lazily per `RoomRenderer` instance and disposed in
  `OnDestroy`. Fine for one on-screen room; would need pooling if V4+ ever
  renders many rooms at once, which is not currently planned.
- **Every accepted edit rebuilds the whole `RenderedRoom`.** Task V5's brief
  explicitly allows "rerender only object if easy, otherwise rebuild scene";
  V5 takes the latter, exactly as V2-V4 already do for every other scene
  change. Fine at MVP room sizes; a dedicated single-object rerender path is
  not implemented and is not currently planned.
- **No undo/redo, no delete/add object.** The V5 task brief does not require
  either, and the implementation plan's section 17 Task V5 description does
  not ask for them either — scope was not expanded to add them.
- **The selection highlight can be visually subtle from directly above in
  dollhouse mode**, since only 2 of its 12 edges face the camera at a steep
  overhead angle; orbiting reveals the full wireframe box clearly. See the
  V5 handoff's visual captures.
- **`EditingIsDisabledBeforeFinalization`-style tests aside, `ObjectEditController`
  does not itself re-check `SceneObjectModel.id` ownership beyond what
  `ViewerEditableScene.TryApplyLocalEdit` already verifies** (the id must
  exist in the current room). This is intentional — id existence is the only
  ownership check the schema defines — not a gap.
- Only `MinDimensionM`/`MaxDimensionM` from the shared `FurnitureValidator`
  gate width/depth/height; there is no separate Viewer-side clamp or
  rounding, so an out-of-range typed value is rejected outright (the field
  snaps back to the last valid value) rather than silently clamped.
- Carried over from V2-V4, unchanged: `-executeMethod` hangs in this sandbox
  (use `-runTests`); `-nographics` segfaults on `Camera.Render()` — V5's own
  visual verification needed a run without `-nographics`, same as V2-V4; a
  `BoxCollider`'s effective size/position after a `Transform.localScale`/
  `.position` change made *outside* Play mode is not guaranteed visible to
  `Physics.Raycast` until `Physics.SyncTransforms()` is called — encountered
  and fixed in `MeasurementControllerTests`' own ad hoc test colliders, not
  in any production code path (production colliders are always moved via a
  `SceneObjectBinding` root's `Transform.position`/`BoxCollider.size`
  together, which was already proven raycast-safe by every
  `ObjectSelectionControllerTests` case).
- No physical-device testing applies to this workstream (desktop app).

## Next safe task
- **V6 — Persistence and a polished Viewer HUD** (implementation plan
  section 17, Task V6). Save/load the locally-edited scene, and clean up the
  HUD now that selection/editing/measurement are live. Do not begin
  Integration from here.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
  `docs/decisions/**` — owned by the Shared/Integration workstream.
- `apps/scanner/**` — owned by the Scanner workstream.
