# Scanner Status

## Current state
- **S6 implemented and passing all automated tests; not yet verified on a
  physical iPhone.** The scanner now has a real TCP client
  (`ScannerNetworkClient`) implementing protocol v1 exactly: connect, `hello`
  then the current snapshot, a heartbeat every 2 s, and a reconnect every 2 s
  that resends the latest snapshot. `ScannerSnapshotPublisher` watches
  `ScanWorkflowController` for a revision change or a transition to
  `Finalized` and turns each into the wire message(s) protocol v1 requires,
  batching a final `scene.snapshot` and the following `scan.finalized`
  together so nothing draining the queue concurrently can reorder them.
  `ScanWorkflowController.TryFinalize()` is the real finalization path:
  `ReadyToFinalize -> Finalized`, `SceneSnapshot.finalized = true`, revision
  incremented once more, and every existing mutation method already refuses
  outside its own phase — none of them is `Finalized` — so structural
  mutations are rejected automatically, with no separate guard needed. A new
  in-app **Reset** (`FloorLockHud.ResetScan()`) rebuilds the entire S2-S5
  controller graph from scratch, producing a fresh session id, room id,
  `Boot` phase and revision 0, without tearing down the underlying AR
  tracking session. `ScannerHudController` is the new networking/finalization
  HUD: laptop IP/port fields, Connect, a live network-status line, Reset,
  Finalize, and one consolidated status line (phase, tracking, closure
  error, finalized). The S2-S5 per-phase readouts now hide themselves
  outside their own phase (`CornerCaptureHud` gained the same behavior its
  S4/S5 siblings already had), which meaningfully de-clutters the screen
  without removing any diagnostic capability — the world-space markers they
  draw are unaffected. See "Task S6" below for the full design and "Tests
  run" for the automated results; see "Physical-device verification needed —
  S6" for exactly what a hardware pass must exercise before S6 can be closed.
- **S5 complete and verified on a physical iPhone.** Doors and windows are
  captured on one of the four S3/S4-derived walls by intersecting the
  center-screen ray with that wall's mathematical plane at a lower-left then
  an upper-right point; furniture is placed by intersecting the center-screen
  ray with the locked floor plane and applying the type's MVP default
  dimensions. Both use the shared `OpeningValidator` / `FurnitureValidator`
  for all validation. The scan phase advances
  `AddOpenings -> AddObjects -> ReadyToFinalize`; S6 owns finalization itself.
  On device: door capture worked and produced physically plausible
  dimensions, window capture worked with the sill correctly reading above
  0, wall selection worked and opening markers stayed on the correct walls,
  furniture placement and its width/depth/height/yaw adjustment controls
  worked, the S2 frame and S3 corner markers stayed fixed throughout, and
  both `Finish Openings` and `Finish Objects` advanced the phase correctly
  (`AddOpenings -> AddObjects -> ReadyToFinalize`). See "Task S5" below for
  the full design and "Physical-device verification — S5, passed" below for
  the complete device-test record.
- Delivered in S5: `OpeningCaptureController`, `ObjectPlacementController`,
  `OpeningCaptureHud`, `ObjectPlacementHud`, the S5 transitions and real
  `openings`/`objects` arrays on `ScanWorkflowController`'s snapshot, and the
  scene builder's wall/type selection, two-point capture, furniture placement
  and adjustment UI. `VerifyScene()` now asserts the S5 wiring too.
- **S4 complete and verified on a physical iPhone.** Room height is captured
  by deriving vertical wall planes from the S3 footprint, intersecting the
  center-screen ray with the wall the user selects, and validating the result
  against 2.0-4.0 m. On device: wall selection worked, aiming at the selected
  wall's ceiling junction produced a valid candidate, **captured height was
  2.69 m**, `Capture Height` advanced the phase to `AddOpenings`, `Frame O/X/Z`
  and the S3 corner markers stayed fixed, and the tracking session stayed
  `SessionTracking` throughout. The measured height agreed with the captured
  value within acceptable tolerance. A manual numeric fallback exists per the
  implementation plan's section 8.7 and was not exercised on this device pass
  (see "Known issues").
- Delivered in S4: `HeightCaptureController`, `HeightCaptureHud`, the S4
  transition and real `heightM` on `ScanWorkflowController`'s snapshot, and the
  scene builder's wall-selection / capture / manual-entry UI. `VerifyScene()`
  now asserts the S4 wiring too.
- **S3 complete and verified on a physical iPhone.** Four-corner capture and
  closure verification work on device: corners are captured from the
  center-screen ray against the locked floor plane, every stored corner lands on
  GhostMap y = 0, markers stay put, the S2 frame does not move, and closure
  measured **0.031 m — Excellent** on the test room.
- Delivered in S3: `CornerCaptureController`, `CornerCaptureHud`, the S3
  transitions and snapshot contents on `ScanWorkflowController`, and the scene
  builder's corner-capture UI. `VerifyScene()` now asserts the S3 wiring as
  well.
- **S2 complete and verified on a physical iPhone.** Floor lock and the GhostMap
  coordinate frame work on device: the frame is established from a confirmed
  floor, it is right-handed in Unity's sense, it stays fixed while the user moves,
  and the floor normalizes to Ghost y = 0.
- Delivered in S2: `ArSpatialProvider` / `ISpatialProvider`,
  `FloorLockController`, `ScanWorkflowController`, `ScanPhase`, and the
  `FloorLockHud` crosshair / Lock Floor button / readout. The scene builder
  produces all of it, and `ScannerSceneBuilder.VerifyScene()` asserts the wiring
  so a null reference fails on a laptop rather than silently on a phone.
- **S1 complete and verified on a physical iPhone.** The scanner Unity project,
  AR smoke-test scene, two-step iOS build pipeline and on-device diagnostics are
  in place, and the Task S1 physical-device test passes.
- `apps/scanner/` is a Unity 6000.3.24f1 project: `Packages/manifest.json`
  (AR Foundation 6.3.1, Apple ARKit XR Plugin 6.3.1, `com.ghostmap.shared`),
  `ProjectSettings`, `Assets/XR` XR Plug-in Management configuration,
  `Assets/GhostMap/Scanner/Scanner.unity`, runtime bootstrap, editor build
  scripts, and EditMode tests.
- Two device failures were found and fixed along the way, each with a distinct
  root cause. Both are recorded below and in their handoffs.

## Last verified commit
- S6 is **implemented and passing 361/361 automated EditMode tests (Scanner +
  embedded Shared), plus a successful `ScannerBuild.BuildScanner` and
  `xcodebuild ... BUILD SUCCEEDED`**, but has **not yet been verified on a
  real iPhone** — see "Physical-device verification needed — S6" below.
  `AGENTS.md` rule 12 applies: do not treat S6 as done until that pass
  succeeds. The commit sha is filled in by the follow-up
  "docs(handoffs): record S6 implementation commit sha" commit, the same
  two-commit pattern S5 used.
- `7274cfe` — S5, verified on a real iPhone. The commit that follows it
  changes only documentation, so its scanner sources are byte-identical.
- `1b8a6ce` — S4, verified on a real iPhone. The commits that follow it change
  only documentation, so their scanner sources are byte-identical.
- `1e04d02` — S3, verified on a real iPhone. The commits that follow it change
  only documentation, so their scanner sources are byte-identical.
- `b0fe6f0` — S2, verified on a real iPhone. `4dd4c13` follows it and changed
  only a handoff document, so its scanner sources are byte-identical.
