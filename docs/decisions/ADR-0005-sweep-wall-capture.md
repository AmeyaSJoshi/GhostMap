# ADR-0005: Capture walls by sweeping their floor junction, and derive corners from them

- **Status:** Accepted
- **Date:** 2026-10-03
- **Task:** Scanner sweep-wall capture (branch `scanner/sweep-wall-capture`)

## Context

Task S3's capture flow requires the user to **walk to each of the four room
corners** and tap once per corner, aiming the center-screen ray at the corner's
floor point. It works, and it is verified on a physical iPhone (closure measured
0.031 m on the S3 test room).

It is also the single worst part of the product experience:

- The user physically walks a lap of the room, which takes most of the scan time.
- A corner occluded by furniture — a bed in the corner, a wardrobe, a cable
  tray — cannot be aimed at, and the MVP has no way to capture it.
- Four taps mean four independent single-sample measurements. There is no
  redundancy: one bad tap corrupts one corner, and the only signal that it
  happened is the aggregate closure error at the end.
- A corner is a geometrically awkward thing to aim at. The floor junction lines
  of two walls converge there, so the visual target is a point rather than an
  edge, and the user's own body is usually within half a meter of it.

What the product actually wants is for the user to **stand roughly in the middle
of the room, turn around once, and have the walls come out**.

### Why not detect the walls

The obvious reading of "detect the walls" is ARKit vertical-plane detection.
`ADR-0004` already rejected that, and implementation plan section 1.2 states the
rule directly: GhostMap must not require a detected wall plane. Vertical-plane
detection on a non-Pro iPhone is slow, partial and unreliable against blank
painted bedroom walls, which is exactly the target case. Nothing about that has
changed, and this ADR does not revisit it.

Dense depth, monocular depth estimation and learned reconstruction remain
rejected by `ADR-0004` for the reasons recorded there.

### What is actually available

Implementation plan section 8.4 already describes the primitive this ADR needs,
and states its own consequence:

> This means the user only needs to aim at the floor/wall boundary.

Corner capture is the center-screen camera ray intersected with the **locked
floor plane**. Nothing in that arithmetic requires the user to be standing at a
corner. The floor/wall junction is a line lying entirely in the locked floor
plane, and every point along it is recoverable by the same ray intersection
already used and already device-verified.

## Decision

**A wall is captured by sweeping along its floor junction. Corners are derived
by intersecting consecutive wall lines.** This inverts the existing
corner→wall derivation for capture purposes, while leaving the serialized
schema untouched.

For each of the four walls, in order around the room:

1. the user aims at that wall's floor junction and holds the sweep control;
2. while held, each frame's center-screen ray is intersected with the locked
   floor plane, and accepted hits are accumulated as Ghost-space samples;
3. on release, a **total-least-squares line** is fitted to the samples in XZ.

Total least squares, not ordinary least squares: a wall may run in any
direction, including parallel to the Ghost `Z` axis, where a `z = mx + c` fit is
singular. The fit minimizes perpendicular distance and is therefore
orientation-free.

After four wall lines exist, corners are derived:

```text
corner[i] = intersect(wall[i - 1], wall[i])     indices mod 4
```

so that `wall[i]` spans `corner[i] -> corner[i + 1]`, which is exactly the
convention `RoomGeometry.BuildWalls` already uses. Winding is therefore
determined by sweep order, and sweep order is explicit in the UI.

The derived corners are then validated by the **existing**
`RoomValidator.ValidateRoom`, unchanged: spacing, self-intersection, area, wall
length and interior angle all still apply. A sweep-derived room that violates
any of them is rejected exactly as a walked room would be.

### Per-wall quality is recorded and surfaced

A line fit, unlike a single tap, reports its own quality. Each wall carries:

- **RMS residual** — root-mean-square perpendicular distance of its samples to
  the fitted line, in meters. High residual means the user's aim wandered off
  the junction, or tracking drifted mid-sweep.
- **maximum sample distance** — the furthest sample from the camera, in meters.
  This is the lever-arm warning described under Consequences.
- **sample count** and **swept span**, both of which gate acceptance.

### `VerifyClosure` is kept, and becomes a stronger check

Corners derived by line intersection are self-consistent by construction, so a
closure error measured against them would be `0.0` for any scan, however bad.
Reporting that through `SceneSnapshot.closureErrorM` would claim
`ClosureQuality.Excellent` for a scan nobody verified.

`VerifyClosure` is therefore **retained unchanged in meaning**: the user re-aims
at one physical room corner, and `closureErrorM` is the horizontal distance from
that observation to the **derived** corner. Under the previous flow this
compared a tap against an earlier tap by the same method — a repeatability
check. Under this flow it compares an independent direct observation against a
value derived from a different set of measurements, which is a genuine accuracy
check and strictly more informative.

The `<= 0.08 m` / `<= 0.15 m` / `> 0.15 m` bands and
`RoomValidator.ClassifyClosure` are unchanged.

### The walked-corner path is retained

`ScanPhase.CaptureCorners` and `CornerCaptureController` remain, reachable as an
explicit fallback from the sweep phase. Both paths converge on `VerifyClosure`.

This is deliberate. Sweeping has a real accuracy ceiling (below), it has not yet
been verified on a physical iPhone, and the walked path has been. Deleting the
verified path before the replacement has device numbers would leave no way to
complete a scan if sweeping underperforms in a real room.

## Compatibility

**Additive. No schema or protocol change.**

