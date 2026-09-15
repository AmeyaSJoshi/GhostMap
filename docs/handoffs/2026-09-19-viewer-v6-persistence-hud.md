# Handoff

## Branch
`viewer/v6-persistence-hud`

## Base commit
`a5979e9` (merge of PR #11, `viewer/v5-edit-measure` -> `main`; Scanner S1-S6
and Viewer V1-V5 complete)

## Head commit
`3017e37` (V6 implementation)

## What changed
Task V6 from `docs/plans/ghostmap-implementation-plan.md` section 17:
persistence for the effective, locally-edited scene, and a polished Viewer
HUD.

### Persistence
- `Runtime/Persistence/ScenePersistence.cs` — **new**. Plain C#, no
  `MonoBehaviour`. `TrySave(SceneSnapshot, path, out error)` serializes with
  `JsonUtility` (the exact same `SceneSnapshot` type and serializer the wire
  protocol and `FixtureLoader` already use — no duplicate DTO, no second JSON
  schema per `AGENTS.md` rules 2/3) and writes atomically: to a `.tmp`
  sibling first, then `File.Replace`/`File.Move` into place, so a crash or
  failure mid-write can never leave a corrupt save file where a good one used
  to be. `TryLoad(path, out snapshot, out error)` reuses
  `FixtureLoader.TryLoadFromFile` for well-formedness (file exists, valid
  JSON, present room, supported schema version) and then runs the same
  domain validation every scanner snapshot goes through
  (`SceneSnapshotValidator`) before returning a snapshot — a save file is
  therefore byte-for-byte a bare fixture file (protocol v1 section 8) and can
  be re-loaded by either this type or the "Load fixture" developer button.
- `Runtime/Scene/SceneSnapshotValidator.cs` — **new**. Extracted, byte-for-
  byte identical, from `ViewerSceneStore`'s former private `TryValidate` —
  `RoomValidator.ValidateRoom` plus `OpeningValidator`/`FurnitureValidator`
  per opening/object. Pure refactor (no behavior change) so
  `ScenePersistence.TryLoad` runs exactly the same check a scanner snapshot
  does, rather than a third copy of the same three-validator composition.
  `ViewerSceneStore.cs` now calls it instead of its own private method.
- `Runtime/Scene/ViewerEditableScene.cs` — added
  `LoadExternalSnapshot(SceneSnapshot loaded, out error)`. Installs a
  previously-validated snapshot as the new effective scene through the
  *same* clone-and-`Changed` path a scanner finalized snapshot uses, so
  `RoomRenderer`/`OrbitCameraController`/every interaction controller pick
  it up through the one existing `IViewerSceneSource` channel — no second,
  load-specific rendering path. Fails without side effects (`Current`
  untouched) for a null snapshot or one with no room.

### The what-gets-saved decision
`ViewerHudController`'s Save button always passes
`bootstrap.EditableScene.Current` — never `bootstrap.Session.SceneStore.Current`
— so a save always captures:

```text
Scanner snapshot -> ViewerEditableScene -> post-finalization Viewer edits -> SAVE THIS
```

exactly as the task brief requires. This was verified directly: a dedicated
test (`ScenePersistenceTests.SavingTheEditableSceneSavesTheEditNotTheOriginalScannerData`)
edits a furniture object through `ViewerEditableScene`, confirms the
underlying `ViewerSceneStore.Current` still holds the **stale, pre-edit**
position (proving the two are genuinely different objects, not aliases), and
then confirms the saved-and-reloaded file holds the **edited** position.

### Load authority semantics (this task's decision, per the brief's
"choose the smallest architecture-consistent implementation, document it,
test it")
A successfully loaded scene always becomes `EditingEnabled = true` — the
only reason a scene is ever saved is that it was already post-finalization,
so a loaded file is put back exactly where it left off. This reuses
`ViewerEditableScene`'s existing same-session/`EditingEnabled` guard
unmodified:

| Situation after a Load | Outcome |
| --- | --- |
| Scanner resends the *same* `sessionId` the loaded file carries (a stale/duplicate reconnect) | **Ignored** — the loaded/edited scene is protected, exactly like a duplicate finalized resend already was in V5 |
| A genuinely different `sessionId` arrives (a new scan) | **Replaces** the loaded scene unconditionally, per `ADR-0003` — a new scan session always wins |

Both directions are proved against the real `ViewerSceneStore` pipeline in
`ViewerEditableSceneLoadTests` (`AStaleReconnectResendOfTheLoadedSessionDoesNotOverwriteTheLoadedScene`,
`AGenuinelyNewSessionStillReplacesALoadedScene`). No ADR change was needed —
this composes from `ADR-0003`'s existing rule and V5's existing
`OnSourceChanged` guard without modifying either.

### HUD
- `Runtime/UI/ViewerHudController.cs` — polished into the real control
  surface:
  - **Save** / **Load** buttons — new. Save writes
    `bootstrap.EditableScene.Current` to a single fixed slot at
    `Application.persistentDataPath/ghostmap-scene.json` (unlike the V1
    "Load fixture" button's repo-relative dev shortcut, `persistentDataPath`
    resolves correctly in a standalone player build too — see "Standalone
    build" below). Load reads that slot through `ScenePersistence.TryLoad`
    and, on success, installs it via `ViewerEditableScene.LoadExternalSnapshot`,
    which fires `Changed` and rebuilds/displays the room immediately through
    the existing `RoomRenderer` pipeline. Both report a one-line result
    (`Saved to ...` / `Load failed: ...`) in the status text.
  - **Shortened session id** — the status line now shows the effective
    scene's `sessionId` (`bootstrap.EditableScene.Current.sessionId`, not
    the raw scanner connection's `session.LastSessionId` — the two can
    differ after a Load) truncated to 8 characters plus `…`. The full id is
    still what every ownership/revision decision actually uses; only the
    display is shortened.
  - **"Waiting for a scanner connection..."** — shown before any scene
    exists and the server is listening, replacing V5's bare "Scene: none
    yet" with an explicit statement that the Viewer is up and listening
    (task requirement: "before connection, clearly show that it is
    listening/waiting").
  - **"Disconnected — displaying last snapshot."** — protocol v1 section
    6's exact required message, shown once a scene exists and
    `ServerConnectionState.Disconnected` is reported. This closes a gap
    that existed since V1 (the HUD showed `Connection: Disconnected` but
    never protocol v1's specific required sentence).
  - Reset View, Dollhouse, and the developer "Load fixture" button are
    unchanged from V4/V1.
- `Runtime/Interaction/*`, `Runtime/UI/InspectorPanelController.cs` —
  **unchanged**. Decision: the inspector's own Measure/Clear-measurement
  buttons stay where V5 put them (both are already part of the same
  Canvas/button column `ViewerSceneBuilder` builds, both drive the single
  real `MeasurementController` — no parallel copy of its state), rather
  than moving their ownership into `ViewerHudController` for its own sake.
  "The object inspector must integrate cleanly with this HUD rather than
  becoming a competing UI system" is read as "no second source of truth",
  which was already true in V5; V6 did not need to also centralize every
  button's C# ownership to satisfy it, and moving working, tested wiring
  for no functional gain was judged higher-risk than leaving it alone.
- `Editor/ViewerSceneBuilder.cs` — adds `SaveButton`/`LoadButton` to the
  existing left-side button column (below Load Fixture, at the same 70px
  button / 10px gap spacing V1-V5 already established) and wires them into
  `ViewerHudController`. `VerifyScene()` asserts both are present and
  assigned.

## Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**`, `docs/decisions/**` was touched, and no
`apps/scanner/**` file was touched. `ScenePersistence` persists the exact
frozen `SceneSnapshot` type via the exact same `JsonUtility` serializer the
wire protocol and `FixtureLoader` already use — no new DTO, no second JSON
schema, no manually-mirrored fields. A saved file is a bare `SceneSnapshot`,
identical in shape to a protocol v1 fixture file (protocol v1 section 8).

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

## Test results
- **Viewer project: 458 tests, 458 passed, 0 failed, 0 skipped.** Unity exit
  code `0`. That is the prior 430 (156 embedded shared + 210 V1-V4 + 64 V5,
  all unchanged) + **28 new V6 tests**:
  - `ScenePersistenceTests` (21): save fixture, load fixture, full
    semantic-equality round trip, corners/height/openings/furniture/
    position/dimensions/yaw/finalized-flag individually, "the edit is what
    gets saved, not the stale scanner copy", invalid JSON rejected,
    unsupported schema version rejected, missing room (no `"room"` key at
    all) rejected, a structurally-present-but-illegal room (bad height)
    rejected, a failed load never installs over the current scene,
    file-not-found handled cleanly, repeated save/load does not drift
    values, an existing save file is overwritten intentionally, and no
    `.tmp` file is left behind on success.
  - `ViewerEditableSceneLoadTests` (7): a valid load installs and enables
    editing, `Changed` fires, a null snapshot and a no-room snapshot are
    both rejected without touching `Current`, editing works immediately
    after a load, a stale same-session reconnect resend does not overwrite
    a loaded scene, and a genuinely new session still replaces one.
- **Shared `TestProject`: 156 tests, 156 passed, 0 failed.** Exit code `0`
  — confirms no `shared/**` regression, as expected since nothing there was
  touched.
- `ViewerSceneBuilder.BuildScene` and `.VerifyScene` re-verified end to end
  via a throwaway EditMode test (not committed; `-executeMethod` still hangs
  in this sandbox — see prior handoffs). `VerifyScene` now also asserts the
  HUD's `saveButton`/`loadButton`. The real `Viewer.unity` scene asset was
  regenerated by this run and is part of this commit.
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.

## End-to-end persistence smoke test
A throwaway EditMode test (not committed) drove the *real* production
pipeline, not a fake:

1. Loaded the real `fixtures/room-with-door-window-v1.json` fixture
   (finalized, 4 corners, 2 openings, 3 objects) through
   `FixtureLoader` -> `ViewerSceneStore` -> `ViewerEditableScene`.
2. Edited `bed-1` (`TryApplyLocalEdit`: new position and a 270° yaw).
3. Saved the edited `ViewerEditableScene.Current` via `ScenePersistence.TrySave`
   to a temp file.
4. Discarded every in-memory object (`Detach()`, dropped references) —
   simulating a fresh Viewer process, not just a reset method call.
5. `ScenePersistence.TryLoad`'d the file into a **brand-new**
   `ViewerSceneStore` + `ViewerEditableScene` + `RoomRenderer` +
   `OrbitCameraController` + `ObjectSelectionController` +
   `ObjectEditController` + `MeasurementController` — none of them the same
   instances used in steps 1-3.
6. Confirmed the edited bed's position/yaw came back exactly, both on the
   `SceneSnapshot` and on the real `Object_bed_bed-1` GameObject's
   `Transform.position`.
7. Confirmed room/opening counts were semantically identical to the
   original fixture.
8. Toggled Dollhouse on the fresh `OrbitCameraController` — confirmed
   `RoomRenderer.CeilingVisible` went false, exactly like V4.
9. Ran a real two-point `MeasurementController` measurement via
   `Physics.Raycast` against the reloaded floor — got a positive, finite
   distance.
10. Selected the reloaded bed via a real `ObjectSelectionController.TrySelectAt`
    raycast (resolved to `bed-1` correctly) and applied a further
    `ObjectEditController.TrySetYaw` — confirming selection and editing both
    work on a scene that arrived via Load, not via the scanner.

All ten steps passed on the real components. Full detail was in the
throwaway test's assertions (not committed, per this sandbox's established
convention for one-off verification code).

## Standalone build verification
A throwaway EditMode test (not committed) called
`BuildPipeline.BuildPlayer` directly for `BuildTarget.StandaloneOSX` against
the real `Viewer.unity` scene. Result: **`BuildResult.Succeeded`, 0 errors,
0 warnings**, ~102 MB `.app` bundle produced and confirmed to exist on disk.
No elaborate release pipeline was built — this only confirms the existing
scene/build settings are correct and that V6 introduced no Editor-only
dependency into runtime code (`grep -rl UnityEditor Runtime/` returns
nothing). `EditorBuildSettings.scenes` already correctly lists only
`Viewer.unity`, set by the existing `ViewerSceneBuilder.BuildScene()`.

## Exact Save/Load behavior
- **Save**: writes `bootstrap.EditableScene.Current` — the effective scene,
  post-finalization Viewer edits included — to
  `Application.persistentDataPath/ghostmap-scene.json`, atomically (temp
  file + `File.Replace`/`Move`). Disabled in effect (shows "Nothing to save
  yet.") when no scene is loaded.
- **Load**: reads that same fixed slot, runs full well-formedness +
  domain validation (`SceneSnapshotValidator`, the same validators every
  scanner snapshot goes through) before touching anything, then installs
  the result via `ViewerEditableScene.LoadExternalSnapshot`, which
  immediately rebuilds the room through the existing `RoomRenderer` (via
  `IViewerSceneSource.Changed`) and sets `EditingEnabled = true`. A bad or
  missing file leaves the currently displayed scene completely untouched
  and reports `Load failed: <reason>` in the HUD.
- Single fixed save slot (no file picker, no multiple slots) — an explicit
  MVP scope decision; the task brief does not ask for either and Unity's
  legacy UGUI has no built-in native file dialog, so building one would be
  scope expansion, not "the smallest architecture-consistent
  implementation."

## Known limitations
- **Single save slot.** Save/Load always use the one fixed
  `Application.persistentDataPath/ghostmap-scene.json` file; there is no
  save-as, no multiple slots, and no in-app file browser. Not required by
  the task brief.
- **No confirmation dialog before Save overwrites the existing slot.** The
  write itself is atomic/safe (never a half-written file), but there is no
  "are you sure" prompt distinguishing a fresh save from an overwrite. Not
  requested by the task brief.
- **A loaded scene's `EditingEnabled` is always forced `true`**, regardless
  of the loaded file's own `finalized` flag (see "Load authority
  semantics" above). In practice this never diverges from `finalized`,
  since the only path that ever produces a save file already required
  `EditingEnabled == true`; a hand-edited save file with `finalized: false`
  would still become editable on load, which is a deliberate,
  documented choice, not an oversight.
- Carried over from V1-V5, unchanged: `-executeMethod` hangs in this
  sandbox (use `-runTests`, wrapping target code in a throwaway EditMode
  test); `-nographics` segfaults on `Camera.Render()` (not needed for this
  task's verification, since no new rendering surface was added);
  `Resources.GetBuiltinResource` logs an editor assert in batchmode that a
  test calling `BuildScene` must ignore (not a build failure); no
  undo/redo, no delete/add object (still out of scope); every accepted
  edit or load still rebuilds the whole `RenderedRoom` (fine at MVP room
  sizes, same pattern V2-V5 already use).
- No physical-device testing applies to this workstream (desktop app).
  Standalone build was verified to produce a valid `.app`; it was not
  launched and driven interactively as a separate running process in this
  sandbox (no display session was exercised beyond the Editor's own
  offscreen rendering used for V2-V5's screenshots).

## Files most important to read next
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Persistence/ScenePersistence.cs`
  — the entire save/load contract.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Scene/ViewerEditableScene.cs`
  — `LoadExternalSnapshot` and its authority-semantics doc comment.
- `docs/status/viewer.md` — full V6 design notes and known limitations.

## Next task
- **I1 — real iPhone -> Viewer live room** (implementation plan section 18).
  Full demo integration requires S6 (done) and V6 (this task, done). Do not
  begin I1 in this branch.