- `80b5a48` — S1, verified on a real iPhone.
- S1 reached `main` as merge commit `150512d` (PR #1) and S2 as merge commit
  `a3f15f8` (PR #2). Both were merged with a merge commit so the original task
  SHAs stay reachable from the handoff documents that cite them.

## Physical-device verification needed — S6

Not yet run. This is the exact procedure for whoever runs it next.

### What the Scanner UI should show
On launch the screen still carries every S2-S5 element, but each phase's own
button row and readout now hide themselves outside their phase (this was
already true for buttons; S6 added it for the S4/S5/now-S3 readouts too), so
the screen only shows the controls for the phase you are actually in, plus
four things that are **always** visible near the top, just below the S1
diagnostics block:
- **Scan status** — phase, revision, tracking (good/poor + reason), closure
  error, and `FINALIZED`/`Not finalized`.
- **Network status** — `Disconnected` / `Connecting` / `Connected` /
  `Retrying`, the target host:port once one is set, and the last error when
  not connected.
- A **Laptop IP** field, a **Port** field (pre-filled `47831`), and a
  **Connect** button.
- A **Reset** button and a **Finalize GhostMap** button (the latter only
  interactable in `ReadyToFinalize`).

### How to connect it to the laptop/listener
1. On the laptop, start a bare TCP listener on port 47831 so there is
   something for the scanner to talk to — the Viewer does not exist yet.
   The simplest option needs nothing installed: `nc -l 47831` in a terminal
   (macOS/Linux `netcat`). Leave it running.
2. Find the laptop's LAN IP (`ipconfig getifaddr en0` on macOS, or read it
   off the Wi-Fi settings) and confirm the iPhone is on the **same Wi-Fi**.
3. Deploy the Scanner build to the iPhone (see "Build process" in Known
   issues — the two-step `ConfigureXr` then `BuildScanner` is unchanged).
4. On the phone, type the laptop's IP into **Laptop IP**, leave Port at
   `47831`, tap **Connect**.

### How to know connection succeeded
The **Network status** line reads `Connected (<ip>:47831)`, and on the
laptop's `nc` terminal you should immediately see two lines of JSON text
appear (a `hello` line, then a `scene.snapshot` line) — `nc` prints raw text,
so this is a direct, unmediated look at the wire.

### How to know snapshots are being sent
Every structural mutation (locking the floor, a corner, a height, an
opening, an object, undo, `Finish Openings`/`Finish Objects`) should print
one more `scene.snapshot` JSON line in the `nc` terminal, and its `revision`
field should be one higher than the previous line's. Roughly every 2 seconds
of no mutation, a short `heartbeat` line should also appear.

### What to do from floor lock through ReadyToFinalize
Exactly the S2-S5 procedure already documented above (lock floor, four
corners, verify closure, capture height, doors/windows, furniture, `Finish
Openings`, `Finish Objects`) — S6 changes nothing about capture itself.

### What button finalizes the scan
**Finalize GhostMap**, enabled only once `Phase` reads `ReadyToFinalize`.

### What should happen after finalization
- The **Scan status** line changes from `Not finalized` to `FINALIZED`.
- `Phase` becomes `Finalized`.
- The `nc` terminal should show one more `scene.snapshot` line (with
  `"finalized":true` in it) immediately followed by a `scan.finalized` line —
  in that order, in the same burst.
- Every button belonging to an earlier phase should already be hidden (they
  hide themselves once their phase is behind you), and nothing on screen
  should be able to mutate the room further except **Reset**.

### What diagnostics should remain fixed
The S1 diagnostics block, the S2 frame readout (`Frame O`/`Frame X`/`Frame
Z`/`Handedness`), and the S3 corner markers, S4 captured height and S5
opening/object markers in world space — none of these are touched by
networking or finalization and should look exactly as they did at the end of
the S5 device pass.

### Network behavior to test by disconnecting/reconnecting
1. **Kill the `nc` listener** (Ctrl-C) while mid-scan, continue capturing on
   the phone for a bit, then confirm nothing on the phone changes — the room
   keeps building, only **Network status** should flip to `Retrying` (after
   at most one heartbeat interval's delay) with a short OS-level error
   message.
2. **Restart `nc -l 47831`** and wait up to a few seconds: **Network status**
   should return to `Connected`, and the very next lines `nc` prints should
   be a fresh `hello` followed by a `scene.snapshot` carrying the room's
   **current** revision (not revision 0 — the whole scan is not lost).
3. **Turn the iPhone's Wi-Fi off and back on** mid-scan: same expectation as
   above — the phone's own local scan state must be completely unaffected;
   only the network status and eventual reconnect should be visible.
4. **Finalize, then repeat step 1-2**: after reconnecting, the very first
   `scene.snapshot` line `nc` prints should already carry
   `"finalized":true` and `"scanPhase":"Finalized"` — the finalized room
   must survive a reconnect, not just an in-progress one.
5. **Tap Reset** at any point, confirm the phase returns to `Boot` /
   `WaitingForTracking`, a fresh floor lock is required, and (if still
   connected) `nc` shows a new `scene.snapshot` line with a **different
   `sessionId`** and revision 0.

### What failure symptoms to report
- Any Console exception, especially anything naming `ScannerNetworkClient`,
  `ScannerSnapshotPublisher` or `ScannerHudController`.
- **Network status** stuck on `Connecting` for longer than a couple of
  seconds after a valid IP/port (suggests the background thread or the
  socket connect itself is hanging).
- A `scene.snapshot` line that does not parse as JSON, or that is missing a
  trailing newline before the next line starts (framing bug).
- A reconnect whose resent snapshot's `revision` is lower than what was last
  seen before the drop (should be impossible per `SnapshotRevisionPolicy`,
  but this is exactly the failure mode to watch for).
- Any case where a disconnect/reconnect visibly altered the room's geometry,
  the captured height, or the finalized flag.
- Which exact step above the phone was on when something looked wrong, plus
  the **Scan status** and **Network status** line text at that moment.

## Physical-device verification — S5, passed

Observed on a real iPhone against the build produced from `7274cfe`:

- door capture worked and produced physically plausible dimensions
- window capture worked, with the sill correctly reading above 0
- wall selection worked
- opening markers stayed on the correct walls
- furniture placement worked
- furniture width/depth/height/yaw adjustment controls worked
- the S2 frame readout and the S3 corner markers stayed fixed throughout
  openings and furniture capture
- `Finish Openings` advanced `Phase` from `AddOpenings` to `AddObjects`
- `Finish Objects` advanced `Phase` from `AddObjects` to `ReadyToFinalize`

That covers the golden path of the device procedure below, and confirms on
hardware what the automated tests assert off it: the wall-plane and
floor-plane ray intersections land correctly, offset/width/sill/height derive
correctly from the two captured points, the S2 frame and S3/S4 footprint and
height are untouched by S5 capture, and the workflow's forward transitions
fire on the expected user actions.

### Not covered by the S5 device test
The S5 hardware run exercised the golden path: valid door capture, valid
window capture, wall selection, furniture placement and adjustment, and both
`Finish` transitions. These paths are covered by EditMode tests but have
**not** been seen on a phone:

- an opening whose two points fall outside the selected wall's length;
- a window whose lower-left point is aimed below the physical floor line;
- a window/door whose sill + height would exceed the captured room height;
- two overlapping openings on the same wall;
- tapping **Capture Upper-Right** before ever tapping **Capture Lower-Left**;
- undoing a captured opening or a placed object with **Undo Opening** /
  **Undo Object**;
- finishing either phase with zero items captured (continuing without
  openings or without furniture).

None of these is suspected broken — each has a passing, mutation-checked
EditMode test. They are recorded because a passing test is not the same
evidence as a passing phone.

### The S5 device procedure
Build from the working tree at `7274cfe` with `ScannerBuild.ConfigureXr` then
`ScannerBuild.BuildScanner` (two separate Unity invocations — see "Known
issues" / "Build process" below), deploy to a physical iPhone, and drive the
scan through S1-S4 exactly as before (lock floor, capture four corners,
accept closure, capture height). `Phase` should reach `AddOpenings`.

### Door
1. On the `AddOpenings` screen, tap **Wall N/4** until the readout's
   `Wall x/y: <start>-><end>` line and the yellow world-space line on the
   floor both indicate the physical wall you want the door on.
2. Confirm the button reads **Type: door** (tap **Type: door/window** to
   toggle it if it reads `window`).
3. Aim the crosshair at the door's bottom-left corner (where the door meets
   the floor) and tap **Capture Lower-Left**. A blue marker should appear
   there, and the button should now read **Capture Upper-Right**.
4. Aim at the door's top-right corner (the far side, at the top of the
   frame) and tap **Capture Upper-Right**.
5. **Success looks like:** the readout's opening count increases by one, a
   new line appears listing `door offset ... width ... sill 0.00 height ...`,
   and the values are plausible for a real door (width roughly 0.7-1.0 m,
   height roughly 2.0-2.1 m, sill exactly 0.00).
6. If the two points don't form a legal door (e.g. you aimed outside the
   wall, or width is under 0.30 m), the readout shows `Rejected: ...` with
   the reason, the opening count does not increase, and you can just try the
   two points again — no need to reselect the wall or the type.

