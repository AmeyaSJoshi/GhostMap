# Onboarding: everything a new agent needs

Read this once, top to bottom, before touching anything. It is the shortest
complete picture of GhostMap: what it is, where it stands, how the code fits
together, what you can and cannot do from your environment, and every trap
earlier workers fell into. It points to the canonical documents rather than
repeating them; when this file and a canonical document disagree, the
canonical document wins (see `docs/README.md`, "Which document wins").

---

## 1. The 60-second version

- **Product.** Stand in the middle of a room with a standard non-LiDAR iPhone,
  turn around once, and get a structured 3D model: four walls, height, doors,
  windows and named furniture. The phone builds Unity-ready files (`room.glb`,
  one `.glb` per object, `scene.json`) plus a self-contained `room.html`, and
  **Send to Computer** hands them over through the iOS share sheet (AirDrop).
  Everything runs on the phone. Direction: ADR-0012 and ADR-0013.
- **Not a mesh.** The model is plain data: four ordered floor corners (walls
  are derived, never stored), rectangular openings, parametric furniture boxes
  with yaw. Schema: `docs/contracts/scene-schema-v1.md`.
- **Stack.** Two Unity 6000.3.24f1 projects (`apps/scanner` for iOS,
  `apps/viewer` desktop, frozen) sharing one local package
  (`shared/com.ghostmap.shared`). AR Foundation / ARKit 6.3.1. C#. EditMode
  tests. A three.js browser viewer lives in `apps/web-viewer/` (`R8`). The phone
  app stays Unity for now (see "Where Unity fits, and why" below).
- **Where it stands.** The original assisted-capture MVP is done and its
  scanner was verified on an iPhone. The owner's hackathon work (stand-in-place
  scan, sweep, furniture surface detection, `.glb` export) was merged in task
  `R1`. **None of the new work has run on an iPhone.** Roadmap `R2`-`R10`
  (plan section 18) is the work ahead.
- **Your first job** is in `docs/status/integration.md` under "Next safe task".

### Where Unity fits, and why

The browser viewer (`room.html`, ADR-0013) replaces only the **desktop Unity
Viewer**. Unity stays in two places:

1. **The iPhone app is a Unity app** (`apps/scanner`, AR Foundation + ARKit).
   Capture, the YOLO-n detector (`R4`), the export bundle (`R6`) and Send to
   Computer (`R7`) are all built inside it. The shared C# package
   (`shared/com.ghostmap.shared`) is part of it.
2. **Unity is where the owner uses the result.** They drag `room.glb` and
   `objects/*.glb` into a Unity project. `room.html` is for looking and
   measuring without opening Unity.

```text
iPhone app (Unity)  ->  zip via AirDrop  ->  room.html in a browser (look, measure)
                                         ->  .glb files into Unity (build with it)
```

**Why the phone app is Unity:** chosen at the start (ADR-0001), when the plan
had a desktop Unity Viewer too, so both apps could share one C# package. With
the Viewer retiring, that reason is gone. Native Swift would fit the remaining
work better: Core ML/Vision for YOLO-n and the share sheet are built in, and it
builds on any Mac with no Unity licence. Against that, all capture code and its
577 tests exist in Unity, built but never run on an iPhone.

**Owner's decision (2026-10-11): stay on Unity for now; revisit after `R2`.**
If the stand-in-place scan works on the device in `R2`, stay. If it does not,
capture code gets rewritten anyway, and that is the cheapest point to consider
native Swift. Do not start a rewrite, or argue for one mid-task (`AGENTS.md` rule 15).

---

## 2. Where things stand

