# Viewer Status

## Current state
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
- `Runtime/Rendering/`, `Runtime/Interaction/` and `Runtime/Persistence/`
  remain empty placeholders (V2-V6 own them).

## Last verified commit
- Not yet committed — see the V1 handoff for the working-tree state this
  status reflects.

## Tests run
- Command:
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
    -batchmode -nographics -projectPath apps/viewer \
    -runTests -testPlatform EditMode -testResults <out>.xml -logFile <out>.log
  ```
- Result: **181 tests, 181 passed, 0 failed, 0 skipped.** Unity exit code `0`.
  (156 are the embedded `GhostMap.Shared.Tests` suite, unchanged; 25 are new
  Viewer tests: 6 `ViewerTcpServerTests`, 9 `ViewerSceneStoreTests`,
  5 `FixtureLoaderTests`, 4 `ViewerSessionTests`, plus one shared test-suite
  wrapper.)
- Scene build: `GhostMap.Viewer.Editor.ViewerSceneBuilder.BuildScene` and
  `.VerifyScene` both ran via `-executeMethod` in batch mode with exit code 0
  and no "not ready" assertion failures.
- **Outside the test runner**, the real `tools/send_fixture.py` was run
  against a real `ViewerSession` listening on `127.0.0.1:47831` (via a
  throwaway `-executeMethod`, not committed): the Python sender's process
  exited 0, and the Viewer's `ViewerSceneStore.Current` correctly held
  `sessionId=fixture-valid-room-v1`, `revision=12`, `finalized=true`,
  `4 corners / 1 opening / 3 objects` — exactly `fixtures/valid-room-v1.json`'s
  contents — confirming the real Python fixture sender and the real
  `ViewerTcpServer` interoperate over an actual TCP socket, not just the
  in-process `FakeScannerClient` test harness.
- Editor: Unity `6000.3.24f1`. Test framework `1.6.0`.
- Test-driven within this task: `ViewerTcpServer`'s bounded-line framing was
  exercised first with a hand-built oversized line and found to have a real
  off-by-one bug (the oversized check only ran *between* reads, so a final
  read chunk carrying both the overflow bytes and the terminating newline
  together skipped it) before the fix landed; see `LineReader.TryReadLine`.

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

## Known issues
- None blocking V2. `Runtime/Rendering/`, `Runtime/Interaction/` and
  `Runtime/Persistence/` are still empty — that is V2-V6's scope, not a defect.
- The "Load fixture" button resolves its fixture path relative to the repo
  root via `Application.dataPath`, which only works for in-Editor development
  (as V1 explicitly targets); a standalone player build would need a
  different fixture-location strategy, which is not this task's concern.
- No physical-device testing applies to this workstream (desktop app).
- The Viewer scene has no camera or 3D rendering yet — `Canvas` is
  `ScreenSpaceOverlay`, needing no `Camera`. V2 introduces the first rendered
  geometry (floor/ceiling/walls) and V4 introduces the orbit camera.

## Next safe task
- **V2 — Floor, ceiling, walls.** `RoomRenderer` should listen to
  `ViewerSceneStore.Changed` and do a full rebuild per snapshot (acceptable
  for MVP scale per the implementation plan).

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
  `docs/decisions/**` — owned by the Shared/Integration workstream.
- `apps/scanner/**` — owned by the Scanner workstream.