### Window
Same as the door procedure, with two differences:
- Toggle the type button to **Type: window** first.
- Aim the lower-left point at the window's actual sill (bottom edge above
  the floor, not at the floor itself) and the upper-right point at the
  window's actual top edge.
- **Success looks like:** the new readout line shows a `sill` value that is
  clearly above 0.00 (roughly the real sill height off the floor) and a
  `height` that is the window's real height, not its top-edge distance from
  the floor.
- To confirm the window is on the correct wall: reselect the same wall with
  **Wall N/4** afterward and check the readout's `Wall x/y` line still names
  the wall you aimed at; the opening is stored against that wall's corner
  pair regardless of which wall is currently selected on screen.

### Furniture
1. Tap **Finish Openings** once you're done with doors/windows (zero is
   fine). `Phase` should read `AddObjects`.
2. Tap **Type: bed** (or whichever type shows) repeatedly to cycle through
   the eight MVP types until the one you want is shown.
3. Aim the crosshair at the floor spot where you want the object's center
   and tap **Place Object**. An orange cube marker should appear there.
4. To adjust the object you just placed: **W -/W +**, **D -/D +**,
   **H -/H +** step its width/depth/height by 0.10 m per tap, and
   **Yaw -/Yaw +** step its rotation by 15° per tap. Only the most recently
   placed object is affected.
5. **What should remain fixed while editing:** the frame readout (`Frame O`,
   `Frame X`, `Frame Z`), the four corner markers, and the captured room
   height must not move or change while you place or adjust furniture.
6. Tap **Finish Objects** when done (zero furniture is fine). `Phase` should
   read `ReadyToFinalize`.

### What phase/state should appear at each step
```text
after S4 height capture -> AddOpenings
after Finish Openings    -> AddObjects
after Finish Objects     -> ReadyToFinalize
```
No screen in S5 should ever show `Finalized` — that is Task S6's job.

### Invalid cases worth deliberately testing
- A door/window whose two points land outside the selected wall's length.
- A window whose lower-left point is aimed below the physical floor line.
- A window/door whose sill + height would exceed the captured room height.
- Two openings on the same wall whose spans overlap.
- Tapping **Capture Upper-Right** before ever tapping **Capture Lower-Left**
  (should simply do nothing / be a no-op via the disabled-until-aimable
  button state).
- Placing a piece of furniture, undoing it with **Undo Object**, and
  confirming the room's corner markers and frame readout are unaffected.

### If something fails, send
- A screenshot of the full screen (all four readouts plus buttons) at the
  moment of the failure.
- The exact `Rejected: ...` line from the opening or object readout, if one
  appeared.
- Which wall (`Wall x/y: <start>-><end>`) was selected.
- The room's captured height from the S4 readout, for context on the
  ceiling-rule checks.

## Physical-device verification — S4, passed
Observed on a real iPhone against the build produced from `1b8a6ce`:

- `Phase` reached `CaptureHeight` after the S3 closure
- wall selection worked: cycling `Wall N/4` moved the highlighted wall
- aiming the center crosshair at the selected wall's ceiling junction produced
  a valid candidate height
- **candidate/captured height was 2.69 m**
- `Capture Height` succeeded and advanced `Phase` to `AddOpenings`
- `Frame O`, `Frame X` and `Frame Z` stayed fixed through height capture
- the S3 corner markers stayed fixed through height capture
- `Session` stayed `SessionTracking` throughout
- the measured room height agreed with the captured 2.69 m within acceptable
  tolerance

That covers the golden path of the device procedure in
`docs/handoffs/2026-09-12-scanner-s4-height-capture.md`, and confirms on
hardware what the automated tests assert off it: walls are derived correctly
from the S3 footprint, the ray/plane intersection lands on the real ceiling
line, the S2 frame and S3 corners are untouched by height capture, and a valid
height advances the phase.

**Not exercised on this device pass** — still covered only by EditMode tests:
the manual height fallback and its on-screen-keyboard behavior, an
out-of-range automatic candidate (red marker / rejected capture), selecting a
wall other than the one aimed at, and a non-rectangular room. None of these is
suspected broken; they are recorded because a passing test is not the same
evidence as a passing phone.

## Physical-device verification — S3, passed
Observed on a real iPhone against the build produced from `1e04d02`:

- `Phase` reaches `CaptureCorners` after the floor lock
- all four corners capture successfully, in order
- every stored corner reads GhostMap `y = 0.00`
- markers stay fixed at the locations they were captured at
- `Frame O`, `Frame X` and `Frame Z` unchanged after the floor lock
- closure verification works
- **closure error 0.031 m, classified `Excellent`**
- a successful closure advances the phase to `CaptureHeight`
- `Session` stays `SessionTracking`
- `Handedness` stays `+1.000`
- GhostMap camera coordinates keep updating correctly as the user moves
- **Undo** removes the most recent corner correctly
- **Redo Corners** clears the corner set without changing the locked frame
- a deliberately too-close corner is refused by the spacing validation

That covers every item of the Task S3 device procedure in
`docs/handoffs/2026-09-12-scanner-s3-corner-capture-closure.md`, and confirms on
hardware what the automated tests assert off it: capture is anchored to the
locked floor plane, corners land on Ghost y = 0, the S2 frame does not move
during or after corner capture, and the closure bands behave as specified.

The 0.031 m closure is the strongest on-device evidence available that the frame
did not drift across the scan. A frame that had moved would have surfaced here as
accumulated error rather than as a clean re-aim onto the stored first corner.

## Task S6 — networking and finalization

### The network client
```text
ScannerNetworkClient(latestSnapshotProvider, appVersion, deviceName, nowSecondsProvider?)
  .RequestConnect(host, port)   -> schedules an immediate connect attempt
  .Enqueue(...messages)         -> thread-safe; queued for the next drain
  .PumpOnce()                   -> one step: connect-if-due, else drain+heartbeat
```

`PumpOnce` is the entire state machine and touches only `TcpClient`/
`StreamWriter`/the outgoing queue — never a Unity API — so it can run on a
real background `Thread` on device (`ScannerHudController` starts one in
`Awake`, sleeping 100 ms between calls) and be driven directly, synchronously,
from an EditMode test with a fake clock. This is exactly why the client takes
no dependency on `ScanWorkflowController`: it only ever reads the *current*
snapshot through `latestSnapshotProvider`, at the two moments protocol v1
requires it (`docs/contracts/protocol-v1.md` section 6) — right after connect,
and right after every reconnect. A connection failure, a write failure, or an
unreachable host therefore cannot corrupt or lose the captured room; the next
successful connect just reads whatever the workflow currently holds.

On every fresh connection the outgoing queue is cleared before the handshake:
implementation plan section 1.4's "snapshot synchronization, not event replay"
means only the *latest* snapshot is ever worth sending, so anything still
queued from before a drop is stale by definition once a new connection resends
the current one.

### The publisher
```text
ScannerSnapshotPublisher(sink).Tick()
  revisionChanged = workflow.Revision != lastSeenRevision
  justFinalized   = workflow.Phase == Finalized && !hasSentFinalized
  -> sink.Enqueue(snapshotMessage?, finalizedMessage?)   // one batch, in order
```

Both the final `scene.snapshot` and the following `scan.finalized`
(protocol v1 section 3.5) become true in the exact same `Tick()` — because
`ScanWorkflowController.TryFinalize()` increments the revision and sets
`Phase = Finalized` in one call — so they are handed to the sink as a single
`Enqueue(...)` batch. `ScannerNetworkClient.Enqueue` keeps a batch's messages
adjacent in its queue even while a background thread is concurrently
draining it, which is what actually guarantees the final snapshot is written
to the socket before `scan.finalized` rather than merely usually being first.

`Rebind(workflow)` resets `lastSeenRevision` to an impossible value and
`hasSentFinalized` to false, so `ScannerHudController` calling it whenever
`FloorLockHud.Workflow` changes reference (i.e., after **Reset**) makes the
very next `Tick()` publish the fresh session's revision-0 snapshot, exactly
as any other structural mutation would be.

### Finalization
```text
ScanWorkflowController.TryFinalize()
  Phase must be ReadyToFinalize
  isFinalized = true
  TransitionTo(Finalized)
  Publish()   // revision++, SceneSnapshot.finalized = true, scanPhase = "Finalized"
```

