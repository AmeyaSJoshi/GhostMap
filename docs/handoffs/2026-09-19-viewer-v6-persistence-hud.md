# Handoff

## Branch
`viewer/v6-persistence-hud`

## Base commit
`a5979e9` (merge of PR #11, `viewer/v5-edit-measure` -> `main`; Scanner S1-S6
and Viewer V1-V5 complete)

## Head commit
`3017e37` (V6 implementation), fixed by a follow-up commit on this same
branch — persistence-authority fix (this branch, pending commit at
hand-off time); see "Post-review fix" immediately below.

## Post-review fix: ADR-0003 persistence authority (supersedes parts of
"What changed" below)
An independent review of the pushed V6 code found four real authority bugs,
all now fixed on this branch without redesigning the persistence
architecture (`ScenePersistence`, `SceneSnapshotValidator`,
`ViewerEditableScene`, the bare `SceneSnapshot` JSON format, the
`Application.persistentDataPath` fixed slot, and atomic writes are all
unchanged):

1. **Save was not Viewer-authoritative.** `ViewerHudController.OnSaveClicked`
   saved any non-null `ViewerEditableScene.Current`, including an
   unfinalized, Scanner-owned scene — a direct `ADR-0003` violation. Fixed:
   `ViewerHudController.CanSave(ViewerEditableScene)` (public, static, the
   pure/testable piece of an otherwise-thin button handler — the same split
   `ViewerInteractionRouter.IsClick` already established) now requires
   `editable != null && editable.Current != null && editable.EditingEnabled
   && editable.Current.finalized` before Save writes anything. Otherwise the
   HUD shows **"Save unavailable until the scan is finalized."** and writes
   nothing.
2. **A loaded scene could become editable without actually being
   finalized.** `ViewerEditableScene.LoadExternalSnapshot` used to force
   `EditingEnabled = true` unconditionally. Fixed: it now rejects any
   snapshot whose `finalized` flag is `false` *before* installing it —
   `Current` and `EditingEnabled` are left completely untouched — and only
   then sets `EditingEnabled = Current.finalized` (derived from the flag,
   never hard-coded). It also now re-runs `SceneSnapshotValidator` itself
   (defense in depth for this public scene-replacement boundary, reusing the
   one validator rather than copying its rules).
3. **Load could interrupt an active, unfinalized scan.** Fixed:
   `ViewerHudController.CanLoad(ViewerEditableScene)` (public, static) is
   `true` only when `editable == null || editable.Current == null ||
   editable.EditingEnabled` — i.e. nothing exists yet, or the current room
   is already Viewer-owned. While a Scanner-owned scan is in progress
   (`Current != null && EditingEnabled == false`), Load is refused, the
   HUD shows **"Finish or reset the active scan before loading a saved
   room."**, and neither the displayed scene nor the Scanner session is
   touched.
4. **A successful Load left stale interaction state pointing at the old
   room.** Fixed: `ViewerHudController.ResetTransientStateAfterLoad()` (also
   public, for the same testability reason) runs only after
   `LoadExternalSnapshot` actually succeeds, calling exactly the existing
   APIs the task named: `ObjectSelectionController.ClearSelection()`,
   `MeasurementController.SetActive(false)` + `.Clear()`, and
   `OrbitCameraController.FrameRoom()`. `ViewerHudController` gained two new
   serialized references (`selectionController`, `measurementController`,
   wired in `ViewerSceneBuilder` alongside the existing `cameraController`)
   and `SetSelectionController`/`SetMeasurementController`/
   `SetCameraController` test-wiring setters mirroring the pattern every
   other interaction controller already uses.

**The "Load authority semantics" and "Known limitations" sections below are
corrected by this fix** — in particular, the earlier claim that "a loaded
scene's `EditingEnabled` is always forced `true`, regardless of the loaded
file's own `finalized` flag" is **no longer true** and is removed; see the
corrected rule in "Load authority semantics".

17 new regression tests were added (`ViewerHudControllerPersistenceGatingTests`:
12; `ViewerEditableSceneLoadTests`: 3 more; `ScenePersistenceTests`: 2 more),
bringing the Viewer suite to **475/475**. Full breakdown in "Test results".

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

### The what-gets-saved decision, and the Save/Load authority gate
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

**Fixed by this post-review pass**: that was necessary but not sufficient —
"passes the right snapshot" still let an unfinalized, Scanner-owned snapshot
through. `ViewerHudController.CanSave`/`CanLoad` (both documented above)
now gate both buttons *before* any write/install happens, per `ADR-0003`:

| Button | Gate | Refused when | HUD message when refused |
| --- | --- | --- | --- |
| Save | `CanSave(editable)` | no scene, or the scene is Scanner-owned (`EditingEnabled == false`) | "Save unavailable until the scan is finalized." |
| Load | `CanLoad(editable)` | a Scanner-owned, unfinalized scan is in progress (`Current != null && EditingEnabled == false`) | "Finish or reset the active scan before loading a saved room." |

