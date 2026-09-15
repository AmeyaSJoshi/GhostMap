# Viewer Status

## Current state
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
- Not yet committed — see the V2 handoff for the working-tree state this
  status reflects.

## Tests run
- Command:
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
    -batchmode -nographics -projectPath apps/viewer \
    -runTests -testPlatform EditMode -testResults <out>.xml -logFile <out>.log
  ```
- Result: **220 tests, 220 passed, 0 failed, 0 skipped.** Unity exit code `0`.
  (156 are the embedded `GhostMap.Shared.Tests` suite, unchanged; 25 are the
  V1 Viewer tests, unchanged; 39 are new V2 rendering tests:
  16 `FloorCeilingRendererTests`, 14 `WallRendererTests`,
  13 `RoomRendererTests`.)
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
- `GhostMap.Viewer.Rendering.WallRenderer` — static;
  `BuildWalls(RoomModel) -> IReadOnlyList<WallRenderSpec>`;
  `WallRenderSpec` (`StartCornerId`, `EndCornerId`, `Position`, `Rotation`,
  `Scale`, `LengthM`); `WallThicknessM` constant (`0.10`).
- `GhostMap.Viewer.Rendering.RoomRenderer` — `MonoBehaviour`;
  `Attach(ViewerSceneStore)`, `Detach()`, `Root` property.

## Known issues
- None blocking V3. `Runtime/Interaction/` and `Runtime/Persistence/` are
  still empty — that is V5/V6's scope, not a defect.
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
- **V3 — Wall openings.** `Rendering/WallSliceGenerator.cs` implements the
  grid-cut segmentation from implementation plan section 12.3 as a pure
  function (`BuildSlices(wallLength, wallHeight, openings) -> IReadOnlyList<WallSlice>`)
  and `WallRenderer`/`RoomRenderer` render each slice instead of one solid
  cuboid per wall, skipping slices whose center falls inside a door/window.
  The `room-with-door-window-v1` fixture is the one to render a visible
  doorway and window hole in.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
  `docs/decisions/**` — owned by the Shared/Integration workstream.
- `apps/scanner/**` — owned by the Scanner workstream.