No new guard rejects mutations after finalization: every mutating method
already checks for a specific phase (`AddObjects`, `AddOpenings`,
`CaptureCorners`, ...), none of which is `Finalized`, so they are already
unreachable — confirmed by
`StructuralMutationsAreRejectedAfterFinalization` deliberately trying four of
them (an object placement, an opening capture, a corner capture, a manual
height) plus a second `TryFinalize()` call, all of which fail with
`WrongPhase`.

### Reset
`FloorLockHud.ResetScan()` calls the same `BuildWorkflow()` private method
`Awake()` uses, replacing every S2-S5 controller and the
`ScanWorkflowController` itself with fresh instances. Every other HUD already
reads `floorLockHud.Workflow` freshly every `Update()` rather than caching it
(a design choice S3's `CornerCaptureHud` remarks on), so the swap is picked
up scene-wide on the very next frame with no additional wiring. The
underlying AR session and `ArSpatialProvider` are untouched — only the
scan's logical state (session id, room id, phase, revision, every captured
corner/opening/object) resets, matching implementation plan section 10's
"Reset starts a new AR session and new session ID" without the cost of
actually tearing down ARKit tracking for no reason.

### UI consolidation
`ScannerHudController` adds the connection screen (host/port fields, Connect),
a live network-status line, Reset, Finalize, and one consolidated status line
(phase, tracking, closure error, finalized) — all top-anchored, just below
the S1 diagnostics block, since the S2-S5 bottom button/readout stack already
runs past the 1920-tall reference resolution (see the S5 handoff's "screen is
now extremely crowded" note) and new controls that must stay reachable
regardless of that overflow cannot safely be added to it. Separately, every
S2-S5 phase readout (buttons already did this) now hides itself outside its
own phase — `CornerCaptureHud` gained this in S6, matching what
`HeightCaptureHud`/`OpeningCaptureHud`/`ObjectPlacementHud` already did — which
meaningfully reduces on-screen clutter without touching any diagnostic
capability, since the world-space markers each HUD draws are unaffected by
hiding their readout text. This is a deliberate, scoped consolidation, not the
full single "Door / Window / Furniture / Finish" screen implementation plan
section 19 describes; see "Known issues" for what is intentionally left for
later.

### Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no
`apps/viewer/**` file was touched. Every message type, field and timing
constant S6 uses already existed in protocol v1; S6 is the first task to
actually open a socket and drive them.

## Task S5 — openings and furniture

### Doors and windows
```text
walls  = RoomGeometry.BuildWalls(room)          // from the S3 corners, in order
wall   = walls[selectedWallIndex]               // user-selected, as in S4
plane  = WallGeometry.PlaneFor(wall)            // vertical, through wall.Start
ray1   = provider.GetScreenRay(...)             // lower-left point
ray2   = provider.GetScreenRay(...)             // upper-right point
        -> intersect each Ghost-space ray with plane
        -> convert each intersection to wall-local (u, v) via WallGeometry.ToWallLocal
        -> door:   offset=min(u), width=|Δu|, sill=0,      height=max(v)
        -> window: offset=min(u), width=|Δu|, sill=min(v), height=|Δv|
        -> OpeningValidator.Validate(candidate, room)
        -> append, revision++, republish snapshot
```

Exactly implementation plan section 16 (Task S5): both points come from the
same center-screen ray / `WorldRayToGhost` / wall-plane intersection S4
already established, so no new capture primitive was needed. `OpeningCaptureController`
re-derives walls from the live corners on every access — the same choice
`HeightCaptureController` makes — so an opening's wall can never disagree
with the footprint it came from.

A rejected second point clears the pending first point rather than retrying
it: a failed two-point capture always restarts clean, so a stale first point
measured against different aim never silently survives into the next
attempt.

### Furniture
```text
ray    = provider.GetScreenRay(...)                          // center screen
world  = RayPlaneMath.TryIntersectHorizontalPlane(ray, frame.FloorWorldY)
ghost  = frame.WorldToGhost(world); ghost.y = 0               // forced, as in S3
        -> FurnitureValidator.TryGetDefaultDimensions(type, ...)
        -> new SceneObjectModel { center=ghost, yawDeg=0, ...defaults }
        -> FurnitureValidator.Validate(candidate)
        -> append, revision++, republish snapshot
```

`ObjectPlacementController` never scans a mesh or infers shape from pixels:
every object is `{ type, center, yawDeg, widthM, depthM, heightM }`, exactly
scene schema v1's `SceneObjectModel`. Adjustment (`TrySetWidth`/`TrySetDepth`/
`TrySetHeight`/`TrySetYaw`) always re-validates the whole candidate through
`FurnitureValidator.Validate` and only applies the change if the result would
still be legal — an invalid adjustment leaves the object exactly as it was.

### Validation is the shared package's, not a scanner copy
Both controllers validate through `OpeningValidator.Validate` and
`FurnitureValidator.Validate` exclusively (`AGENTS.md` rules 2 and 3). Wall
containment, the ceiling rule (`sillHeightM + heightM <= room.heightM`,
real now that S4 has captured a non-zero height), opening overlap, and
furniture dimension bounds are each enforced in exactly one place.

### Workflow and state machine
`AddOpenings` and `AddObjects` behave like every earlier phase: selection
(`SelectOpeningWall`, `SetOpeningType`, `SetObjectType`) and the start-point
capture are read-only with respect to the snapshot; only an accepted
end-point capture, an accepted placement, an undo, an accepted adjustment, or
a `FinishAdding*` transition increments the revision and republishes. Both
`FinishAddingOpenings` and `FinishAddingObjects` are legal with zero items —
implementation plan section 16 allows continuing without either — and S5
stops at `ReadyToFinalize`. Actually finalizing (`scan.finalized` on the
wire) is Task S6's networking work, not S5's; `ReadyToFinalize` is reached
and `SceneSnapshot.finalized` stays `false`.

### The S2 frame, S3 corners and S4 height are read, never written
Both new controllers hold `FloorLockController` and (for openings)
`CornerCaptureController` / `HeightCaptureController` only to read `Frame`,
`CopyCorners()` and `HeightM`/`HasCapturedHeight`. Neither has a path to move
the frame, mutate a corner or change the captured height. Tests pin the
frame surviving by reference and the corners/height surviving by value
across opening and object capture.

### Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no viewer file
was touched. `OpeningModel` and `SceneObjectModel` already existed in scene
schema v1 with exactly this meaning; S5 is the first task to actually write
non-empty `openings`/`objects` arrays into a live snapshot.

## Task S4 — height capture

### How a height is captured
```text
walls  = RoomGeometry.BuildWalls(room)          // from the S3 corners, in order
wall   = walls[selectedWallIndex]               // user-selected
plane  = WallGeometry.PlaneFor(wall)            // vertical, through wall.Start
ray    = provider.GetScreenRay(provider.CenterScreenPoint)   // AR world space
ghost  = frame.WorldRayToGhost(ray)             // Ghost-space ray
        -> intersect ghost ray with plane
        -> candidate = intersection.y
        -> reject if candidate < 2.0 or > 4.0
        -> RoomModel.heightM = candidate, revision++, republish snapshot
```

Exactly implementation plan section 8.7: the footprint is already exact, so a
vertical plane through two consecutive Ghost-space corners is the real wall,
without waiting on ARKit to detect a vertical plane or depending on LiDAR.
`HeightCaptureController` never raycasts against a detected plane — the same
reasoning S3's corner capture already established for the floor.

### Wall selection
Walls are derived, never serialized, from the same `RoomGeometry.BuildWalls`
S3 uses: wall 0 is corner 0 -> corner 1, and so on. The HUD auto-selects wall 0
the first time the phase reaches `CaptureHeight`, then lets the user cycle
through the rest with **Wall N/4**. A yellow world-space line is drawn along
the selected wall's floor edge, and a marker is drawn at the current aim
point — green if the candidate height would validate, red otherwise.