### Load authority semantics (corrected by this post-review pass)
A loaded scene becomes `EditingEnabled = true` **only when its own
`finalized` flag is already `true`** — `ViewerEditableScene.LoadExternalSnapshot`
rejects anything else outright, leaving `Current` completely untouched.
`EditingEnabled` is then derived from that same flag
(`EditingEnabled = Current.finalized`), never hard-coded, so the two can
never silently disagree. In practice a persisted file is always finalized
(the Save gate above requires it before a file is ever written), but a
hand-edited or corrupted file claiming `finalized: false` is rejected here,
not silently granted editing rights.

Once a finalized load *is* installed, this reuses `ViewerEditableScene`'s
existing same-session/`EditingEnabled` guard unmodified:

| Situation after a Load | Outcome |
| --- | --- |
| Scanner resends the *same* `sessionId` the loaded file carries (a stale/duplicate reconnect) | **Ignored** — the loaded/edited scene is protected, exactly like a duplicate finalized resend already was in V5 |
| A genuinely different `sessionId` arrives (a new scan) | **Replaces** the loaded scene unconditionally, per `ADR-0003` — a new scan session always wins |

Both directions are proved against the real `ViewerSceneStore` pipeline in
`ViewerEditableSceneLoadTests` (`AStaleReconnectResendOfTheLoadedSessionDoesNotOverwriteTheLoadedScene`,
`AGenuinelyNewSessionStillReplacesALoadedScene`); the finalized-only gate is
proved in the same file (`ANonFinalizedSnapshotIsRejectedWithoutTouchingCurrent`,
`LoadExternalSnapshotNeverForcesEditingEnabledForAnUnfinalizedSnapshot`) and
end to end in `ScenePersistenceTests`
(`AFinalizedSavedFileLoadsThroughTheFullPipelineAndBecomesEditable`,
`AnUnfinalizedSavedFileIsRejectedAtTheInstallationBoundary`). No ADR change
was needed — everything here composes from `ADR-0003`'s existing rule and
V5's existing `OnSourceChanged` guard without modifying either.

### Resetting transient interaction state after a successful Load
A Load replaces the room the user was inspecting, so
`ViewerHudController.ResetTransientStateAfterLoad()` — called only after
`LoadExternalSnapshot` succeeds, never on a failed load — clears whatever
referenced the *old* room: `ObjectSelectionController.ClearSelection()`,
`MeasurementController.SetActive(false)` + `.Clear()` (both: leaves
measurement mode off and discards any in-progress/completed measurement),
and `OrbitCameraController.FrameRoom()` (re-frames the camera on the newly
loaded room rather than leaving it wherever the user had orbited/panned to).
Proved against real controllers — not mocks — in
`ViewerHudControllerPersistenceGatingTests`.

