# R9: Hardening and the guided flow

**Plan:** section 18, task `R9`. **Workstream:** Scanner.
**Depends on:** `R2` (real failures seen on the device), `R7` (Send to
Computer exists), `R8` (`room.html` exists).
**Who:** an agent writes fixes and tests; the owner reproduces each case on the
iPhone.

> **Agent part built 2026-10-11** on `scanner/r9-hardening` (handoff
> `docs/handoffs/2026-10-11-scanner-r9-hardening.md`). What was done, row by row:
>
> | Item | Result |
> | --- | --- |
> | Poor tracking | Every capture path already refused; tests now drive height, an opening, placement and a detected surface through the workflow with tracking lost (refused, no revision). **Bug found and fixed:** accepting an already-detected surface, and Add All, still worked with tracking lost |
> | Fewer than four walls | After a full turn with 0-2 walls the guide says "Found 2 of 4 walls. Tap Help GhostMap to trace them by hand." (or "No walls found yet. ...") after 6 s instead of 30 s. Three walls keep pointing at the missing one |
> | Detector finds nothing | With a detector running, the furniture step first says "Look slowly around at your furniture..." and after 10 s "Nothing recognised yet. Place furniture by hand, or tap Next if there is none." |
> | Share cancelled | Already covered by `ShareControllerTests` (`R7`) |
> | App backgrounded | `TrackingResumeGate` in `ArSpatialProvider`: after `OnApplicationPause`, tracking counts as lost until ARKit has tracked well for 1 s; the guide says "Welcome back. Hold the phone steady..." |
> | Storage full | Already handled in `R6`; the disk-full check is now public and tested, and a failed save is tested to leave nothing behind |
> | Debug text | `S6 diag` removed; the S1 diagnostics show only in the Editor, Development builds, or after tapping Details five times within 3 s (`DeveloperMode`) |
> | Steps | Six: Floor, Room, Height, Openings, Furniture, Send (`ScanGuide.StepNames`); the corner check is part of Room |
> | Reticle | 108 px (was 84), centre dot 18 px, with a dark halo behind both; scene regenerated |
> | Haptics | `CaptureHaptics` behind `IHaptics`: one light tap (`UIImpactFeedbackGenerator`, plugin `GhostMapHaptics.mm`) per frame in which the scan changed |
> | **Demo mode** | **Bug found and fixed:** demo mode reported no AR planes, so its automatic scan never saw a wall. `SimulatedPlanes` now reports the demo room's walls, door and window frames, ceiling, floor and furniture tops, each once looked at |
>
> Running the now-working demo end to end in the iOS Simulator found three more
> bugs, all fixed with regression tests:
>
> 1. **Detected furniture was turned a quarter turn** (since `R1`, ADR-0006).
>    A plane's width runs along its local X, but the yaw was the bearing of
>    that axis, which puts a `SceneObjectModel`'s width a quarter turn away.
>    Every non-square surface (a bed, a desk) came out rotated; the demo bed
>    went through a wall. `YawFromSurface` now subtracts 90 degrees. The old
>    test planes shared the mistake, so the new test checks the added object
>    against the plane's real rectangle.
> 2. **Detected doors and windows were lost when ARKit missed the ceiling.**
>    They were only added when the scan also measured the height; now they
>    are added as soon as the height is measured or typed.
> 3. **The Simulator build did not link Core ML and Vision** (Debug builds do
>    not auto-link them; the Release device build happened to).
>    `ScannerIosPostBuild` now adds Core ML, Vision, Core Image and UIKit to
>    `UnityFramework` explicitly.
>
> Left for the owner: every row on the iPhone.

## Goal

A first-time user gets from **Start Scan** to **Send to Computer** with no debug
text on screen, and every predictable failure says what happened and what to do.

## Failure cases

| Case | Expected | What exists today | Work |
| --- | --- | --- | --- |
| Poor light or fast motion | Capture refused, clear tracking message | `ISpatialProvider.IsTrackingGood` gates corner capture (`CornerCaptureController`); `ScanGuideContext.TrackingGood` feeds the guide; phase `WaitingForTracking` | Check every capture path checks it (auto scan, sweep, height, openings, furniture), not only corners. One test per controller: tracking bad → capture refused, no revision |
| ARKit finds fewer than four walls | Name the missing direction, offer sweep | `AutoRoomScanController` sets `AutoScanState.MissingWall` with a bearing message (`DescribeBearing`) when ≥ 3 walls are trusted | Confirm on device; cover 2-wall and 0-wall cases with clear fallbacks to the sweep |
| Detector finds nothing | Manual placement offered | Manual placement exists (`ObjectPlacementController`, `ObjectPlacementHud`) | After N seconds with no proposals, guide text points to the manual Place row |
| Share sheet cancelled or AirDrop fails | Scan kept; button works again | `R7` adds `ShareResult` | Test: cancel → scan still listed, button enabled |
| App backgrounded mid-scan | Tracking must recover before any capture | **Nothing.** No `OnApplicationPause`/`OnApplicationFocus` handler in the scanner | Add a handler that marks tracking as not good until ARKit reports tracking again; test with the fake provider |
| Storage nearly full | Export fails with a plain message, scan state kept | `R6` writes atomically | Catch `IOException` around the save; message "Not enough space on the iPhone to save this room."; test with a store that throws |

Every reproducible software failure gets an EditMode regression test
(`AGENTS.md` rule 11). Device-only failures are recorded in
`docs/status/scanner.md` with the build and date.

## Polish

- **Remove debug text from the release flow:**
  - `ScannerHudController.AppendDiagnostics` (the `S6 diag: hud.enabled=...`
    line) and its call.
  - The S1 diagnostics block: `ScannerBootstrap.Update` writes XR, session,
    tracking-chain and plane diagnostics into `DiagnosticsText`, which
    `ScannerSceneBuilder` creates under the details panel. Keep the code behind
    a developer toggle (for example a hidden long-press) or delete it; do not
    show it by default.
- **Steps:** `ScanGuide.StepCount` is 7 today. The plan wants
  Floor → Room → Height → Openings → Furniture → Send. Map the phases
  (`ScanPhase.cs`) to these six labels in `ScanGuide` only (it is the single
  owner of wording), and update `ScanGuideTests`.
- **Reticle:** larger and higher contrast; one material in `MarkerMaterials`.
- **Haptics on capture:** iOS has no Unity API for light haptics beyond
  `Handheld.Vibrate()` (too strong). Either accept `Handheld.Vibrate` or add a
  tiny `UIImpactFeedbackGenerator` call to the `R7` share plugin's `.mm`. Put
  it behind an `IHaptics` seam so tests can assert a capture fired one pulse.

Scene changes go through `ScannerSceneBuilder` and are regenerated by the owner
in Unity (**GhostMap → Build Scanner Scene**); never hand-edit `Scanner.unity`.
Run `ScannerSceneTests` after.

## Device check (owner)

Run each failure case on the iPhone and tick it in `docs/status/scanner.md`.
Then run the plan's section 23 acceptance test once end to end.

## Done when

All failure rows pass on the device; no debug text is visible in a normal scan;
EditMode tests pass in CI; status and a handoff are updated.
