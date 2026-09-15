# Handoff

## Branch
`viewer/v2-room-geometry`

## Base commit
`b8e4a7f` (merge of PR #7, `viewer/v1-desktop-network-foundation` -> `main`; Scanner S1-S6 and Viewer V1 complete)

## Head commit
`<recorded after commit — see docs/status/viewer.md "Last verified commit">`

## What changed
Task V2 from `docs/plans/ghostmap-implementation-plan.md` section 17: floor,
ceiling, and wall rendering from the accepted `SceneSnapshot`.

- `Runtime/Rendering/FloorCeilingRenderer.cs` — static, pure functions
  (`RoomModel` in, `Mesh` out, no `MonoBehaviour`): `TryBuildFloorMesh` and
  `TryBuildCeilingMesh` fan-triangulate the footprint from corner 0. Winding
  is corrected per triangle against an explicit desired normal (`Vector3.up`
  for the floor, `Vector3.down` for the ceiling) by swapping index order
  only — vertex positions are never negated, so a wrong input winding can
  never come out as a mirrored footprint. Fan triangulation from a single
  apex is only valid for a convex polygon; `RoomValidator`'s 35-145 degree
  interior-angle rule (enforced once all four MVP corners exist) makes every
  closed room convex by construction, so this is a documented reliance, not
  an unchecked assumption — a partial (fewer than four corner) chain is
  always trivially convex regardless.
- `Runtime/Rendering/WallRenderer.cs` — static, pure function `BuildWalls`
  turning the shared package's own `RoomGeometry.BuildWalls` (never a Viewer
  reimplementation of wall derivation) into `WallRenderSpec` cuboid
  transforms: centered on the wall's floor-to-ceiling midpoint, oriented
  with `Quaternion.FromToRotation(Vector3.right, wall.Tangent)`, thickness
  `0.10 m` per implementation plan section 12.3. Solid until V3 cuts
  openings.