| Contract | Impact |
| --- | --- |
| `SceneSnapshot` | None. Still `schemaVersion` 1. |
| `RoomModel.corners` | None. Still exactly four ordered Ghost-space floor corners, `y = 0`. |
| Walls | Still derived, still never serialized. |
| `closureErrorM` | Meaning preserved. Same field, same units, same bands. |
| `scanPhase` | One new value, `SweepWalls`. The field is a free-form string; nothing parses or validates it. |
| Protocol v1 | None. No new message types, no framing change. |
| `scene-schema-v1.md` | No change required. |
| `protocol-v1.md` | No change required. |

`docs/architecture/overview.md` is updated, which this ADR authorizes per that
document's own freeze note.

## Scanner impact

- **New shared geometry consumed:** `WallFitting` — total-least-squares line fit
  in XZ, line/line intersection, and corner derivation from four ordered walls.
- **New** `WallSweepController` (plain C#, no `MonoBehaviour`), owning sweep
  lifecycle, sample accumulation, per-wall fitting and corner derivation.
- **New** `ScanPhase.SweepWalls`, entered from `FloorLocked`.
- **New** `WallSweepHud`, and `ScannerSceneBuilder` wiring.
- `ScanWorkflowController` gains the sweep transitions and a snapshot revision
  bump per accepted wall. Its existing corner, height, opening, object and
  finalization paths are untouched.
- The S2 frame is read, never written, exactly as in S3-S5.

## Viewer impact

**None. No viewer source changes.**

`scanPhase` reaches the viewer as a display-only string appended to the HUD
status line (`ViewerHudController`), and the room arrives as the same four
corners it already renders. A sweep-derived room is indistinguishable on the
wire from a walked one.

## Tests

Shared (`shared/TestProject`, EditMode):

- line fit recovers a known axis-aligned wall, a `Z`-parallel wall (the case
  that breaks ordinary least squares) and an arbitrary-angle wall;
- fit is translation- and rotation-invariant, and independent of sample order;
- RMS residual is `~0` for exactly collinear samples and matches a
  hand-computed value for a known scatter;
- fit is rejected for too few samples, for a swept span below the minimum, and
  for coincident samples;
- line intersection recovers a known crossing point, and is rejected for
  parallel and near-parallel lines;
- corner derivation reproduces a known rectangle from its four wall lines, and
  reproduces a non-rectangular quadrilateral;
- derived corner order satisfies `wall[i] = corner[i] -> corner[i + 1]` against
  `RoomGeometry.BuildWalls`;
- a derived room that violates area, interior angle or self-intersection is
  refused by the unchanged `RoomValidator.ValidateRoom`.

Scanner (`apps/scanner`, EditMode):

- sweep refused before floor lock, with bad tracking, and in the wrong phase;
- samples accumulate only while a sweep is active;
- rays parallel to or aimed away from the floor are rejected, not extrapolated;
- a wall is refused when its fit fails, and the sweep can be retried;
- undo removes the most recent wall only;
- four accepted sweeps derive four corners and advance to `VerifyClosure`;
- a sweep-derived room failing `RoomValidator.ValidateRoom` blocks the advance;
- revision increments once per accepted wall and never decreases;
- the fallback transition to `CaptureCorners` works and leaves the walked path's
  behavior unchanged.

**Physical-device test is required before this is considered working**, per
`AGENTS.md` rule 12. The automated suites above cannot establish real-room
accuracy.

## Consequences

**Positive**

- No walking. The user stands and turns, which is the point.
- Occluded corners no longer block a scan. A wall needs only a visible segment
  of its floor junction, not its endpoints.
- Many samples per wall instead of one per corner. Random aim jitter averages
  down by roughly `1/sqrt(N)`.
- Every wall self-reports fit quality, so a bad sweep is visible immediately
  rather than inferred from closure at the end.
- Partial occlusion, furniture against walls, and skirting boards are tolerated
  by the fit instead of defeating a single tap.

**Negative — and this is the real cost**

- **Error grows with the square of aim distance.** For camera height `h` and
  aim distance `d`, a pitch error `delta` displaces the floor point by
  approximately `delta * (h + d^2 / h)`. At `h = 1.5 m`:

  | `d` | `delta = 0.5 deg` | `delta = 1.0 deg` |
  | --- | --- | --- |
  | 1.5 m | 0.03 m | 0.05 m |
  | 3.0 m | 0.07 m | 0.13 m |
  | 5.0 m | 0.16 m | 0.32 m |

  Walking to a corner keeps `d` near 1.5 m. Standing mid-room in a 4x3 m
  bedroom gives `d` of roughly 1.5-2 m and stays inside the plan's 0.12 m
  median target; a 6x5 m room pushes against it.

- **Random error averages out over a sweep; systematic aim bias does not.** A
  user who consistently aims slightly above the junction line pushes every wall
  outward by the full table value, producing a uniformly oversized room with a
  low RMS residual and a clean-looking scan. Fit residual cannot detect this.
  Only `I2`'s tape-measure benchmark can.

- Each wall needs a swept span long enough to constrain its direction. A very
  short visible segment of junction yields a confident-looking fit with a badly
  determined angle, which the span gate exists to refuse.

- Two capture paths now exist for the same four corners, which is more surface
  area than the MVP strictly needs. Retained on purpose until device numbers
  justify removing one.

## Compliance

- Replacing the capture model again, or removing the walked-corner fallback,
  requires a new ADR.
- Any change to the fit acceptance gates — minimum samples, minimum span,
  maximum residual — must be mirrored in `tools/inspect_snapshot.py` only if it
  becomes a serialized rule. It is not one today: these gates are scanner-side
  capture acceptance, and nothing about them reaches the wire.
- `RoomValidator` remains the only authority on whether a room is legal. The
  sweep path must not introduce a second copy of any room rule.
