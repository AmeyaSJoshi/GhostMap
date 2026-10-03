# Handoff

## Branch
`integration/ui-pr16-device-test`

## Base commit
`b2b56e3` (`scanner/ui-overhaul`), merged with PR #16 head `539f026`
(`integration/sweep-and-furniture`).

## Head commit
`9733e4c` (merge `c57718a`, regenerated scenes `9733e4c`).

## What changed
- Merged PR #16 into the guided-scan UI overhaul. Only
  `ScannerSceneBuilder.cs` conflicted textually: PR #16 placed detection
  controls by pixel in the old debug layout. Kept the overhaul's builder and
  added PR #16's requirements in its idiom: `planeManager` on
  `ArSpatialProvider`; a `DetectRow` (Is:<type> / Skip / Next / Add) above
  the manual Place row; the detection readout in Details; `FurnitureDetectionHud`
  built and asserted by `VerifyScene`.
- Fixed compile breaks git could not see: `ScanWorkflowSweepTests` and
  `ScanWorkflowDetectionTests` (broken on PR #16 itself) and `ScanGuideTests`
  used the six-argument `ScanWorkflowController` constructor.
- `FurnitureDetectionHud`: buttons only while a candidate exists (row
  collapses, manual screen unchanged otherwise); unlit markers like the other
  HUDs; an Xcode log line per Add.
- `ScanGuide`: offers a detected surface with its measured width, depth and
  top height. Four new `ScanGuideTests`.
- Observability: per-wall sample count and corner-to-corner wall lengths in
  Details; `GhostMap sweep:` / `GhostMap closure:` / `GhostMap detect:` lines
  in the Xcode console for each committed wall, Build Room, Check Corner and
  detected Add.
- Both scenes regenerated with their builders; diffs are additive only.

## Contract impact
- None. No schema, protocol or shared change beyond what PR #16 already
  carries (`WallFitting`, `RoomGeometry.ContainsPointXZ`).

## How to test
1. `GhostMap/Build Scanner Scene` and the viewer builder are already run;
   `VerifyScene` passes for both.
2. Scanner EditMode with `-buildTarget iOS`, Shared, Viewer EditMode.
3. `ScannerBuild.ConfigureXr`, `ScannerBuild.BuildScanner`, then open
   `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`, set signing, run on an iPhone.

## Test results
- Shared: 197 total, 197 passed, 0 failed, 0 skipped.
- Scanner (iOS target): 524 total, 524 passed, 0 failed, 0 skipped.
- Viewer: 542 total, 542 passed, 0 failed, 0 skipped. (PR #16 predicted
  501; the viewer embeds the shared package, so it also runs the 41 new
  shared tests. `ScanWorkflowSweepAndDetectionTests` has 9 tests, not 10.)
- `ScannerBuild.BuildScanner`: success; `libUnityARKit.a` present;
  Info.plist has camera and local-network usage strings and `arkit`.
- `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO`: BUILD SUCCEEDED.

## Known failures
- None in automated suites.

## Known limitations
- No physical-device test of sweep or detection yet.
- Demo mode (Editor/Simulator) reports no detected planes, so detection
  cannot be exercised there.
- Everything in the two PR #16 feature handoffs still applies.
- ADR numbering: this branch has no collision, but unmerged
  `docs/one-button-computer-transfer` and `integration/i1-one-button-transfer`
  also use ADR-0005/ADR-0006. Whichever merges second must renumber.

## Next task
- Physical iPhone session: sweep + detection, tape-measure everything, desk
  against a wall in a swept room, then Viewer + `.glb` in Blender.
