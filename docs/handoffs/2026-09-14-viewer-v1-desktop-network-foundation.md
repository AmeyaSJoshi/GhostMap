# Handoff

## Branch
`viewer/v1-desktop-network-foundation`

## Base commit
`9996e5b` (merge of PR #6, `scanner/s6-network-finalization` -> `main`; Scanner S1-S6 complete)

## Head commit
See the next handoff/commit that records this task's SHA (per this repo's
convention of recording the commit SHA in a follow-up docs commit).

## What changed
Task V1 from `docs/plans/ghostmap-implementation-plan.md` section 17: the
Viewer's Unity project, TCP server, and fixture ingestion.

- **New Unity project** at `apps/viewer/`: created via
  `Unity -batchmode -createProject`, then curated to the packages implementation
  plan section 2.3 specifies — `com.ghostmap.shared` (local, file-referenced),
  `com.unity.ugui`, `com.unity.test-framework`, and the `jsonserialize`/
  `imgui`/`ui` core modules. No AR, no third-party networking/JSON packages.
- `Runtime/Networking/LineReader.cs` — a hand-rolled bounded-line reader over
  a raw `Stream`. Not `StreamReader.ReadLine()`, which buffers an unterminated
  line without limit; this caps the buffer at
  `ProtocolConstants.MaxLineLengthBytes` and discards-and-resyncs to the next
  newline once exceeded, so one oversized line cannot exhaust memory or wedge
  the connection.
- `Runtime/Networking/ViewerTcpServer.cs` — listens on port 47831, accepts one
  scanner connection at a time (a new connection always closes and replaces
  the old socket), parses every line through the shared `ProtocolSerializer`,
  and queues parsed messages for main-thread dispatch via
  `TryDequeueMessage`. A malformed line or an oversized line is logged to
  `LastError` and the connection continues — never torn down over one bad
  message, per protocol v1 section 4's "a malformed line is a normal event."
- `Runtime/Scene/ViewerSceneStore.cs` — the single authority for whether an
  incoming snapshot replaces `Current`: revision arbitration runs through the
  shared `SnapshotRevisionPolicy` exclusively, and a snapshot must also pass
  `RoomValidator.ValidateRoom` plus `OpeningValidator`/`FurnitureValidator`
  per opening/object before it is applied. No path clears `Current` other
  than accepting a newer valid snapshot.
- `Runtime/Scene/FixtureLoader.cs` — loads a bare `SceneSnapshot` fixture file
  (not a wire-wrapped message) directly with `JsonUtility`, for the "Load
  fixture" developer button.
- `Runtime/Bootstrap/ViewerSession.cs` — the plain-C# session state machine
  (deliberately not a `MonoBehaviour`, mirroring
  `ScannerNetworkClient`'s own reasoning) owning one `ViewerTcpServer` and one
  `ViewerSceneStore`; `Pump()` drains and dispatches queued messages.
  `Runtime/Bootstrap/ViewerBootstrap.cs` is the thin `MonoBehaviour` wrapper.
- `Runtime/UI/ViewerHudController.cs` — renders connection/session/revision/
  phase/finalized diagnostics and wires the Load Fixture button.
- `Editor/ViewerSceneBuilder.cs` — builds and saves
  `Assets/GhostMap/Viewer/Viewer.unity` (`GhostMap/Build Viewer Scene` menu
  item) and `VerifyScene()` asserts every component is wired, mirroring
  `ScannerSceneBuilder`.
- `Tests/EditMode/`: `FakeScannerClient` (a real loopback `TcpClient` test
  helper, the Viewer-side mirror of the Scanner's `FakeViewerListener`),
  `ViewerTcpServerTests`, `ViewerSceneStoreTests`, `FixtureLoaderTests`,
  `ViewerSessionTests` — 25 new tests, all against real TCP sockets and the
  real fixture files in `fixtures/`, never a mock.
- `docs/status/viewer.md` updated; this handoff created.

## Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no
`apps/scanner/**` file was touched. Every message type and field V1 consumes
already existed in protocol v1 and scene schema v1; V1 is the first task to
actually open a listening socket and apply snapshots on the viewer side.

## How to test
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/viewer \
  -runTests -testPlatform EditMode \
  -testResults /tmp/viewer-tests.xml -logFile /tmp/viewer-tests.log
```

To exercise the real Python fixture sender against a running Viewer, open
`Assets/GhostMap/Viewer/Viewer.unity` in the Editor, press Play, then:
```bash
python3 tools/send_fixture.py --host 127.0.0.1
```
The on-screen status text should show `Connection: Connected`, the
`fixture-valid-room-v1` session id, revision 12, and `FINALIZED`.

## Test results
- **181 tests, 181 passed, 0 failed, 0 skipped.** Unity exit code `0` (156
  unchanged `GhostMap.Shared.Tests` + 25 new Viewer tests).
- `ViewerSceneBuilder.BuildScene` and `.VerifyScene` both ran via
  `-executeMethod` with exit code 0 and no wiring assertion failures.
- A throwaway `-executeMethod` (not committed) ran the real
  `tools/send_fixture.py` against a real `ViewerSession` on
  `127.0.0.1:47831`: the sender exited 0, and the Viewer's
  `ViewerSceneStore.Current` correctly held `sessionId=fixture-valid-room-v1`,
  `revision=12`, `finalized=true`, 4 corners, 1 opening, 3 objects — an exact
  match to `fixtures/valid-room-v1.json` — over a real socket, not the
  in-process test harness.
- One real bug was found and fixed during this task: `LineReader`'s
  oversized-line detection only ran *between* two `Stream.Read()` calls, so
  when the terminating newline arrived in the same read as the bytes that
  pushed the buffer past the cap, the oversized check was skipped entirely
  and the full over-limit line was handed to `ProtocolSerializer`, which
  rejected it with a different, less specific message. Fixed by also
  checking the found line's length at the moment a newline is located, not
  only via the between-reads flag. Caught by
  `OversizedLineIsRejectedAndFramingResyncsForTheNextLine`, which failed with
  the wrong-message assertion before the fix and passes after it.

## Known failures
- None.

## Files most important to read next
- `docs/status/viewer.md` — full V1 design notes and known issues.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Networking/ViewerTcpServer.cs`
  and `LineReader.cs` — the bounded-line framing V2+ will build on.
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Scene/ViewerSceneStore.cs` — V2's
  `RoomRenderer` should subscribe to `Changed`.

## Next task
- **V2 — Floor, ceiling, walls** (implementation plan section 17, Task V2).
  Do not begin Scanner or Integration work from here.