| Area | State | Verified on iPhone |
| --- | --- | --- |
| Floor lock, coordinate frame, walked corners, closure, height by aiming or typing, two-point doors/windows, manual furniture | Built (S1-S5) | **Yes** |
| TCP snapshot stream, finalize, Reset | Built (S6); now a developer tool | Yes, against a Python listener |
| Guided scan UI (step header, bottom sheet, Details panel, two-tap Restart), Editor/Simulator demo mode | Built (hackathon, `R1`) | No |
| Automatic room scan from ARKit planes | Built (`R1`) | No |
| Wall sweep fallback | Built (`R1`, ADR-0005) | No |
| Furniture surface detection with measured size | Built (`R1`, ADR-0006) | No |
| Furniture type from ARKit plane label | **Removed** in `R4` (ADR-0007, superseded by ADR-0011) | — |
| UDP discovery to the desktop Viewer | Built (`R1`, ADR-0009); **superseded**, removed in `R10` | No |
| Desktop Unity Viewer (render, edit, measure, save, per-object `.glb`) | Built (V1-V6, ADR-0006); **frozen**, removed in `R10` | n/a |
| YOLO-n furniture identification (ADR-0011) | Built (`R4`): Core ML plugin, model in the app, tracker, "Found: ..." and Add All; compiled for iPhone | No |
| Phone-built export bundle, saved scans (`R6`) | Built: zip on Finish, kept in `persistentDataPath/scans/`; validator-clean `.glb`, glTFast import checked | No |
| Share-sheet Send to Computer, Rooms list | Built (`R7`); native plugin compiled for iPhone | No |
| `room.html` browser viewer | Built (`R8`), filled by the phone (`R6`); checked in headless Chrome, owner's Safari check left | n/a |
| A photo of each object, shown in `room.html` | Built (`R11`); the phone crops it from the camera; no photos in demo mode | No |

