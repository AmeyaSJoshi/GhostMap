# Handoff

## Branch
`scanner/s6-network-finalization`

## Base commit
`82d27ef` (S6 implementation, `docs(handoffs): record S6 implementation commit sha`)

## Head commit
`<filled in by the follow-up "docs(handoffs): record S6 diagnostics commit sha" commit>`

## What changed
A first physical-device pass of S6 reached `ReadyToFinalize` (confirmed by
the S5 object controls correctly hiding themselves) but showed no
**Finalize GhostMap** button and none of the S6 controls besides Connect.

Investigation, not a code fix: a ground-truth dump of the actual committed
`Scanner.unity` (opened in the Editor, walking the real GameObject hierarchy
via a throwaway diagnostic script rather than re-reading the scene-builder
source) found `ScannerHudController` active and enabled, every serialized
reference resolved, and `FinalizeButton` `activeSelf=true`/
`activeInHierarchy=true`, positioned at a reasonable on-canvas offset, and
at the *highest* sibling index under `Canvas` of anything `ScannerSceneBuilder`
creates — meaning it renders on top of every S1-S5 panel, not behind one.
No defect was found in the scene or its wiring.

- `Runtime/UI/ScannerHudController.cs`: `BuildStatus()` now appends a final
  `S6 diag: hud.enabled=... finalizeBtn.active=... .interactable=...
  resetBtn.active=...` line, read directly off the phone's own screen. Its
  presence or absence alone proves whether the *running* build actually
  contains this component (ruling out — or confirming — a stale
  build/deploy, the leading hypothesis for what was observed) without
  needing an attached Xcode console.
- `Assets/GhostMap/Scanner/Scanner.unity`: regenerated via
  `GhostMap/Build Scanner Scene` to pick up the diagnostics change.
- `docs/status/scanner.md`: records the investigation, the diagnostics
  addition, and an explicit instruction for the next device pass to do a
  clean Xcode rebuild (delete the app from the phone, or at minimum
  Product > Clean Build Folder) before testing again, to eliminate any
  possibility of a cached/stale binary.

No change was made to `ScanWorkflowController`, `ScannerNetworkClient`,
`ScannerSnapshotPublisher`, or the scene-building logic itself — nothing
there was found broken.

## Contract impact
- None.

## How to test

### Automated (already run — see Test results)
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s6fix-run.xml -logFile /tmp/ghostmap-s6fix-run.log
```

### Physical device (NOT yet run against this build)
1. **Delete the previous GhostMap Scanner app from the iPhone**, or at
   minimum do Product > Clean Build Folder in Xcode, then open the
   freshly-regenerated `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj` and
   build+deploy from it.
2. Follow `docs/status/scanner.md`'s "Physical-device verification needed —
   S6" procedure exactly.
3. Once `ReadyToFinalize` is reached, read the **Scan status** block on
   screen. It must end with a line reading
   `S6 diag: hud.enabled=True finalizeBtn.active=True .interactable=True ...`.
   - If that line is **missing entirely**: the deployed build is still
     stale — stop and redeploy, do not report a Scanner UI bug.
   - If it is present and every value reads `True`: **Finalize GhostMap**
     should be visible and tappable directly above it in the Reset/Finalize
     row: tap it.
   - If it is present but a value reads `False` or `NULL`: report the exact
     line text — it pinpoints the actual broken link precisely.

## Test results
- **361 tests, 361 passed, 0 failed, 0 skipped.** Same suite, same count as
  the prior S6 run — the diagnostics addition changes no test's
  expectations.
- Shared standalone: **156/156**, unchanged.
- `ScannerBuild.ConfigureXr` / `BuildScanner`: both exited 0; `Builds/iOS`
  regenerated fresh.
- `xcodebuild ... Release -sdk iphoneos CODE_SIGNING_ALLOWED=NO`: **BUILD
  SUCCEEDED**.
- **Physical-device verification against this build has NOT been run.**

## Known failures
- None identified in the Scanner/Shared codebase. The originally reported
  symptom (no Finalize button visible) remains unexplained from the Mac
  side; the on-screen diagnostics exist specifically to resolve that
  ambiguity on the next device pass.

## Files most important to read next
- `docs/status/scanner.md` — "First device attempt — inconclusive, no code
  defect found" and the updated "Physical-device verification needed — S6"
  procedure.
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/UI/ScannerHudController.cs`
  — the new `AppendDiagnostics` method.

## Next task
- Re-run physical-device verification with a **clean** Xcode
  build/redeploy, and report exactly what the `S6 diag: ...` line reads at
  `ReadyToFinalize`. Do not begin Viewer or Integration work.
