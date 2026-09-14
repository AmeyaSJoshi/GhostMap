# Handoff

## Branch
`scanner/s6-network-finalization`

## Base commit
`2bb4899` (merge of PR #5, `scanner/s5-openings-furniture` into `main`)

## Head commit
`96b5de0`

## What changed
- `Runtime/Networking/ScannerNetworkClient.cs` (new): protocol v1's TCP
  client — connect, `hello` then the current snapshot, heartbeat every 2 s,
  reconnect every 2 s resending the latest snapshot. Plain C#, no Unity
  dependency, driven by `PumpOnce()` so it can run on a real background
  thread on device and be pumped directly and deterministically (with a fake
  clock) from an EditMode test.
- `Runtime/Networking/ISnapshotSink.cs` (new): the narrow interface
  `ScannerSnapshotPublisher` needs from a network client, so its own tests
  run against a fake sink instead of a real socket.
- `Runtime/Networking/ScannerSnapshotPublisher.cs` (new): watches
  `ScanWorkflowController` for a revision change or a transition to
  `Finalized` and turns each into the wire message(s) protocol v1 requires,
  batching the final `scene.snapshot` and the following `scan.finalized`
  together so a concurrently-draining sink can never reorder them.
- `Runtime/Workflow/ScanWorkflowController.cs`: adds `FinalizeRejection` and
  `TryFinalize()` — `ReadyToFinalize -> Finalized`, `SceneSnapshot.finalized`
  set, revision incremented once more. No new mutation guard was needed:
  every existing mutating method already gates on a specific phase, none of
  which is `Finalized`.
- `Runtime/UI/FloorLockHud.cs`: adds `ResetScan()`, refactoring the
  controller-graph construction already in `Awake()` into a reusable
  `BuildWorkflow()` so Reset can rebuild a fresh session without duplicating
  the wiring.
- `Runtime/UI/ScannerHudController.cs` (new): the S6 scene-facing shell —
  laptop IP/port fields, Connect, a live network-status line, Reset,
  Finalize, and one consolidated status line (phase, tracking, closure
  error, finalized). Owns the background thread that drives
  `ScannerNetworkClient.PumpOnce()` on device.
- `Runtime/UI/CornerCaptureHud.cs`: its readout now hides itself outside
  `CaptureCorners`/`VerifyClosure`, matching what `HeightCaptureHud`/
  `OpeningCaptureHud`/`ObjectPlacementHud` already did — the world-space
  corner/closure markers are unaffected, so no device-verification
  capability is lost.
- `Editor/ScannerSceneBuilder.cs`: builds the S6 UI (top-anchored status
  line, network status line, host/port fields, Connect/Reset/Finalize
  buttons) and extends `VerifyScene()` to assert `ScannerHudController` is
  present and fully wired.
- `Editor/GhostMap.Scanner.Editor.asmdef`: adds a direct reference to
  `GhostMap.Shared` (the scene builder now references
  `GhostMap.Shared.Protocol.ProtocolConstants` directly; Unity assembly
  references are not transitive).
- `Assets/GhostMap/Scanner/Scanner.unity`: regenerated via
  `GhostMap/Build Scanner Scene` to include the S6 GameObjects.
- New tests: `Tests/EditMode/ScannerNetworkClientTests.cs` (8, against a
  real loopback `FakeViewerListener`), `Tests/EditMode/ScannerSnapshotPublisherTests.cs`
  (8, against a `FakeSnapshotSink`), plus test-only helpers
  `FakeViewerListener.cs`, `FakeSnapshotSink.cs`. Extended
  `Tests/EditMode/ScanWorkflowControllerTests.cs` (+6 finalization tests).

## Contract impact
- None. No file under `shared/**`, `fixtures/**`, `tools/**`,
  `docs/contracts/**` or `docs/decisions/**` was touched, and no
  `apps/viewer/**` file was touched. Every message type, field and timing
  constant S6 uses already existed in protocol v1; S6 is the first task to
  actually open a socket and drive them.

## How to test

### Automated (already run — see Test results)
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s6-run4.xml -logFile /tmp/ghostmap-s6-run4.log
```

### Physical device (NOT yet run — see `docs/status/scanner.md`'s
"Physical-device verification needed — S6" for the full procedure)
1. Start a bare TCP listener on the laptop: `nc -l 47831`.
2. Build with `ScannerBuild.ConfigureXr` then `ScannerBuild.BuildScanner`
   (two separate Unity invocations, each needing `-quit` when run from the
   command line via `-executeMethod` — batchmode does not exit on its own
   otherwise), deploy to a real iPhone.
3. Type the laptop's LAN IP into **Laptop IP**, tap **Connect**; confirm
   **Network status** reads `Connected` and `nc` prints a `hello` line then
   a `scene.snapshot` line.
4. Run S1-S5 as before through `ReadyToFinalize`; confirm a new
   `scene.snapshot` line appears in `nc` after every mutation, with a
   strictly increasing `revision`, and a `heartbeat` line roughly every 2 s
   of idle time.
5. Tap **Finalize GhostMap**; confirm **Scan status** reads `FINALIZED` and
   `nc` shows one more `scene.snapshot` (with `"finalized":true`) immediately
   followed by a `scan.finalized` line.
6. Kill and restart `nc` mid-scan and again after finalizing; confirm
   **Network status** cycles `Connected -> Retrying -> Connected` and the
   first lines after each reconnect are `hello` then a `scene.snapshot`
   carrying the *current* (not reset) revision — and the finalized one after
   finalization.
7. Tap **Reset**; confirm the phase returns to `Boot`, a fresh floor lock is
   required, and (if connected) `nc` shows a new `scene.snapshot` with a
   different `sessionId` and revision 0.

## Test results
- **361 tests, 361 passed, 0 failed, 0 skipped.** Unity exit code 0.
  Breakdown: `ScannerNetworkClientTests` 8 (new), `ScannerSnapshotPublisherTests`
  8 (new), `ScanWorkflowControllerTests` 55 (49 + 6 new), plus all
  290 pre-existing Scanner/Shared tests unchanged. S5 finished at 339.
- The shared package, run standalone in `shared/TestProject`: **156/156**,
  unchanged from S5 — confirms S6 touched nothing under `shared/`.
- `ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0;
  `libUnityARKit.a` was verified present in the generated Xcode project.
- `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO` reported **BUILD SUCCEEDED**, with only the same
  stock toolchain warnings prior tasks already recorded.
- **The S6 physical-device test has NOT been run.** Per `AGENTS.md` rule 12,
  S6 is not considered done until it is. See "How to test" above and
  `docs/status/scanner.md`'s "Physical-device verification needed — S6" for
  the exact procedure.

## Known failures
- None found in automated testing. See `docs/status/scanner.md`'s
  "Scene / runtime — new in S6" for known scope decisions (phone-pose
  streaming deliberately not implemented; the bottom S2-S5 button/readout
  stack was not fully redesigned into one screen; Reset has no confirmation
  step; a connect attempt to an unreachable host can block the background
  networking thread, never the UI thread, for the platform's TCP connect
  timeout) — all deliberate, none of them bugs.

## Files most important to read next
- `docs/status/scanner.md` — "Physical-device verification needed — S6" for
  the exact procedure to run next, and "Task S6 — networking and
  finalization" for the full design rationale.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Networking/ScannerNetworkClient.cs`
  and `.../ScannerSnapshotPublisher.cs` — the actual networking/publishing
  logic.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Workflow/ScanWorkflowController.cs`
  — the `TryFinalize()` addition.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/UI/ScannerHudController.cs`
  — the S6 HUD and background-thread wiring.

## Next task
- **Physical-device verification of S6**, exactly as described in
  `docs/status/scanner.md`'s "Physical-device verification needed — S6".
  A bare `nc -l 47831` on the laptop is sufficient; the Viewer does not need
  to exist yet. Once that passes, the Scanner-side MVP (S1-S6) is complete.
  Do not begin Viewer or Integration work from here.