Test suites: shared 303, viewer 580, scanner 821, all passing in this
repository (owner's Mac, 2026-10-11, after `R5`); web viewer 48 (Node). CI's Unity jobs still wait
for the owner's licence secrets (`docs/ci.md`); until then run
`./tools/run_unity_tests.sh` on a Mac with Unity.

---

## 3. Your environment: what you can and cannot do

Most agents run in a cloud container. Know its limits before planning:

| You want to | In a cloud session |
| --- | --- |
| Run the Unity EditMode suites | **No.** Unity is not installed, and Unity's download and licensing servers are blocked by the network policy. Use CI (`.github/workflows/tests.yml`, `docs/ci.md`) or ask the owner to run `./tools/run_unity_tests.sh` on their Mac |
| Regenerate `Scanner.unity` / `Viewer.unity` | **No** (needs Unity). If you change a scene builder, say so in your handoff; the owner runs **GhostMap > Build Scanner Scene** / **Build Viewer Scene** |
| Build to an iPhone or test on a device | **No.** Only the owner can. Write a precise device procedure instead (see the `R2` brief) |
| Run Python tools | Yes. `python3` 3.13 (stdlib only for `tools/`) |
| Build and test the browser viewer | Yes. Node 22 is available: `cd apps/web-viewer && npm ci && npm test` |
| Push | Only to the branch the session was given. You cannot delete remote branches or push tags |
| Merge another contributor's code | An automated permission check may block it unless the owner explicitly approves in the conversation |
| Open and merge PRs | Yes, through the GitHub tools, when the owner asks |

Consequences for how you work:

- **Write code you can reason about without compiling it**, keep changes
  small, and lean on pure C# with EditMode tests so CI proves it.
- **Never claim a test passed unless it ran.** Write "not run here" and name
  who must run it.
- **Never claim device behaviour works** until the owner reports it from an
  iPhone (`AGENTS.md` rule 12).

---

## 4. Repository tour

```text
AGENTS.md                 the rules (read first; this file does not replace it)
docs/
  README.md               which document answers which question
  onboarding.md           this file
  ci.md                   CI and the one-time Unity secret setup
  specs/                  product spec: workflow, FR-01..FR-19 with real status
  plans/                  implementation plan; section 18 = roadmap R1-R10
  architecture/overview.md  design + section 12 code map + section 13 runtime model
  contracts/              scene-schema-v1.md, protocol-v1.md, export-bundle-v1.md
  decisions/              ADR-0001..0013, index in README.md
  status/                 one page per workstream; integration.md = roadmap tracker
  handoffs/               one file per completed task; only recent ones in the tree
  tasks/                  implementation briefs for R2-R10
  research/               hackathon experiments, not production
shared/com.ghostmap.shared/  Domain, Geometry, Validation, Protocol, Export (.glb writer, part table, walls)
shared/TestProject/       host project that only runs the shared tests
apps/scanner/             iOS app: all capture, recognition, (soon) export and share
apps/viewer/              desktop Unity app: frozen, retiring in R10
apps/web-viewer/          three.js source; builds dist/room-template.html (R8)
fixtures/                 three canonical SceneSnapshot JSON files
tools/                    run_unity_tests.sh, inspect_snapshot.py, send_fixture.py
.github/                  PR template, CI workflow
```

Every folder has a README. The code map in
`docs/architecture/overview.md` section 12 lists every runtime class.

---

## 5. How one scan flows through the code (today)

Follow this once in the source; it explains most of the scanner.

1. **Composition root.** `FloorLockHud.BuildWorkflow()`
   (`apps/scanner/.../Runtime/UI/FloorLockHud.cs`) constructs every controller
   against one `ISpatialProvider` (`ArSpatialProvider`, or `SimulatedRoom` in
   the Editor/Simulator) and wires them into one `ScanWorkflowController`.
   **Reset/Restart** calls it again: new session id, revision 0, AR tracking
   kept.
2. **Phases.** `ScanWorkflowController` owns `ScanPhase` and a transition table
   (`AllowedTransitions`). Only it changes phase. Every structural change calls
   `Publish()`: revision + 1 and a fresh `SceneSnapshot`.
3. **Floor.** `FloorLockController` raycasts the screen centre against ARKit
   planes, requires `HorizontalUp`, and builds a `GhostCoordinateFrame`
   (origin at the hit, +Y up, +Z = camera forward projected flat). Everything
   afterwards is in Ghost space, floor at y = 0.
4. **Room.** Default: `BeginAutoScan` → `AutoRoomScanController.Tick` →
   `WallPlaneAccumulator` clusters vertical planes → `RoomFromWalls` picks four
   → `TryFinishAutoScan` installs corners via
   `CornerCaptureController.TryAdoptDerivedCorners`. Fallbacks:
   `BeginWallSweeping` (`WallSweepController` + shared `WallFitting`) or
   `FallBackToWalkedCorners` (`CornerCaptureController`). All three end with
   four corners in the same store and pass `RoomValidator.ValidateRoom`.
5. **Height, openings.** `HeightCaptureController` (wall plane from corners,
   ray intersection, or typed); `OpeningCaptureController` (two points on a
   derived wall; also adopts ARKit-labelled openings from the auto scan).
6. **Furniture.** `FurnitureDetectionController.Refresh` turns horizontal
   surfaces into `FurnitureCandidate`s (size, yaw, height). The camera-image
   detector (`IObjectDetector`: `CoreMlObjectDetector` on the phone,
   `SimulatedObjectDetector` in demo mode) feeds
   `ScanWorkflowController.UpdateFurnitureIdentification`;
   `FurnitureIdentificationController` projects each box to the floor, matches
   a surface, maps the COCO class (`FurnitureClassMap`) and tracks it across
   frames, and names the matching surfaces (`SuggestedTypeFor`).
   `TryAcceptDetectedFurniture` / `TryAcceptAllDetectedFurniture` adopt
   surfaces and identified furniture into `ObjectPlacementController`'s store;
   manual placement uses the same store.
7. **Finalize.** `TryFinalize` sets `finalized = true` and publishes once more.
   No structural method accepts the `Finalized` phase, so the scene is frozen.
   `ScannerHudController.OnFinalizePressed` then calls `ScanExporter.Export`:
   `ExportBundleBuilder` builds the zip (shared `GlbWriter` for `room.glb` and
   each object, the `Resources/GhostMapRoomTemplate.html` copy of the browser
   viewer filled with the data) and `SavedScanStore` writes it atomically to
   `persistentDataPath/scans/`. `ScanGuide` shows "saved" or the error.
8. **Send to Computer.** After Finish, `ScannerHudController.OnSharePressed`
   (retrying a failed export first) calls `ShareController.TryShare` with the
   saved zip. `IosShareSheet` calls the native plugin
   (`Assets/Plugins/iOS/GhostMapShare/GhostMapShare.mm`), which presents
   `UIActivityViewController` and answers through `UnitySendMessage`;
   `ShareResult.TryParse` reads it and the guide shows the outcome.
   `SavedRoomsHud` (**Rooms**) lists saved zips and sends any of them.
   **Developer live view** (not the product path): the **Live view** chip
   opens a card that discovers the desktop Viewer (`PeerDiscoveryClient`, UDP
   47832) and streams snapshots over TCP 47831 (`ScannerNetworkClient`,
   `ScannerSnapshotPublisher`); `R10` removes it.
9. **UI.** `ScanGuide.Describe(workflow, context)` (pure, tested) produces the
   step title, instruction and messages; `ScannerGuideHud` renders them;
   per-phase HUDs (`AutoScanHud`, `WallSweepHud`, `FurnitureDetectionHud`, ...)
   own their buttons and world-space markers. `ScannerSceneBuilder` builds the
   whole scene and `VerifyScene()` asserts every reference is wired.

On the desktop side (frozen): `ViewerTcpServer` → `ViewerSession` →
`ViewerSceneStore` (revision rules, validation) → `ViewerEditableScene` →
`RoomRenderer`, camera, selection, editing, measurement, `ScenePersistence`,
`FurnitureAssetExporter`.

---

## 6. Invariants you must not break

1. `shared/com.ghostmap.shared` is the only home of the schema, geometry,
   validation and wire formats. Never copy a rule into an app or into
   JavaScript.
2. Walls are derived from consecutive corners and never serialized.
3. Exactly four ordered corners, floor at Ghost y = 0, units metres, angles
   degrees, +Y up.
4. Every structural change increments `revision`; it never goes backwards
   (undo is a forward change).
5. `RoomValidator`, `OpeningValidator`, `FurnitureValidator` decide what is
   legal. Any limit change is mirrored in `tools/inspect_snapshot.py` in the
   same commit.
6. Only `ScanWorkflowController` changes the phase; no UI sets it directly.
7. Learned models may only name objects, never set geometry (rule 6). Nothing
   leaves the phone except the bundle the user shares.
8. Generated `.unity` scenes come from their builders; never hand-edit them.
9. Code comments cite plan section numbers; never renumber the plan.
10. Unity and package versions are pinned; changing them needs its own PR
    (rule 10).

---

## 7. Gotchas earlier workers hit

**Unity and C#**
- `JsonUtility` ignores unknown fields, leaves missing fields at defaults, and
  creates a default object for a missing nested object (so "no snapshot"
  arrives as a zeroed snapshot). It cannot serialize properties, dictionaries,
  interfaces or `List<T>` reliably; schema types use public fields and arrays.
- A missing component from `GetComponent<T>()` is a Unity "fake null": `?.`
  does not short-circuit on it. Use explicit `!= null`.
- After moving a collider outside Play mode, call `Physics.SyncTransforms()`
  before raycasting in a test.
- `ARRaycastHit` cannot be built in EditMode tests; that is why
  `ISpatialProvider` returns `FloorHit` and `DetectedSurface`.
- Two branches that both add a constructor argument merge cleanly and still
  break the build (a new test file on one branch uses the old arity). Search
  for every constructor call after a merge.
- Test counts include the embedded shared suite: the viewer and scanner
  projects list `com.ghostmap.shared` under `testables`. Compute expected
  counts from the results XML, never by arithmetic.

**Browser viewer (`apps/web-viewer`)**
- An element with its own `display` ignores the `hidden` attribute; the
  stylesheet's `[hidden] { display: none !important; }` is what keeps the
  full-screen message overlay from covering the page. Do not remove it.
- The placeholder `__GHOSTMAP_DATA__` must appear in the template exactly once,
  so the code that detects it checks only a prefix. Never write the full
  placeholder in `src/`.
- `scene.json` is left-handed Ghost space; `.glb` is glTF. Objects are placed
  with Z negated and yaw negated (`src/placement.js`). Fix placement there,
  never in the exporter.
- The Claude desktop app's browser pane cannot open `file://` pages, and its
  screenshots are blank while the pane is hidden. Check the page with headless
  Chrome over the DevTools protocol instead.

**Unity tooling**
- Headless `-executeMethod` has hung at "Start Indexing" before; prefer
  `-runTests`. `-nographics` crashes `Camera.Render()`.
- `Scanner.unity` must be regenerated after any change to
  `ScannerSceneBuilder`; `ScannerSceneTests` catches staleness. The viewer has
  **no** scene test, so its staleness is invisible.

**iOS**
- The iOS build is two Unity invocations: **Configure Scanner XR (iOS)**, then
  **Build Scanner (iOS)**. The XR loader and Input System settings only reach
  compiled code on the next compile.
- `ScannerIosPostBuild` adds camera and local-network usage strings and an
  Xcode 26 Swift-library workaround; it is guarded by `UNITY_IOS`.
- A stale Xcode build once made a device test inconclusive. Delete the app
  and clean-build before every device test.
- Signing team is chosen by hand in Xcode.

**iOS native plugins**
- A `.mm` file is Objective-C++: declare Unity's C functions
  (`UnitySendMessage`, `UnityGetGLViewController`) `extern "C"`, or the link
  fails. Files under `Assets/Plugins/iOS/` are iOS-only by folder convention;
  check `Builds/iOS/Libraries/Plugins/iOS/` after a build.
- `UnitySendMessage` needs a GameObject by name. `IosShareSheet.Create` makes
  one at runtime so the scene does not depend on it.
- Only an unsigned `xcodebuild` proves a plugin compiles and links; EditMode
  tests never load it. The iOS Simulator runs it for real (the share sheet
  works there, without AirDrop): `ScannerSimulatorBuild.ConfigureForSimulator`
  and `BuildForSimulator`, `xcodebuild -sdk iphonesimulator`, install. To test
  **Rooms** without finishing a scan, copy a bundle into the app's
  `Documents/scans` (`xcrun simctl get_app_container <udid> com.ghostmap.scanner data`).
- `ConfigureForSimulator` changes tracked files (`XRGeneralSettingsPerBuildTarget.asset`,
  `ProjectSettings.asset`: Simulator SDK). Run `ScannerBuild.ConfigureXr`
  afterwards and `git checkout --` whatever is still changed before committing.
- Injected Simulator screenshots lag one frame behind taps; take a second one
  before concluding a tap did nothing.

**Geometry**
- The GhostMap frame is right/up/forward from the floor-lock moment and is
  immune to `XROrigin.CameraYOffset` because everything is read in world space.
- Interior angles use polygon winding; a reflex corner must be rejected
  (F4 found and fixed this).
- glTF is right-handed: the exporter negates Z. **glTFast negates X on import**
  (checked with glTFast 6.20.0), so exported files arrive in Unity turned 180°
  about Y relative to `scene.json`. Furniture placed by hand in Unity goes at
  `(-x, y, -z)` with yaw unchanged (`docs/contracts/export-bundle-v1.md`
  section 4). An earlier note here said glTFast negates Z back; it does not.
- `GlbWriter` must write accessor `min`/`max` with round-trip precision; glTF
  requires them to equal the data exactly, and float noise on rotated walls
  broke six-decimal rounding. Run the Khronos validator after changing it.
- The scanner ships a copy of the browser viewer template in `Resources/`.
  Rebuild `apps/web-viewer` and recopy; `ShippedTemplate_MatchesTheWebViewerBuild`
  fails while the copy is stale.

**Process**
- Handoffs are append-only. Status pages describe reality, including where it
  differs from the plan.
- ADRs are never rewritten; supersede them and add a status note.

---

## 8. Testing strategy

- **EditMode only.** Everything with logic is plain C# behind an interface so
  it can be tested without a device: `ISpatialProvider` (fake:
  `FakeSpatialProvider`), `ISnapshotSink` (`FakeSnapshotSink`), sockets
  (`FakeViewerListener`, `FakeScannerClient`). Follow this for every new
  device-facing piece (`IObjectDetector` is faked through boxes in
  `FurnitureIdentificationTests`; `IShareSheet` has `FakeShareSheet`).
- `MonoBehaviour`s stay thin: they read input and call tested plain classes.
- Scene wiring is asserted by `ScannerSceneBuilder.VerifyScene()` and
  `ScannerSceneTests`.
- Python tools are checked in CI's `tools` job. The browser viewer has Node
  tests (`apps/web-viewer`, CI job `web-viewer`); its placement test loads a
  `.glb` written by the real C# exporter.
- Every reproducible bug gets a regression test (rule 11).

---

## 9. Process

1. Start: `git status`, `git log -5 --oneline`, `git fetch origin`; read
   `AGENTS.md`, your status page, the contracts, the latest handoff, and the
   task brief in `docs/tasks/`.
2. Work on a branch (`scanner/<task>`, `viewer/<task>`, `shared/<task>`,
   `integration/<task>`), or the branch your session was given.
3. Commits: `feat(scanner): ...`, `fix(viewer): ...`, `docs(...)`, with the
   attribution lines your environment requires.
4. Finish: follow `AGENTS.md` rule 13 and its end-of-task documentation
   checklist (tests or who must run them, every document you touched, a new
   handoff, commit), then open a PR using `.github/pull_request_template.md`.
5. Contract changes (schema, protocol, export bundle, validation limits):
   ADR if behaviour changes, contract doc, tests, `inspect_snapshot.py`, a
   dedicated commit (rule 8).

---

## 10. People and history

- **Owner:** AmeyaSJoshi. Makes product decisions, runs Unity and the iPhone,
  approves merges of other people's code.
- **Contributor:** krishangnaikar wrote the sweep capture, furniture detection,
  `.glb` export and the ADR-0007/0008 proposals during the hackathon.

| Date | What happened |
| --- | --- |
| 2026-09-12 | Foundation F0-F4: repo, schema, geometry, validation, protocol, fixtures, review |
| 2026-09-12-13 | Scanner S1-S6, each verified on an iPhone |
| 2026-09-14-19 | Viewer V1-V6 |
| 2026-10-03 | Hackathon (in the separate `GhostMapDublinHacks` repo): sweep, furniture detection, `.glb` export, UI overhaul, automatic room scan, UDP discovery |
| 2026-10-10 | Repository cleanup; direction set: stand in place, YOLO-n on device (ADR-0011, 0012) |
| 2026-10-11 | Browser viewer and share-sheet transfer chosen (ADR-0013); hackathon repo merged (`R1`); CI added |

Old status pages and handoffs are in git: `git show f5a7d30:docs/status/scanner.md`,
`git ls-tree --name-only 1ff72c9 docs/handoffs/`, and the hackathon's at
`0b7a106`.

---

## 11. Open questions for the owner

Do not decide these yourself; note them in your handoff if they block you.

1. Unity licence secrets for CI (`docs/ci.md`).
2. Whether the AGPL-3.0 licence of YOLO-n is acceptable long term (ADR-0011).
3. Whether to keep the developer TCP stream after `R10`.
4. 20 stale remote branches still exist; only the owner can delete them (list
   and command in `docs/status/integration.md`).
5. Whether furniture should arrive in Unity already placed. `R6` kept each
   `objects/<id>.glb` at its own origin, with a verified manual placement rule
   in `README.txt`; a placed `room-furnished.glb` would be additive
   (`docs/status/integration.md`, "Open decisions").
6. Unity or native Swift for the phone app. Decided "Unity for now"; revisit
   with the owner once `R2` shows whether the stand-in-place scan works on the
   device (section 1, "Where Unity fits, and why"; `AGENTS.md` rule 15).

---

## 12. Glossary

| Term | Meaning |
| --- | --- |
| Ghost space | GhostMap's coordinate frame: origin at the floor-lock point, +Y up, +Z the camera's flat forward at lock time, metres |
| Snapshot | `SceneSnapshot`: the whole scene at one revision |
| Revision | Monotonic counter per session; increments on every structural change |
| Finalize | End of capture; the scene becomes read-only |
| Closure error | Distance between the first corner and a re-aimed check of it (manual paths) |
| Auto scan | Stand-in-place room capture from ARKit vertical planes |
| Sweep | Fallback: sweep the aim along each wall's floor line, fit lines |
| Walked corners | Fallback: tap at each of the four corners |
| Surface / candidate | An ARKit horizontal plane offered as a piece of furniture |
| Export bundle | The zip the phone writes on finalize (`R6`; contract `docs/contracts/export-bundle-v1.md`) |
| Developer stream | Protocol v1 over TCP to the desktop Viewer; not a product path |
| `R1`-`R10` | Roadmap tasks, plan section 18 |