### Distinguishing ray rejections
`RayPlaneMath.TryIntersectPlane` collapses "parallel to the plane" and
"intersection behind the camera" into one `false`. Height capture needs to
tell them apart — the plan lists them as separate test cases — so
`HeightCaptureController` re-implements the same two-step check
(`RayPlaneMath.ParallelEpsilon`, then `Plane.Raycast`'s own distance) rather
than calling `TryIntersectPlane` and losing the distinction. This is not a
second implementation of a validation rule (the parity risk `docs/status/shared.md`
warns about); it is exposing which half of one existing check fired.

### Validation and the manual fallback
The 2.0-4.0 m range comes from `RoomValidator.MinRoomHeightM` /
`MaxRoomHeightM` directly — referenced, not restated — so the two can never
drift apart. A rejected candidate leaves `HeightM` and `HasCapturedHeight`
exactly as they were; nothing is clamped.

Per plan section 8.7, "a failed automatic height capture must never block the
demo": `TrySetManualHeight` runs the same validation and is always available
in `CaptureHeight`, independent of wall selection or aim. It is not written to
the wire — scene schema v1 has no manual-entry flag, and adding one is a
contract change this task does not need — so it is HUD-visible only, via
`HeightCaptureController.IsManualEntry`.

### The S2 frame and S3 footprint are read, never written
`HeightCaptureController` holds `ISpatialProvider`, `FloorLockController` and
`CornerCaptureController` only to read from them: `Frame`, `CopyCorners()` and
`IsComplete`. It has no path to move the frame or mutate a corner. Tests pin
the frame surviving by reference and the corners surviving by value across a
height capture.

### Contract impact
**None.** No file under `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**` or `docs/decisions/**` was touched, and no viewer file was
touched. `RoomModel.heightM` already existed in scene schema v1 with exactly
this meaning; S4 is the first task to actually write a non-zero value into it.

## Task S3 — corner capture and closure

### How a corner is captured
```text
ray   = provider.GetScreenRay(provider.CenterScreenPoint)   // AR world space
world = RayPlaneMath.TryIntersectHorizontalPlane(ray, frame.FloorWorldY)
ghost = frame.WorldToGhost(world)
ghost.y = 0                                                 // forced, not assumed
        -> RoomValidator.ValidateNewCorner(existing, ghost)
        -> RoomValidator.ValidateRoom(existing + candidate)
        -> append with a GUID id, revision++, republish snapshot
```

A corner is found by arithmetic, **not** by an AR plane raycast. Plane extents
lag the room, stop at skirting boards and furniture, and are routinely absent at
exactly the corner being aimed at, so requiring a plane hit per corner would make
real rooms uncapturable. The locked floor plane is already known exactly. A test
asserts the plane raycaster is never consulted after the lock.

Validation runs twice on purpose. `ValidateNewCorner` sees one candidate against
its neighbours; it cannot see maximum wall length, footprint area or interior
angles, because those belong to the chain rather than to a corner. `ValidateRoom`
covers the partial chain before four corners and the whole footprint at four.

### Closure bands
`RoomValidator.ClassifyClosure`, boundaries inclusive:

| Error | Band | Result |
| --- | --- | --- |
| `<= 0.08 m` | `Excellent` | proceed to `CaptureHeight` |
| `> 0.08 m`, `<= 0.15 m` | `Acceptable` | proceed, warn |
| `> 0.15 m` | `Rejected` | stay in `VerifyClosure`; Redo Corners only |

### Two decisions recorded
- **Revision is monotonic, including across undo.** The plan's S3 list says
  "undo decrements count/revision". The count is decremented; the revision
  advances like any other mutation. Scene schema v1 has the viewer ignore any
  snapshot whose revision is not greater than the one it holds, so an undo that
  wound the counter back would publish a room the viewer must drop.
- **An accepted closure moves the phase to `CaptureHeight`**, per the plan's
  state machine and its "otherwise proceed". No height-capture behaviour exists;
  that is S4. The S3 HUD offers only Redo Corners there.

### The S2 frame is read, never written
`FloorLockController` refuses a second lock and `GhostCoordinateFrame` has no
setters, so nothing in S3 can move the frame. Five tests pin it: the same object
survives every capture and the closure check, its origin and axes are unchanged,
a stored corner does not shift when the camera moves, a re-lock attempt during
capture is refused, and `RedoCorners` keeps the frame while clearing the
footprint.

## Physical-device verification — S2, passed
Observed on a real iPhone against the build produced from `b0fe6f0`:

- `Phase` reaches `FloorLocked`
- `Session` is `SessionTracking`
- `Handedness: +1.000` — the frame is not mirrored
- `Cam G` exists and updates as the user moves
- stepping right increases GhostMap **x**
- walking forward increases GhostMap **z**
- crouching lowers GhostMap **y**
- `Frame O`, `Frame X` and `Frame Z` stay fixed after the lock
- floor points stay near GhostMap **y = 0**
- returning near the lock spot returns close to the GhostMap origin
- no mirrored-axis behavior observed

That covers every item of the Task S2 device procedure in
`docs/handoffs/2026-09-12-scanner-s2-floor-lock-coordinate-frame.md`, and in
particular confirms on hardware what the automated tests assert off it: the
frame is right-handed, immutable after lock, and immune to `CameraYOffset`.

## The GhostMap coordinate frame — S2
Built at the floor-lock instant, exactly as `GhostCoordinateFrame`'s own
documentation specifies:

```text
up      = Vector3.up
forward = ProjectOnPlane(camera.forward, up).normalized
right   = Cross(up, forward).normalized
origin  = floor raycast hit (world space)
```

`Cross(up, forward)` is +X in Unity's left-handed basis, so the axes satisfy
`Cross(right, up) == forward` — the same relationship Unity's world axes
satisfy. Reversing the cross would mirror every captured room and **nothing
downstream would notice**: area, wall length, closure error and interior angle
are all unchanged by a reflection. Handedness is therefore asserted directly and
printed on the device readout.

The frame is immutable once locked. `GhostCoordinateFrame` has no setters and
`FloorLockController` refuses a second lock.

## CameraYOffset — resolved, no compensation needed
This was deferred at S1 with "decide it when the floor is locked". Resolved: it
needs no compensation, and compensating for it would be a bug.

The AR camera is a child of Camera Offset, whose local Y is `CameraYOffset`
(1.1176) because ARKit reports `TrackingOriginModeFlags.Device`. Detected planes
hang off `XROrigin.TrackablesParent` instead, so it looks as though camera and
planes sit in two spaces 1.12 m apart. They do not. In
`com.unity.xr.core-utils@a8b9003/Runtime/XROrigin.cs`, `OnBeforeRender` (line
611) assigns `TrackablesParent` the pose from `GetCameraOriginPose()` (line 585),
which returns the camera's **parent** — Camera Offset itself. The same offset is
baked into both.

The frame's origin is the floor hit, so `WorldToGhost` subtracts the shared
offset away exactly: the floor lands on Ghost `y = 0` for any offset value and
the camera reads its true eye height above the floor.

**This holds only because every reading is taken in world space.** Mixing in a
*local* position — the camera's `localPosition`, printed as "Cam L" in the S1
diagnostics — would reintroduce the offset. Nothing in `ArSpatialProvider` reads
one. `m_CameraYOffset` is deliberately left at AR Foundation's default.

## Physical-device verification — S1, passed
Observed on a real iPhone against the build produced from `80b5a48`:

- `Active loader: ARKitLoader`
- live camera passthrough works
- `Session` reaches `SessionTracking`
- `Not tracking reason: None`
- `XRInput: running`
- `Cam L` and `Cam W` position change as the phone moves
- camera rotation changes as the phone rotates
- `Cam moved` counter increases
- detected plane count goes above 0
- `floor candidate: yes` when a floor plane is detected

This satisfies the Task S1 physical-device test in
`docs/plans/ghostmap-implementation-plan.md`: camera feed displays, the AR
session reaches tracking, the plane manager reports a floor candidate, and the
screen shows session state, notTrackingReason and camera pose.

## Tests run

### S6 — latest
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s6-run4.xml -logFile /tmp/ghostmap-s6-run4.log
```
**361 tests, 361 passed, 0 failed, 0 skipped.** Unity exit code 0.
`ScannerNetworkClientTests` 8 (new), `ScannerSnapshotPublisherTests` 8 (new),
`ScanWorkflowControllerTests` 55 (49 + 6 new finalization tests), plus all
290 pre-existing Scanner/Shared tests unchanged. S5 finished at 339 (Scanner
sources only; this count also includes the 156 embedded `GhostMap.Shared.Tests`
as S5's own count did).

Covered by the new tests, matching the task's required list: hello/heartbeat/
snapshot/finalized message creation and header stamping; snapshot
serialization carrying corners, height, openings and objects; monotonic
revisions including across finalization; `finalized` flag and `Finalized`
phase set correctly; structural mutations rejected after finalization
(object, opening, corner, manual height, and a second finalize, each
checked individually); an actual loopback TCP connection receiving real
NDJSON — hello then snapshot, each exactly one line; heartbeat timing against
a fake clock; a real connection failure and a real mid-session socket
failure (forced with an abortive `LingerState` close so the peer gets an
immediate RST) each leaving the client `Retrying` with the workflow-standin
snapshot object completely untouched; reconnect resending the same revision,
not resetting it; reconnect after finalization resending the finalized
snapshot; every line parsing as exactly one JSON object with no embedded
newline; and a typical MVP-scale snapshot (4 corners, 1 door, bed+desk+chair)
staying well under the 262144-byte line cap.

`ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0
(`GhostMap: ARKit XR loader for iOS already enabled`, `GhostMap: scanner
Xcode project written to Builds/iOS`, `libUnityARKit.a` verified present),
and `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported **BUILD SUCCEEDED**, with only the same
stock toolchain warnings prior tasks already recorded (see "Toolchain
warnings, stock noise" below).

The shared package was also run standalone in its own host project, to
confirm S6 changed nothing under `shared/`:
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s6-shared.xml -logFile /tmp/ghostmap-s6-shared.log
```
**156 tests, 156 passed, 0 failed, 0 skipped.** Unity exit code 0 — unchanged
from S5.

**Not yet run: the S6 physical-device test.** See "Physical-device
verification needed — S6" above for the exact procedure.

### S5 — latest
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s5-final.xml -logFile /tmp/ghostmap-s5-final.log
```
**339 tests, 339 passed, 0 failed, 0 skipped.** Unity exit code 0.
`OpeningCaptureControllerTests` 20 (new), `ObjectPlacementControllerTests` 17
(new), `ScanWorkflowControllerTests` 49 (39 + 10 new S5 tests),
`CornerCaptureControllerTests` 37, `HeightCaptureControllerTests` 18,
`GhostFrameTests` 20, `FloorLockControllerTests` 17,
`ScannerXrSettingsTests` 4, `ScannerSceneTests` 1, `GhostMap.Shared.Tests`
156. S4 finished at 292.

Mutation-checked rather than merely observed passing:

| Mutation | Result |
| --- | --- |
| Door height formula `Mathf.Max(v1, v2)` flipped to `Mathf.Min(v1, v2)` in `OpeningCaptureController.BuildOpeningModel` | **9 failed** — `AValidDoorIsAccepted` and every test that builds on an accepted door as a fixture (undo, copy, workflow revision, frame/corner/height survival, the overlap and invalid-capture tests) |
| `ObjectPlacementController.TryAdjust`'s `FurnitureValidator.Validate` call replaced with an unconditional `ValidationResult.Valid()` | **1 failed** — exactly `AnInvalidAdjustmentIsRejectedByTheSharedValidatorAndLeavesTheObjectUnchanged`, and only that one |

The first mutation cascading through 9 tests (rather than being silently
absorbed) is itself evidence the door/window formula is load-bearing rather
than incidental. The second mutation confirms adjustment validation is
exercised by exactly the one test written to catch it, not accidentally by
something else.

`ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0, and
`xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported **BUILD SUCCEEDED**.

The shared package was also run standalone in its own host project, to
confirm S5 changed nothing under `shared/`:
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s5-shared.xml -logFile /tmp/ghostmap-s5-shared.log
```
**156 tests, 156 passed, 0 failed, 0 skipped.** Unity exit code 0 — unchanged
from S4.

Re-run after physical-device verification, against the same `7274cfe`, to
confirm nothing drifted between the build used on device and the final
state: Scanner **339/339**, Shared **156/156**, both exit code 0.

The S5 physical-device test was then run on a real iPhone and **passed** —
see "Physical-device verification — S5, passed" above.

### S4 — latest
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s4-final.xml -logFile /tmp/ghostmap-s4-final.log
```
**292 tests, 292 passed, 0 failed, 0 skipped.** Unity exit code 0.
`HeightCaptureControllerTests` 18 (new), `ScanWorkflowControllerTests` 39
(31 + 8 new S4 tests), `CornerCaptureControllerTests` 37, `GhostFrameTests` 20,
`FloorLockControllerTests` 17, `ScannerXrSettingsTests` 4, `ScannerSceneTests` 1,
`GhostMap.Shared.Tests` 156. S3 finished at 266.

Mutation-checked rather than merely observed passing:

| Mutation | Result |
| --- | --- |
| Skip `frame.WorldRayToGhost` and intersect the raw world ray | **14 failed** — every test that used the hostile fixture frame (floor 1.4 m below Unity's origin, 40° yaw) |
| Force the parallel-ray check to never fire (`if (false)` instead of the epsilon test) | **1 failed** — exactly `RayParallelToTheWallIsRejected`, and only that one |

The first mutation biting 14 of 18 controller tests (not all 18 — the manual-
height and wall-derivation tests never touch the ray) is itself evidence the
suite is not vacuously passing. The second mutation confirms the
parallel/behind-camera distinction is load-bearing rather than incidental:
exactly the one test written to catch it fails, nothing else.

`ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0, and
`xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported **BUILD SUCCEEDED**.

The shared package was also run standalone in its own host project, to confirm
S4 changed nothing under `shared/`:
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s4-shared.xml -logFile /tmp/ghostmap-s4-shared.log
```
**156 tests, 156 passed, 0 failed, 0 skipped.** Unity exit code 0 — unchanged
from S3.

Re-run after physical-device verification, against the same `1b8a6ce`, to
confirm nothing drifted between the build used on device and the final state:
Scanner **292/292**, Shared **156/156**, both exit code 0.

The S4 physical-device test was then run on a real iPhone and **passed** —
see the verification section above.

### S3 — latest
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s3-editmode.xml -logFile /tmp/ghostmap-s3-tests.log
```
**266 tests, 266 passed, 0 failed, 0 skipped.** Unity exit code 0.
`CornerCaptureControllerTests` 37, `ScanWorkflowControllerTests` 31,
`GhostFrameTests` 20, `FloorLockControllerTests` 17, `ScannerXrSettingsTests` 4,
`ScannerSceneTests` 1, `GhostMap.Shared.Tests` 156. S2 finished at 212.

The new tests were mutation-checked rather than merely observed passing:

| Mutation | Result |
| --- | --- |
| Intersect at world `y = 0` instead of `frame.FloorWorldY` | 4 failed |
| Drop the whole-room `ValidateRoom` call | 3 failed — the wall-length, area and interior-angle tests |
| Stop forcing Ghost `y = 0` | **0 failed at first — the test was too weak** |

The third mutation survived the first attempt. At the fixture's scale — an eye
1.5 m up aiming a metre ahead — `origin + direction * t` rounds to exactly the
plane it was solved for, so the forcing changed nothing the assertions could see.
`GhostYIsForcedToZeroEvenWhenTheIntersectionCarriesResidue` aims from
progressively further out, where the roundings no longer cancel, and now fails
three of its four cases under that mutation with residues of `-5.96e-07`,
`6.08e-06` and `-2.44e-05` m.

`ScannerBuild.ConfigureXr` and `ScannerBuild.BuildScanner` both exited 0, and
`xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported BUILD SUCCEEDED.

The shared package was also run standalone in its own host project, to confirm
S3 changed nothing under `shared/`:
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s3-final-shared.xml \
  -logFile /tmp/ghostmap-s3-final-shared.log
```
**156 tests, 156 passed, 0 failed, 0 skipped.** Unity exit code 0 — unchanged
from F4.

The S3 physical-device test was then run on a real iPhone and **passed** — see
the verification section above.

### S2 — previous
```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath apps/scanner -buildTarget iOS \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-s2-editmode.xml -logFile /tmp/ghostmap-s2-tests.log
```
**212 tests, 212 passed, 0 failed, 0 skipped.** Unity exit code 0.
`GhostFrameTests` 20, `FloorLockControllerTests` 17,
`ScanWorkflowControllerTests` 14, `ScannerSceneTests` 1,
`ScannerXrSettingsTests` 4, `GhostMap.Shared.Tests` 156.

The frame tests were mutation-checked rather than merely observed passing:
`right = Cross(forward, up)` fails 4 tests, and shifting the origin by
`-CameraYOffset` fails 8. The first attempt at the mirroring mutation caught only
2, because `RightMapsToPlusX` had built its expectation from `frame.Right` and
agreed with a wrong frame; it was rewritten to compute the expected world axis
independently and `FrameAxesMatchTheCameraYawTheLockWasTakenAt` was added.

`ScannerBuild.BuildScanner` succeeded, and
`xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
CODE_SIGNING_ALLOWED=NO` reported BUILD SUCCEEDED.

The S2 physical-device test was then run on a real iPhone and **passed** — see
the verification section above.

### S1
- `GhostMap.Scanner.EditModeTests` — 4/4 passed.
- `GhostMap.Shared.Tests` — 156/156 passed in the same run. 160 total, 0 failed,
  0 skipped.
- `ScannerBuild.ConfigureXr` + `ScannerBuild.BuildScanner` — clean build
  succeeded.
- `xcodebuild -target Unity-iPhone -configuration Release -sdk iphoneos
  CODE_SIGNING_ALLOWED=NO` — BUILD SUCCEEDED; the linked binary contains the
  ARKit native symbols.
- Physical-device smoke test on a real iPhone — **passed** (above).

## Root cause of the first device failure — no active XR loader
`UNITY_XR_ARKIT_LOADER_ENABLED` was never present in the iOS scripting define
symbols, so:

1. Nearly all of `com.unity.xr.arkit` compiled to stubs. `ARKitSessionSubsystem`
   and friends register their subsystem descriptors only after
   `Api.AtLeast11_0()`, which the stub build hard-codes to false, so
   `ARKitLoader.Initialize()` found no descriptors, returned false, and XR
   Management never promoted it to the active loader.
2. `ARKitBuildProcessor.loaderEnabled` stayed false, so its plugin-copy delegate
   excluded `libUnityARKit.a` and `UnityARKit.m` from the Xcode project
   entirely.

The XR settings themselves were always correct and always shipped — iOS
`XRGeneralSettings` with `InitManagerOnStart`, an `XRManagerSettings` with
`ARKitLoader` assigned, all reaching the player's preloaded assets. That is why
the failure looked like a loader that was configured but inert.

The define is normally added by the ARKit package itself from
`UnityEditor.XR.ARKit.LoaderEnabledCheck`, which returns immediately in batch
mode and, in the Editor, only runs on a polling coroutine some time after the
loader is assigned. Its batch-mode fallback inside `ARKitBuildProcessor` is
itself wrapped in `#if UNITY_XR_ARKIT_LOADER_ENABLED`, so it can never bootstrap
the define from nothing. The scanner's build script had been assigning the
loader from inside `BuildPlayer`, which is far too late regardless: a scripting
define only reaches compiled code on the next script compilation.

Fixed by making XR configuration its own step, `ScannerBuild.ConfigureXr`.

## Root cause of the second device failure — camera pose never written
`ProjectSettings.asset` had `activeInputHandler: 0` (Active Input Handling =
Input Manager (Old)), so `ENABLE_INPUT_SYSTEM` was never defined and the Input
System backend was absent from the player. AR Foundation drives the AR camera
with `UnityEngine.InputSystem.XR.TrackedPoseDriver` bound to
`<HandheldARInputDevice>/devicePosition` and `/deviceRotation`. With no backend
there is no such device, the driver's actions resolve to zero controls,
`ReadTrackingStateWithoutTrackingAction()` yields `TrackingStates.None`, and
`SetLocalTransform` writes neither position nor rotation — so the camera sits at
its authored local pose regardless of how well ARKit tracks.

Proven from the failing player's own IL2CPP output: `InputSystemProvider`'s
static constructor, whose only statement is inside `#if ENABLE_INPUT_SYSTEM`,
was compiled down to `{ return; }`.

Everything else in that chain was verified correct first — the Transform the
diagnostics read, the XR Origin → Camera Offset → Main Camera hierarchy,
`XROrigin.Camera`, and the ARCameraManager / ARCameraBackground /
TrackedPoseDriver components with their bindings. See
`docs/handoffs/2026-09-12-scanner-s1-camera-pose-not-driven.md` for the
link-by-link table.

Active Input Handling is now "Both", set by `ScannerBuild.ConfigureXr`, and
`BuildScanner` refuses to build without `ENABLE_INPUT_SYSTEM`.

## Xcode 26 link defect found behind the first fix
Once `libUnityARKit.a` was actually linked, the Xcode project failed to link with
undefined `__swift_FORCE_LOAD_$_swiftCompatibility*` symbols. The ARKit package
points the linker at the Swift compatibility shims via `$(TOOLCHAIN_DIR)`, but
Xcode 26.6 prepends the separately delivered Metal toolchain to `TOOLCHAINS`, so
that resolves to a toolchain with no Swift runtime. `ScannerIosPostBuild` now
adds the correct `XcodeDefault.xctoolchain` paths as `-L` flags on
`OTHER_LDFLAGS`. This had been invisible because the broken builds never linked
the ARKit library at all.

## Interfaces consumed
- Available now from the frozen shared package. The scanner will consume from
  `com.ghostmap.shared`: `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry`,
  `RoomValidator`, `OpeningValidator`, `FurnitureValidator`, the domain DTOs, and
  `ProtocolSerializer`.

## Known issues
All of the following are **non-blocking**. None of them prevented the S1
physical-device test from passing.

### Build process
- Building the scanner for iOS is a **two-step** process and cannot be collapsed
  into one: `ScannerBuild.ConfigureXr`, then `ScannerBuild.BuildScanner` in a
  separate Unity invocation. Both `UNITY_XR_ARKIT_LOADER_ENABLED` and
  `ENABLE_INPUT_SYSTEM` only reach compiled code on the next script compilation.
  In the interactive Editor, the Active Input Handling change from step 1 needs
  an Editor restart. `BuildScanner` fails loudly rather than shipping a broken
  player if this is skipped.
- The generated Xcode project has no signing team. It must be selected by hand
  before deploying to a device.
- `ScannerIosPostBuild` carries an Xcode 26 workaround for the Apple ARKit XR
  Plug-in's `$(TOOLCHAIN_DIR)`-based Swift library search paths. Re-check
  whether it is still needed on an ARKit package or Xcode upgrade; it is
  additive, so it is harmless if the upstream issue is fixed.

### Scene / runtime
- `XROrigin.m_CameraYOffset` stays at `1.1176`. No longer deferred — see the
  CameraYOffset section above. The frame is immune to it.
- `ScannerBootstrap`'s per-link diagnostics readout was built for S1 bring-up.
  It stays through S2-S6 device work and now shares the canvas with the S2
  floor-lock readout; replace both with the real capture UI when that exists.
- Together the two readouts fill much of the screen. This is bring-up
  instrumentation, not the capture UI.
- There is no reset. Establishing a new frame means relaunching the app. Reset
  starts a new AR session and session id, which the plan places later.
- `ISpatialProvider.TryGetFloorHit` returns a scanner-owned `FloorHit` rather
  than the `ARRaycastHit` the implementation plan sketches. `ARRaycastHit` needs
  a live `ARPlane` trackable and cannot be constructed in an EditMode test, which
  would have pushed every floor-lock rule onto a phone to verify.

### Scene / runtime — new in S3
- The screen is now crowded: three readouts and four buttons. Bring-up
  instrumentation, not the capture UI; Task S6 owns the real one.
- There is no AR marker for the aim point before capture, only the numeric
  `Aim G(...)` line. A live preview marker would help and belongs to S6; it was
  left out as a stretch feature.
- Corner markers are untextured spheres from `GameObject.CreatePrimitive` on the
  Built-in pipeline's default material. Legible, not designed.
- `ScanPhase.CaptureHeight` is reachable with nothing behind it. It is entered on
  an accepted closure because the plan's state machine says so; S4 fills it in.
- A corner can be undone but not edited. Fixing corner 2 means undoing 4 and 3
  first, or pressing Redo Corners. The plan asks for Undo, not editing.

### Scene / runtime — new in S4
- The screen is even more crowded now: four readouts, seven buttons and an
  input field. Bring-up instrumentation, not the capture UI; Task S6 owns the
  real one.
- Wall selection is text-and-line, not the "numbered walls in a simple
  top-down mini preview" implementation plan section 8.7 describes. A yellow
  world-space line on the selected wall plus the readout's `Wall N/4` cover
  the same need — which wall is selected, is unambiguous on device — without
  a rendered 2D floorplan. A real top-down preview is left for Task S6.
- The aim marker is an untextured sphere, matching S3's corner markers. It is
  green or red by validation state, not by whether the ray is on the physical
  wall versus past its edge; `withinWallSpan` is readout-only.
- The manual height fallback commits immediately on button press; there is no
  confirmation step. A mistyped value is only caught by the 2.0-4.0 m range
  check, not by asking the user to re-enter it.

### Scene / runtime — new in S6
- The S2-S5 bottom button/readout stack is untouched and still runs past the
  1920-tall reference resolution (the S5 handoff's "screen is now extremely
  crowded" note). S6 did not attempt the plan's full single "Door / Window /
  Furniture / Finish" screen (implementation plan section 19); it added the
  networking/finalization controls in a separate top-anchored area instead
  (see "UI consolidation" above) and made every S2-S5 phase readout hide
  itself outside its own phase, which reduces clutter without a full
  rewrite. A genuine single-screen redesign remains future work, not S6's.
- **Phone-pose streaming (`phone.pose`) was deliberately not implemented.**
  Protocol v1 lists it among the five MVP message types, but it is
  explicitly debug/display-only (`docs/contracts/protocol-v1.md` section
  3.3: "the room is never reconstructed from pose messages"), it is absent
  from every acceptance test in the implementation plan (section 23) and
  from this task's own required-tests list, and the S6 instructions
  themselves gate it on "only if the plan requires it, and no faster than
  the documented limit." Implementing it would mean adding a 5 Hz-capped
  path from `ArSpatialProvider`'s camera pose through the Ghost frame into a
  new message with no test coverage to justify the risk. This is a scope
  decision, not an oversight — flagged here so a future worker does not
  assume it exists.
- The background network thread polls every 100 ms
  (`ScannerHudController.NetworkThreadPollIntervalMs`) rather than being
  woken by a queued write; simple and sufficient at protocol v1's 2 s
  heartbeat/reconnect cadence, but it means a just-enqueued snapshot can sit
  for up to ~100 ms before being sent. Well inside the plan's 500 ms
  local-update target (section 24).
- **Reset has no confirmation step and no visible "are you sure."** A stray
  tap discards the entire in-progress scan (though not the live TCP
  connection, which Reset deliberately leaves alone). The implementation
  plan does not ask for a confirmation dialog; this is the same
  simplicity trade the rest of the bring-up UI makes.
- `ScannerNetworkClient.RequestConnect` performs the TCP `Connect` call
  synchronously inside `PumpOnce`, which runs on the background thread — a
  connect attempt to an unreachable (rather than actively refusing) host can
  block that thread for the platform's default TCP connect timeout, which on
  some networks can be tens of seconds. This blocks only the background
  networking thread, never the main/UI thread, and the existing capture UI
  remains fully responsive throughout; this is recorded because it means
  **Network status** can show `Connecting`-adjacent state for longer than a
  user might expect on a genuinely bad IP, not because it risks a frozen app.

### Scene / runtime — new in S5
- The screen is now extremely crowded: openings and furniture add two more
  readouts and thirteen more buttons on top of the four readouts and eleven
  buttons S2-S4 already placed. This continues rather than solves the
  "screen is now crowded" issue S3/S4 flagged; Task S6 owns the real capture
  UI and is expected to consolidate all of this into the plan's "Door /
  Window / Furniture / Finish" single screen (implementation plan section
  19). Depending on device aspect ratio, some S5 controls may sit close to
  or beyond the top edge of the canvas — if a button described in the test
  procedure above is not visible, scroll is not implemented; report which
  control is missing rather than assuming it is broken.
- Furniture adjustment is by fixed step (0.10 m / 15°) via `+`/`-` buttons
  rather than a typed value or a slider, the same simplicity trade Task S4's
  manual height field makes in the other direction. It always acts on the
  most recently placed object; there is no way to select and re-edit an
  earlier one without undoing back to it.
- The opening capture button doubles as both "capture lower-left" and
  "capture upper-right" via its changing label rather than being two
  separate buttons. This is fewer controls, not fewer capabilities, but it
  means the label text itself is load-bearing UI state.
- There is no per-opening or per-object delete by selection — only "undo the
  most recently added one" for each list, per the implementation plan's
  explicit Undo scope (it asks for undo, not arbitrary deletion).
- Object and opening markers are untextured primitives (cubes for furniture,
  spheres for the opening aim/start points), matching S3's corner markers.
  Legible, not designed.

### Not covered by the S4 device test
The S4 hardware run exercised the golden path on a rectangular room: locking
the floor, capturing four corners, an accepted closure, selecting a wall,
aiming at its ceiling line, and a successful automatic capture (2.69 m). These
paths are covered by EditMode tests but have **not** been seen on a phone:

- the manual height fallback end to end, including the legacy `InputField`'s
  `DecimalNumber` content type on the iOS on-screen keyboard;
- an out-of-range automatic candidate — the red aim marker and a refused
  `Capture Height` press;
- aiming at a wall other than the one currently selected, to confirm the
  highlighted wall and the intersected plane agree;
- a non-rectangular four-corner room's wall planes (only the S3 rectangular
  fixture was used, both in tests and on this device pass).

