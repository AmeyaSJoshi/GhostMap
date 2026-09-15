# Handoff

## Branch
`viewer/v4-objects-camera`

## Base commit
`95fdd4a` (merge of PR #9, `viewer/v3-wall-openings` -> `main`; Scanner S1-S6 and Viewer V1-V3 complete)

## Head commit
`4ed9c67` (V4 implementation)

## What changed
Task V4 from `docs/plans/ghostmap-implementation-plan.md` section 17: parametric
furniture and the orbit/dollhouse camera.

### Furniture
- `Runtime/Rendering/FurnitureFactory.cs` — **new**. `BuildParts(SceneObjectModel)`
  is a pure function returning the object's primitive boxes in its own local
  frame (origin at the object's centre on the floor, +x width, +y up, +z depth);
  `Create(SceneObjectModel, Transform)` is the thin Unity layer with exactly the
  signature the plan specifies. Every type's parts are fractions of the declared
  W/D/H, so **the union of a type's parts is exactly the declared box, resting on
  the floor** — asserted for all eight types. Part structure follows section 12.4
  (bed: headboard/frame/mattress; desk: top/4 legs/back panel; chair:
  seat/back/4 legs; couch: base/back/2 arms/2 cushions; table: top/4 legs;
  dresser: body/3 drawer fronts; tv: screen/stand/stand base; generic: one box).
- `Runtime/Rendering/SceneObjectBinding.cs` — **new**. The object metadata
  component Task V4 requires on every root; V5's selection raycast reads it.
- `Runtime/Rendering/FurnitureRenderer.cs` — **new**. Fills the `Objects`
  container from `room.objects`. Deliberately stateless: `RoomRenderer` already
  destroys and rebuilds the whole `RenderedRoom` per accepted snapshot, so "no
  accumulation", "removed objects disappear" and "a duplicate revision does not
  duplicate objects" fall out of that single rebuild instead of out of diffing
  logic that could drift.
- `Runtime/Rendering/RoomRenderer.cs` — builds `Objects/Object_<type>_<id>`;
  owns one `FurnitureFactory` and disposes it; gained
  `SetCeilingVisible(bool)` / `CeilingVisible`.

### Camera
- `Runtime/Interaction/OrbitCameraRig.cs` — **new**. All the camera maths and
  every clamp, as a plain C# class with no `MonoBehaviour` and no `Input`, so
  EditMode can drive it exhaustively. Orbit/zoom/pan/frame/dollhouse; pitch
  clamped to `[5, 89]` and the target clamped to the floor, which together make
  "do not let camera go below floor" (section 13.1) a structural guarantee
  rather than a runtime check.
- `Runtime/Interaction/OrbitCameraController.cs` — **new**. Reads the mouse and
  keyboard and pushes the rig onto the transform. Listens to
  `ViewerSceneStore.Changed`, never to the TCP server.
- `Runtime/Rendering/RoomBounds.cs` — **new**. `TryCompute(RoomModel, out Bounds)`
  from the room's own corners and captured height, so framing follows the actual
  room rather than a hard-coded 4 x 3 m fixture footprint.

### Wiring
- `ViewerBootstrap.cs` — attaches the camera controller to the session's scene
  store alongside the renderer.
- `Editor/ViewerSceneBuilder.cs` — `RoomCamera` now carries an
  `OrbitCameraController` wired to the `RoomRenderer`; `CreateLoadFixtureButton`
  generalised to `CreateActionButton`, adding **Reset View (F)** and
  **Dollhouse (D)** buttons; `VerifyScene` asserts all of it.
- `ViewerHudController.cs` — wires the two new buttons and prints the control
  scheme plus the dollhouse state.
- `Viewer.unity` — rebuilt by `ViewerSceneBuilder.BuildScene`.

## Camera control scheme
Exactly implementation plan section 13.1:

| Input | Action |
| --- | --- |
| Left drag | Orbit around the room centre |
| Right drag or middle drag | Pan the orbit target |
| Scroll wheel | Zoom (multiplicative, clamped to 0.5-60 m) |
| `F` / **Reset View** button | Frame the whole room — this is also the reset/home view |
| `D` / **Dollhouse** button | Toggle the dollhouse preset: angled overhead view, framed, ceiling hidden |

Design decisions worth knowing:
- **`F` is both "frame whole room" and reset/home.** It restores the home yaw/pitch
  as well as re-centring and re-fitting, which makes "reset" completely
  deterministic and satisfies both the plan's `F` and the task brief's
  reset/home requirement with one command.
- **Only the first accepted scene auto-frames.** Later snapshots refresh the
  bounds but leave the camera alone, so a snapshot arriving while the user is
  inspecting never yanks the view out of their hands.
- **Framing uses the room's bounding sphere**, not its box, so the room stays
  fully on screen at *every* orbit angle rather than only at the angle it was
  framed from.
- **Dollhouse hides the ceiling renderer *and* collider** (section 12.2), and the
  hidden state is sticky across rebuilds — an incoming snapshot must not drop a
  ceiling back on top of the user mid-inspection.

## Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no
`apps/scanner/**` file was touched. Object types come from
`FurnitureValidator.SupportedTypes` — the viewer never invents a type outside
the shared schema — and no serialized field was added. Coordinates are rendered
exactly as the snapshot supplies them: no ARKit transform, no `CameraYOffset`,
no XR Origin transform, no extra normalization. No new package dependency.

**No new fixture was created.** The task brief asked for "fixture/test scene
data containing multiple objects", but `fixtures/**` is Shared/Integration-owned.
`room-with-door-window-v1.json` already carries a bed (yaw 90), a desk (yaw 0)
and a chair (yaw 180), which covers the brief's "bed, desk, chair or couch, one
rotated object" requirement exactly; the all-eight-types and differently-sized
rooms are built as scene data inside the Viewer's own tests, which this
workstream does own.

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
- **Viewer project: 366 tests, 366 passed, 0 failed, 0 skipped.** Unity exit
  code `0`. That is 156 embedded `GhostMap.Shared.Tests` (unchanged) + 123
  V1/V2/V3 Viewer tests (unchanged) + **87 new V4 tests**:
  14 `FurnitureFactoryTests`, 18 `FurnitureRendererTests`,
  16 `RoomRendererObjectsTests`, 18 `OrbitCameraRigTests`,
  13 `OrbitCameraControllerTests`, 8 `RoomBoundsTests`.
- **Shared `TestProject`: 156 tests, 156 passed, 0 failed.** Exit code `0`.
- `ViewerSceneBuilder.BuildScene` and `.VerifyScene` both re-verified end to end
  via a throwaway EditMode test (not committed), since `-executeMethod` still
  hangs in this sandbox. `VerifyScene` now also asserts the
  `OrbitCameraController`, its `roomRenderer` wiring, `ViewerBootstrap.cameraController`,
  and the HUD's two new buttons.
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.

Every item the task brief required is covered:

| Required | Test |
| --- | --- |
| generic object correct center | `RootSitsAtTheModelCentre` |
| correct width / depth / height | `RenderedObjectMatchesTheDeclaredDimensions`, `EverySupportedTypeFillsExactlyTheDeclaredBox` |
| correct yaw | `RootCarriesTheModelYawAsRotationAboutY`, `YawRotatesTheRenderedFootprint` |
| one model = one logical object | `CreateProducesOneRootPerModel`, `OneObjectRendersOneLogicalRoot` |
| multiple objects independent | `PopulateRendersEveryObjectIndependently`, `MultipleObjectsRenderIndependently` |
| every MVP type renders | `EverySupportedTypeCreatesVisibleGeometry`, `EverySupportedTypeRendersInsideTheRoom` |
| invalid type fails safely | `UnsupportedTypeProducesNoPartsAndIsDiagnosed`, `UnsupportedTypeCreatesNothing`, `PopulateSkipsInvalidObjectsAndDiagnosesThem` |
| duplicate snapshot no duplicates | `DuplicateSnapshotDoesNotDuplicateObjects` |
| newer snapshot updates transform | `NewerSnapshotUpdatesAnObjectTransform` |
| removed object disappears | `RemovedObjectDisappears` |
| stale snapshot does not modify | `StaleSnapshotDoesNotModifyRenderedObjects` |
| reconnect no duplicates | `ReconnectWithIdenticalStateCreatesNoDuplicates`, `ObjectsNeverAccumulateAcrossManyRebuilds` |
| zero objects works | `ZeroObjectsRendersNoFurniture`, `PopulateWithZeroObjectsRendersNothingAndDoesNotThrow` |
| fixture data deterministic | `DoorWindowFixtureObjectsRenderDeterministically`, `BuildPartsIsDeterministic` |
| room bounds correct | all 8 `RoomBoundsTests` |
| orbit target uses room centre | `FrameTargetsTheRoomCentre`, `AcceptedSceneAutomaticallyFramesTheRoom` |
| reset/home frames room | `FrameIsDeterministicAndResetsTheViewingAngles`, `FrameRoomReFramesOnDemand` |
| zoom clamps safely | `ZoomClampsToASafeRange`, `ZoomNeverReachesZeroOrNegativeDistance` |
| no NaN/invalid transforms | `NoOperationEverProducesANaNTransform`, `NonFiniteBoundsAreRejectedWithoutCorruptingTheRig` |
| different room sizes usable | `FrameProducesUsableFramingForVeryDifferentRoomSizes`, `DifferentRoomSizesProduceDifferentFramingDistances` |

Regression: the full Viewer suite was run, not only the new tests. V1 networking,
V2 floor/ceiling/walls, V3 openings, reconnect behaviour and revision
arbitration all still pass unchanged, and
`FurnitureDoesNotDisturbTheV2AndV3RoomShell` plus
`DoorWindowFixtureRendersItsThreeObjectsAlongsideTheOpenings` assert the V2/V3
shell explicitly from the V4 tests.

**Mutation check.** Because all 87 new tests passed on their first execution
against the implementation, the dimension invariant was verified to actually
bite: shrinking the bed's headboard from full height to half height was caught
by four independent tests
(`EverySupportedTypeFillsExactlyTheDeclaredBox`, `PartsScaleWithTheDeclaredDimensions`,
`RenderedObjectMatchesTheDeclaredDimensions`, `DoorWindowFixtureObjectsRenderDeterministically`)
before the change was reverted.

## Visual verification
Captured with the same throwaway-EditMode-test technique as V2/V3, driving the
real `RoomRenderer`, `FurnitureFactory`, `OrbitCameraController` and
`OrbitCameraRig` through `FixtureLoader` + `ViewerSceneStore`, rendered offscreen
at 1600x900 (16:9, matching the rig's aspect assumption).

- **`02-fixture-dollhouse`** — `room-with-door-window-v1` in dollhouse mode:
  ceiling hidden, interior visible, blue bed against the left wall, brown desk,
  gold chair, all resting on the floor inside the room; V3's doorway notch and
  window hole both still correct.
- **`03-fixture-dollhouse-orbit`** — the same room orbited 70 degrees. The
  camera swings cleanly, the window opening reads clearly in the far wall, and
  the desk's legs and the chair's back/legs are individually visible, confirming
  the objects are not anonymous boxes.
- **`04-fixture-dollhouse-zoom`** — zoomed in from the dollhouse view; visibly
  closer, geometry intact.
- **`05-all-types-dollhouse`** — a 7 x 6 m room containing all eight MVP types.
  Each is distinct in footprint and colour: bed, couch, dresser, desk, table,
  tv (correctly a thin slab, shown rotated 270 degrees), chair, generic box.
- **`06-yaw-comparison`** — two identical 1.4 x 0.7 m desks, one at yaw 0 and
  one at yaw 90, seen near-overhead: one is wide in X, the other wide in Z.
  Unambiguous proof that yaw is applied correctly.
- **`07-large-room-home`** / **`08-small-room-home`** — an 11 x 9 m room and a
  2.4 x 2.2 m room, both framed by the same home view to roughly the same
  on-screen size, proving framing follows actual room bounds.
- **`01-fixture-home`** — the home view with the ceiling on, for comparison.

## Known issues
- **In dollhouse mode the near wall still occludes furniture standing against
  it.** Only the ceiling is hidden, which is exactly what section 12.2
  specifies; orbiting or raising the pitch reveals the hidden objects. Wall
  fading was not implemented because the plan does not ask for it.
- **Framing is deliberately conservative.** Fitting the room's bounding *sphere*
  leaves noticeable empty screen margin for a wide, flat room, because the
  sphere is larger than the box's on-screen projection. This is the price of
  the room staying fully framed at every orbit angle; tightening it to a
  box-projection fit would let corners clip when the user orbits.
- **`Input.GetAxis("Mouse X"/"Mouse Y")` sensitivity is not calibrated on
  hardware.** The orbit/pan constants are reasonable defaults and are
  `[SerializeField]`-exposed, but this workstream has no Play-mode session in
  this sandbox to tune them by feel. Expect one tuning pass when someone first
  drives the Viewer interactively.
- The `Update()` input path itself is the one part of V4 that EditMode cannot
  execute; every command it dispatches to is covered by
  `OrbitCameraControllerTests` and `OrbitCameraRigTests`.
- **The fixture's bed overhangs the room by ~1.5 cm.** `bed-1` (centre x = 1.0,
  yaw 90, depth 2.03) crosses the `x = 0` wall centreline. That is the fixture's
  own data, owned by the Shared/Integration workstream, not a rendering defect;
  `FixtureObjectsSitInsideTheRoomFootprint` therefore allows one wall thickness
  of slack rather than zero.
- Furniture materials are one shared `Unlit/Color` per type per
  `FurnitureFactory`, disposed with the factory. Flat unlit shading means
  objects read by silhouette and colour rather than by shading.
- Carried over from V2/V3, unchanged: `-executeMethod` hangs in this sandbox
  (use `-runTests`); `-nographics` segfaults on `Camera.Render()`;
  `Resources.GetBuiltinResource` logs an editor assert in batchmode, which a
  test calling `BuildScene` must ignore (not a build failure).
- No physical-device testing applies to this workstream (desktop app).

## Files most important to read next
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Interaction/OrbitCameraRig.cs` —
  V5's selection and drag interactions will need this camera's target and
  distance for screen-to-world maths.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Rendering/SceneObjectBinding.cs` —
  the hook V5's selection raycast reads off the object root's single collider.
- `docs/status/viewer.md` — full V4 design notes and known issues.

## Next task
- **V5 — Selection, editing and measurement** (implementation plan section 17,
  Task V5; behaviour specified in sections 13.2-13.5). Not started. Do not begin
  Integration work from here.