- `Runtime/Rendering/RoomRenderer.cs` — the only `MonoBehaviour` in this
  task; `Attach(ViewerSceneStore)` subscribes to `Changed` and does a full
  destroy-then-rebuild of a `RenderedRoom` GameObject
  (`Floor`/`Ceiling`/`Walls`/`Objects` children, matching the implementation
  plan's hierarchy exactly, `Objects` an empty placeholder for V4). Never
  touches the TCP server or a raw socket message, so live-update correctness
  (no accumulation, no duplication on a resend, stale snapshots never
  replace newer geometry, a disconnect never clears the room) all fall out
  of `ViewerSceneStore` already having done that filtering before `Changed`
  ever fires — `RoomRenderer` does not re-implement any of it.
- `ViewerBootstrap.cs` — added a `[SerializeField] RoomRenderer roomRenderer`
  field, attached in `Awake()` right after `Session.Start()`. This is the
  one line connecting rendering to the session; `ViewerSession` itself is
  untouched (AGENTS.md: the TCP server must not be responsible for creating
  Unity GameObjects).
- `Editor/ViewerSceneBuilder.cs` — added `CreateRoomCamera()` (a fixed
  overview position, not V4's orbit/dollhouse camera) and a `RoomRenderer`
  GameObject wired into `ViewerBootstrap.roomRenderer`; `VerifyScene()` now
  also asserts a `Camera` exists and `ViewerBootstrap.roomRenderer` is
  assigned.
- `Packages/manifest.json` / `packages-lock.json` — added
  `com.unity.modules.physics` (was missing; needed for
  `BoxCollider`/`MeshCollider`, which V2's "colliders" requirement and V1
  never exercised).
- `Tests/EditMode/FloorCeilingRendererTests.cs`,
  `WallRendererTests.cs`, `RoomRendererTests.cs` — 39 new tests (16, 14, 13).
- `docs/status/viewer.md` updated; this handoff created.

## Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no
`apps/scanner/**` file was touched. Coordinates are rendered exactly as the
shared `SceneSnapshot` supplies them — no ARKit transform, no
`CameraYOffset` compensation, no Scanner XR Origin transform, no extra
origin normalization, per this task's explicit instruction.

## How to test
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath apps/viewer \
  -runTests -testPlatform EditMode \
  -testResults /tmp/viewer-tests.xml -logFile /tmp/viewer-tests.log
```
(Omit `-nographics` if you also want to run rendering/screenshot tests —
see "Known issues" below.)

## Test results
- **220 tests, 220 passed, 0 failed, 0 skipped.** Unity exit code `0` (156
  unchanged `GhostMap.Shared.Tests` + 25 unchanged V1 Viewer tests + 39 new
  V2 rendering tests: 16 `FloorCeilingRendererTests`, 14 `WallRendererTests`,
  13 `RoomRendererTests`).
- `ViewerSceneBuilder.BuildScene` and `.VerifyScene` both re-verified
  end-to-end (see "Known issues" for why not via `-executeMethod` directly):
  `VerifyScene` now additionally confirms a `Camera` exists and
  `ViewerBootstrap.roomRenderer` is assigned.
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.
- Real bug found and fixed in test code during this task: the first draft of
  several `RoomRendererTests` assertions used
  `transform.GetComponent<MeshFilter>()?.sharedMesh` to assert "no mesh
  attached." A missing `GetComponent<T>()` result is a Unity "fake null" —
  its overridden `==` treats it as null, but the null-conditional operator's
  raw reference check does not, so it threw `MissingComponentException`
  instead of short-circuiting. Fixed with an explicit `!= null` check
  (`RoomRendererTests.HasMesh`).
- Real bug found and fixed in the camera setup: the first `RoomCamera`
  position (`(2, 6, -5)` looking at `(2, 1.25, 1.5)`) put the fixture room's
  front wall (`z = 0`) directly in the sightline at a height inside the
  wall's 0-2.5 m span, so the camera saw only the wall's exterior face — not
  the room. Fixed to `(2, 10, -3)` looking at `(2, 0, 1.5)`, a steeper
  overhead angle that clears the wall height where the sightline crosses
  `z = 0`. Caught by actually rendering and inspecting the screenshot (see
  "Visual verification"), not by the unit tests, which do not exercise
  camera framing.

## Visual verification
`-executeMethod` batch runs hang in this sandbox (see "Known issues"), so
there is no interactive Play-mode session to screenshot by pressing Play.
Instead, a throwaway EditMode test (not committed) built the same
production `RoomRenderer`/`FloorCeilingRenderer`/`WallRenderer` code with the
real `RoomCamera` transform from the saved scene, applied a real fixture via
`FixtureLoader`, rendered the camera to an offscreen `RenderTexture`, and
wrote a PNG for inspection.

- **`valid-room-v1.json`**: renders a beige box (the four walls, exterior
  faces, seen from the 3/4 overhead angle since the top is open — the
  ceiling's downward-facing normal makes it invisible from outside/above,
  which is expected default behavior, not a defect) with a visible brown
  floor rectangle inside. Confirms floor + all four walls are present,
  correctly positioned, and distinctly colored.
- **`room-with-door-window-v1.json`**: renders identically — a fully solid
  box with no visible cutouts, confirming V2 correctly ignores openings
  (that is V3's scope) rather than crashing or partially rendering.
- Ceiling placement/orientation is confirmed numerically by
  `FloorCeilingRendererTests` and `RoomRendererTests` (vertex y = `heightM`,
  triangles face down) rather than visually, since the ceiling is not in
  frame from an exterior viewpoint by design — the same reason implementation
  plan section 12.2 calls out "dollhouse mode hides ceiling" as a separate,
  later concern.

## Known issues
- **`-executeMethod` hangs in this sandbox's headless Unity.** Both
  `ViewerSceneBuilder.BuildScene` and `.VerifyScene`, run via
  `-batchmode -nographics -executeMethod ...`, hang indefinitely at Unity's
  Search-indexing startup step (`Start Indexing on Editor startup`) with 0%
  further log output despite 100% CPU — confirmed twice, killed after 26+
  minutes and 100+ seconds respectively with no progress. `-runTests` boots
  without triggering that subsystem and completes in seconds. Both
  `BuildScene`/`VerifyScene` were still exercised, end-to-end, via a
  throwaway EditMode test (not committed) that called them directly and
  asserted `Assert.DoesNotThrow`. If a future task needs `-executeMethod`
  from the CLI again, expect this and drive it through `-runTests` instead
  (or investigate disabling Unity Search indexing for batch runs).
- **`-nographics` disables the real graphics device**, and `Camera.Render()`
  segfaults under it (confirmed via crash log: `NoGraphicsMain()` in the
  stack). The visual-verification screenshot capture above was run without
  `-nographics` (this machine has an active GUI session); a fully headless
  CI runner would need a virtual display (e.g. Xvfb-equivalent) to render
  anything.
- `RoomCamera` is a single fixed overview position tuned for the ~4m x 3m
  fixture rooms, not general-purpose framing — V4's orbit/dollhouse camera
  supersedes it.
- Wall cuboids are butt-jointed at each corner (no mitered corner geometry).
  Invisible at the `0.10 m` thickness given the MVP's minimum wall length
  (`0.50 m`); not requested by V2's requirements.
- Wall/floor/ceiling materials are a single shared `Unlit/Color` instance
  per surface type per `RoomRenderer`, disposed in `OnDestroy`. Fine for one
  on-screen room; would need pooling only if a future task renders many
  rooms at once, which is not currently planned.
- No physical-device testing applies to this workstream (desktop app).

## Files most important to read next
- `docs/status/viewer.md` — full V2 design notes and known issues.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Rendering/RoomRenderer.cs` —
  the live-update architecture V3 (wall openings) and V4 (furniture) both
  build on: only `ViewerSceneStore.Changed` triggers a rebuild.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Rendering/WallRenderer.cs` —
  V3's `WallSliceGenerator` will sit next to this and this file's
  `WallRenderSpec` per-wall transform is what V3 slices instead of
  rendering as one solid cuboid.

## Next task
- **V3 — Wall openings** (implementation plan section 17, Task V3). Do not
  begin V4 or Integration work from here.