None of these is suspected broken — each has a passing EditMode test, and the
ray/plane and range-validation logic was mutation-checked. They are recorded
because a passing test is not the same evidence as a passing phone.

### Not covered by the S3 device test
The S3 hardware run exercised a rectangular room with a good scan. These paths
are covered by EditMode tests but have **not** been seen on a phone:

- the `Acceptable` closure band (0.08-0.15 m) and the `Rejected` band
  (> 0.15 m), including the rejected path's refusal to advance and its
  Redo-Corners-only exit. Only `Excellent` (0.031 m) was observed;
- the area, wall-length, interior-angle and self-intersection refusals. Only the
  minimum-spacing refusal was provoked on device;
- capture blocked by degraded tracking (`TrackingNotGood`);
- `RayMissedFloor`, which needs the crosshair aimed at or above the horizon;
- a non-rectangular four-corner room.

None of these is suspected broken — each has a passing test, and two of the
three validation groups were mutation-checked. They are recorded because a
passing test is not the same evidence as a passing phone.

### Toolchain warnings, stock noise
- `ld: warning: search path '.../Metal.xctoolchain/usr/lib/swift*/iphoneos' not
  found` (twice per link). These are the ARKit package's own `$(TOOLCHAIN_DIR)`
  entries that the `OTHER_LDFLAGS` workaround above compensates for. The link
  succeeds.
