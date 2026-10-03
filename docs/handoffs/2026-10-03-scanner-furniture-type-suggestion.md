# Handoff

## Branch
`integration/ui-pr16-device-test`

## Base commit
`169c3fb`

## Head commit
See `git log` (the ADR-0007 feat and docs commits following `169c3fb`).

## What changed
- ADR-0007: detected furniture gets its type pre-selected from ARKit's plane
  label (`ARPlane.classifications`: Table / Seat) plus measured size.
- `DetectedSurface` and `FurnitureCandidate` carry `PlaneClassifications`;
  `ArSpatialProvider` passes `plane.classifications`.
- New `FurnitureTypeSuggester` (scanner `Capture`, plain C#).
- `FurnitureDetectionController` pre-selects the suggestion and re-applies it
  each refresh; a user's own `SetType` choice always wins for that surface.
- Guide: "Looks like a desk at the yellow dot. Tap Add." Details and the
  `GhostMap detect:` log show the ARKit label and the suggestion.

## Contract impact
- None. Scanner-internal only; no schema, protocol, shared or viewer change.

## How to test
1. Scanner EditMode with `-buildTarget iOS`.
2. `ScannerBuild.ConfigureXr`, `ScannerBuild.BuildScanner`, open
   `apps/scanner/Builds/iOS/Unity-iPhone.xcodeproj`, run on an iPhone.
3. In step 6, look at a desk and a chair; check the suggested type.

## Test results
- Scanner (iOS target): 544 total, 544 passed, 0 failed, 0 skipped
  (+20: 14 `FurnitureTypeSuggesterTests`, 6 controller tests).
- Shared and Viewer unchanged by this task (197/197 and 542/542 at `169c3fb`).
- `BuildScanner` succeeded; unsigned `xcodebuild` Release iphoneos: BUILD SUCCEEDED.
- Scene unchanged (no new serialized fields); `ScannerSceneTests` passes.

## Known failures
- None in automated suites.

## Known limitations
- Not device-verified. ARKit label quality on the iPhone 17 is unknown.
- ARKit has no "bed" label; beds come from size and height alone.
- Glass and dark surfaces may still not be detected; manual Place remains.

## Next task
- Physical run: desk and chair in step 6, confirm the suggested type.
