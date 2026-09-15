# Viewer Status

## Current state
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
- V3 implementation (this branch). Previous: `197d4bd` (V2).

## Tests run
- Command:
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
    -batchmode -nographics -projectPath apps/viewer \
    -runTests -testPlatform EditMode -testResults <out>.xml -logFile <out>.log
  ```
- Result: **279 tests, 279 passed, 0 failed, 0 skipped.** Unity exit code `0`.
  (156 embedded `GhostMap.Shared.Tests`, unchanged; 64 V1/V2 Viewer tests; 59
  new V3 tests: 30 `WallSliceGeneratorTests`, 17 `WallRendererOpeningsTests`,
  12 `RoomRendererOpeningsTests`.) The shared package's own `TestProject` was
  also run standalone for V3: **156 tests, 156 passed, 0 failed**, exit code
  `0`.
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
  `Attach(ViewerSceneStore)`, `Detach()`, `Root` property.

## Known issues
- None blocking V4. `Runtime/Interaction/` and `Runtime/Persistence/` are
  still empty — that is V5/V6's scope, not a defect.
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
- `RoomCamera` is a single fixed overview position tuned for the ~4m x 3m
  fixture rooms, not a general-purpose framing for arbitrary room sizes —
  V4 introduces the real orbit/dollhouse camera, which supersedes it.
- Wall cuboids are butt-jointed at each corner (no mitered corner geometry).
  For a `0.10 m` thickness this is a cosmetic detail invisible at the MVP's
  minimum wall length (`0.50 m`) and interior-angle range (35-145 degrees);
  V2's requirements do not ask for mitered corners and this does not affect
  wall length, position, or the floor/ceiling footprint.
- Wall/floor/ceiling materials are a single shared `Unlit/Color` instance per
  surface type, created lazily per `RoomRenderer` instance and disposed in
  `OnDestroy`. Fine for one on-screen room; would need pooling if V4+ ever
  renders many rooms at once, which is not currently planned.

## Next safe task
- **V4 — Parametric furniture + orbit/dollhouse camera** (implementation plan
  section 17, Task V4). `Rendering/FurnitureFactory.cs` builds recognizable
  primitive-composed objects per section 12.4 (bed, desk, chair, couch, table,
  dresser, TV; `generic` may be a box), each root named by type + ID with one
  selection collider covering its bounding box; `Rendering/FurnitureRenderer.cs`
  populates the `Objects` placeholder `RoomRenderer` already creates;
  `Interaction/OrbitCameraController.cs` replaces the fixed `RoomCamera` with
  orbit/pan/zoom plus frame-room and a dollhouse preset that hides the ceiling
  (section 13.1).

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
  `docs/decisions/**` — owned by the Shared/Integration workstream.
- `apps/scanner/**` — owned by the Scanner workstream.
