# Scanner Status

Workstream: Scanner. Owns `apps/scanner/**` and this file.
Full task-by-task history (S1-S6 design notes, device logs, root-cause write-ups):
`git show f5a7d30:docs/status/scanner.md`.

## Current state

**S1-S6 complete, each verified on a physical iPhone.** The scanner-side MVP is done.

| Task | What works | Device result |
| --- | --- | --- |
| S1 | AR Foundation 6.3.1 / ARKit session, camera feed, tracking diagnostics | Tracking reached, floor candidates reported |
| S2 | Floor lock and the GhostMap coordinate frame | Frame fixed while moving, floor at Ghost y = 0 |
| S3 | Four-corner capture, Undo / Redo Corners, closure verification | Closure 0.031 m (Excellent) |
| S4 | Room height from a derived wall plane, manual fallback | 2.69 m captured |
| S5 | Doors and windows on a derived wall, parametric furniture placement | Door, window, furniture all plausible |
| S6 | TCP client, snapshot publishing, finalization, Reset | 22 snapshots, monotonic revisions, 3 reconnects resend the finalized room |

The S6 device test ran against a standalone Python TCP listener, not the Viewer.
No iPhone-to-Viewer run has been done yet; that is roadmap task `R2`.

**Direction (ADR-0012, ADR-0013):** stand in one spot and turn, identify
furniture on the phone with YOLO-n, then the phone builds the export bundle
(`room.html`, `.glb` files, `scene.json`) and **Send to Computer** opens the iOS
share sheet. The stand-in-place scan, wall sweep and furniture surface
measurement exist in `GhostMapDublinHacks` and arrive with task `R1`. YOLO-n is
`R4`, the export bundle `R6`, the share sheet `R7`. Until `R1`, this
repository's scanner has only the walked-corner flow, and the TCP client is a
developer tool.

### Scan flow

```text
Boot -> WaitingForTracking -> FindFloor -> FloorLocked -> CaptureCorners
     -> VerifyClosure -> CaptureHeight -> AddOpenings -> AddObjects
     -> ReadyToFinalize -> Finalized
```

`ScanWorkflowController` is the only thing that changes phase. Every structural
mutation (floor lock, corner add/undo, closure, height, opening add/undo,
object add/undo/adjust, phase finish, finalize) increments `revision` and
rebuilds the snapshot. `ScannerSnapshotPublisher` turns each new revision into
a `scene.snapshot` message; finalization sends the final snapshot and
`scan.finalized` as one batch.

### Where the scanner deliberately differs from the plan

| Plan says | Scanner does | Why |
| --- | --- | --- |
| Send `phone.pose` at <= 5 Hz (section 7.2) | Not implemented | Debug-only, absent from every acceptance test |
| Reset starts a new AR session (section 10) | New session id, room id and revision 0; AR tracking kept | Re-initialising ARKit costs tracking quality for nothing |
| Numbered top-down wall preview for height (section 8.7) | `Wall N/4` readout plus a yellow line on the selected wall | Same information, no 2D render |
| Mark manual height in snapshot metadata (section 8.7) | Shown on the HUD only | Schema v1 has no field for it |
| One guided scan flow (section 19) | Per-phase bring-up HUDs that hide outside their phase | The guided flow arrives with `R1` (hackathon UI overhaul) and is finished in `R9` |
| `TryGetFloorHit` returns `ARRaycastHit` (S2) | Returns scanner-owned `FloorHit` | `ARRaycastHit` cannot be built in EditMode tests |

## Last verified commit

- Scanner sources: `f087aaa` (S6, device-verified). Later commits touch docs only.

## Tests run

```bash
./tools/run_unity_tests.sh scanner
```

Last recorded result at `f087aaa`: **361 tests, 361 passed** (205 scanner + 156
embedded shared). The scanner suite runs with `-buildTarget iOS`, so it needs
the iOS Build Support module (macOS).

## Interfaces consumed

From `com.ghostmap.shared`: the domain DTOs, `GhostCoordinateFrame`,
`RayPlaneMath`, `RoomGeometry`, `WallGeometry`, `MeasurementMath`,
`RoomValidator`, `OpeningValidator`, `FurnitureValidator`, `ProtocolConstants`,
the wire message types and `ProtocolSerializer`.

## Known issues

None blocking `R1`.

**Build**
- iOS build is two Unity invocations: `ScannerBuild.ConfigureXr`, then
  `ScannerBuild.BuildScanner`. The XR loader and Input System defines only take
  effect on the next compile. `BuildScanner` fails loudly if step 1 was skipped.
- The generated Xcode project has no signing team; select it by hand.
- `ScannerIosPostBuild` adds `XcodeDefault.xctoolchain` Swift library paths to
  work around Xcode 26 resolving `$(TOOLCHAIN_DIR)` to the Metal toolchain.
  Re-check on any Xcode or ARKit package upgrade.
- Do a clean Xcode build (delete the app first) before a device test. A stale
  cached binary caused the inconclusive first S6 pass.

**Runtime**
- The screen is crowded: S1 diagnostics, the S6 status block (including an
  `S6 diag:` debug line) and per-phase button rows. On some aspect ratios S5
  controls sit near the top edge. Consolidating this into one guided flow is task `R9`.
- Reset has no confirmation. A stray tap discards the scan (not the connection).
- Once height is captured there is no way back to corners or height except
  Reset. The transition table allows `AddOpenings -> CaptureHeight` and
  `AddObjects -> AddOpenings`, but no method or button uses them.
- Corners, openings and objects support undo-last only, not edit or delete by
  selection. Furniture adjustment uses fixed steps (0.10 m, 15 degrees) and acts
  on the most recently placed object.
- `TcpClient.Connect` runs on the network thread; an unreachable host blocks
  that thread for the OS connect timeout. The UI stays responsive.
- If the scan is finalized while disconnected, the reconnect handshake sends
  `hello` and the finalized snapshot but not `scan.finalized`. Harmless today
  because the Viewer keys on `snapshot.finalized`.
- A non-numeric Port field silently becomes port 0 (`int.TryParse`).
- No handling of app backgrounding (`OnApplicationPause`).

**Covered by EditMode tests but never seen on a phone**
- Closure in the Acceptable and Rejected bands; area, wall-length, angle and
  self-intersection refusals; capture blocked by degraded tracking.
- The manual height fallback on the iOS keyboard.
- Invalid openings (off the wall, above the ceiling, overlapping), undo of
  openings and objects, finishing a phase with zero items.
- A non-rectangular room; a Wi-Fi radio toggle on the phone.

## Next safe task

Roadmap tasks touching the scanner, in order (plan section 18): `R1` import the
hackathon scanner work, `R2` first device session, `R4` YOLO-n identification
(native Core ML plugin, `IObjectDetector`, remove the ARKit-label type guesser),
`R6` build the export bundle and keep saved scans, `R7` share-sheet Send to
Computer (native plugin), `R9` one guided flow without debug text. See
`docs/status/integration.md` for progress.

## Do not touch

`shared/**`, `fixtures/**`, `tools/**`, `docs/contracts/**`,
`docs/decisions/**` (Shared/Integration) and `apps/viewer/**` (Viewer).