- Unity reports `Packages/com.unity.xr.arkit/Tests/Editor/Assets/TestReferenceImageLibrary.asset`
  as "unexpectedly altered" in an immutable package. That is the ARKit package's
  own test asset in the package cache, not anything in this repository.
- Unity logs `The application 'build-server' does not exist. / No .NET SDKs were
  found.` at the end of batch-mode runs. No system .NET SDK is installed; builds
  and tests succeed regardless.
- The usual Xcode deprecation and `has no symbols` warnings from Unity's own
  trampoline sources.

## Next safe task
- **Physical-device verification of S6.** S6's implementation is complete —
  networking, finalization, Reset, the consolidated status/network HUD — and
  passes all 361 automated Scanner+Shared EditMode tests plus a real
  `xcodebuild ... BUILD SUCCEEDED`, but `AGENTS.md` rule 12 means it cannot be
  marked done until it is exercised on a real iPhone against a real TCP
  listener. See "Physical-device verification needed — S6" above for the
  exact procedure (a bare `nc -l 47831` on the laptop is enough; the Viewer
  does not need to exist yet).
- Once that pass succeeds, the Scanner-side MVP (S1-S6) is complete. The
  implementation plan's next stage is Integration (`I1`-`I4`), which needs
  both this workstream and the Viewer's `V1`-`V6` — do not start it from
  here; it is explicitly out of scope for this task.

## Do not touch
- `shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`, `docs/decisions/**`
  — owned by the Shared/Integration workstream.
- `apps/viewer/**` — owned by the Viewer workstream.