### HUD
- `Runtime/UI/ViewerHudController.cs` — polished into the real control
  surface:
  - **Save** / **Load** buttons — new, gated by `CanSave`/`CanLoad` (see
    "Post-review fix" above) before anything is written or installed. Save
    writes `bootstrap.EditableScene.Current` to a single fixed slot at
    `Application.persistentDataPath/ghostmap-scene.json` (unlike the V1
    "Load fixture" button's repo-relative dev shortcut, `persistentDataPath`
    resolves correctly in a standalone player build too — see "Standalone
    build" below). Load reads that slot through `ScenePersistence.TryLoad`
    and, on success, installs it via `ViewerEditableScene.LoadExternalSnapshot`,
    which fires `Changed` and rebuilds/displays the room immediately through
    the existing `RoomRenderer` pipeline, then calls
    `ResetTransientStateAfterLoad()`. Both report a one-line result
    (`Saved to ...` / `Save unavailable until the scan is finalized.` /
    `Loaded from ...` / `Load failed: ...` / `Finish or reset the active
    scan before loading a saved room.`) in the status text.
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
- **Viewer project: 475 tests, 475 passed, 0 failed, 0 skipped.** Unity exit
  code `0`. That is the original V6 total of 458 (prior 430, all unchanged,
  + the 28 first-pass V6 tests) + **17 new post-review fix tests**:
  - `ScenePersistenceTests` (2 more, 23 total): a finalized saved file loads
    through the full `ScenePersistence.TryLoad` -> `LoadExternalSnapshot`
    pipeline and becomes editable; an unfinalized saved file parses fine at
    the `TryLoad` layer (it is a perfectly legal room on its own) but is
    rejected at the `LoadExternalSnapshot` installation boundary.
  - `ViewerEditableSceneLoadTests` (3 more, 10 total): a non-finalized
    snapshot is rejected without touching `Current`; `EditingEnabled` is
    never force-enabled for a rejected (non-finalized) load; a finalized
    snapshot that fails `SceneSnapshotValidator` (defense in depth) is also
    rejected without touching `Current`.
  - `ViewerHudControllerPersistenceGatingTests` (12, new file): `CanSave`
    is false for a null editable, no scene, and an unfinalized
    Scanner-owned scene, and true for a finalized Viewer-owned scene
    (including after a local edit); `CanLoad` is true for a null editable,
    no scene, or an already-Viewer-owned scene, and false while an
    unfinalized scan is active; `ResetTransientStateAfterLoad` clears a
    real selection, clears and deactivates a real in-progress measurement,
    and re-frames a real camera — all against real
    `ObjectSelectionController`/`MeasurementController`/
    `OrbitCameraController` instances, not mocks.
- **Shared `TestProject`: 156 tests, 156 passed, 0 failed.** Exit code `0`
  — confirms no `shared/**` regression, as expected since nothing there was
  touched.
- `ViewerSceneBuilder.BuildScene` and `.VerifyScene` re-verified end to end
  again after this fix, via a throwaway EditMode test (not committed;
  `-executeMethod` still hangs in this sandbox — see prior handoffs).
  `VerifyScene` now also asserts the HUD's `selectionController`/
  `measurementController` references alongside the existing
  `saveButton`/`loadButton`. The real `Viewer.unity` scene asset was
  regenerated by this run and is part of this fix's commit.
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

**Re-run after the post-review fix**, with three additional phases driven
through the real `ViewerHudController.CanSave`/`CanLoad`/
`ResetTransientStateAfterLoad`, not just `ScenePersistence`/
`ViewerEditableScene` directly:

11. On a brand-new, empty pipeline, confirmed `CanSave` was `false` (nothing
    loaded yet) and became `true` only once a scanner snapshot finalized.
12. Started a **second**, independent in-progress (unfinalized) scan on a
    fresh pipeline and confirmed `CanLoad` was `false` — simulating the HUD
    refusing the Load click — and that the in-progress scan's session id and
    `EditingEnabled == false` were completely undisturbed by the refusal.
13. Finalized that second scan, confirmed `CanLoad` became `true`, then
    actually loaded the step-3 save file over it and confirmed: a real,
    pre-load `ObjectSelectionController` selection was cleared; a real,
    half-placed `MeasurementController` measurement was cleared and
    measurement mode left off; a real `OrbitCameraController` deliberately
    moved far from the room (`(900, 900, 900)`) was re-framed back onto it;
    the edited bed's position/yaw were still exactly as saved; and
    selection, editing, measurement, and Dollhouse all continued to work
    normally afterward.

## Standalone build verification
A throwaway EditMode test (not committed) called
`BuildPipeline.BuildPlayer` directly for `BuildTarget.StandaloneOSX` against
the real `Viewer.unity` scene — re-run after the post-review fix too.
Result (both runs): **`BuildResult.Succeeded`, 0 errors, 0 warnings**,
~102 MB `.app` bundle produced and confirmed to exist on disk. No elaborate
release pipeline was built — this only confirms the existing scene/build
settings are correct and that neither V6 nor its fix introduced any
Editor-only dependency into runtime code (`grep -rl UnityEditor Runtime/`
returns nothing). `EditorBuildSettings.scenes` already correctly lists only
`Viewer.unity`, set by the existing `ViewerSceneBuilder.BuildScene()`.

## Exact Save/Load behavior (corrected by the post-review fix)
- **Save**: gated by `ViewerHudController.CanSave(editable)` — requires a
  non-null `ViewerEditableScene`, a non-null `Current`,
  `EditingEnabled == true`, **and** `Current.finalized == true`. If any of
  those fail, nothing is written and the HUD shows **"Save unavailable
  until the scan is finalized."**. Otherwise it writes
  `bootstrap.EditableScene.Current` — the effective scene, post-finalization
  Viewer edits included, never the raw `ViewerSceneStore.Current` — to
  `Application.persistentDataPath/ghostmap-scene.json`, atomically (temp
  file + `File.Replace`/`Move`).
- **Load**: gated by `ViewerHudController.CanLoad(editable)` — refused
  (**"Finish or reset the active scan before loading a saved room."**, no
  file read, no state touched) only when a Scanner-owned, unfinalized scan
  is currently in progress (`Current != null && EditingEnabled == false`).
  Otherwise it reads the fixed slot, runs full well-formedness + domain
  validation (`ScenePersistence.TryLoad` via `SceneSnapshotValidator`, the
  same validators every scanner snapshot goes through), and then
  `ViewerEditableScene.LoadExternalSnapshot` performs one more check before
  installing anything: the snapshot's own `finalized` flag must be `true`,
  or it is rejected and `Current` is left completely untouched. Only once
  installed does `EditingEnabled` become `true` — derived from
  `Current.finalized`, never forced. A successful install immediately
  rebuilds the room through the existing `RoomRenderer` (via
  `IViewerSceneSource.Changed`) and then calls
  `ResetTransientStateAfterLoad()` (clears selection, clears/deactivates
  measurement, re-frames the camera). A bad, missing, or unfinalized file
  leaves the currently displayed scene and all interaction state completely
  untouched and reports `Load failed: <reason>` in the HUD.
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
