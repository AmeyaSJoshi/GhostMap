# R3: Accuracy benchmark

**Who:** the owner, with a tape measure. An agent analyses the numbers.

**Room:** one ordinary room, four walls. Tape-measure and write down:
wall A, B, C, D lengths (floor line, corner to corner), ceiling height, one
door's width and height.

**Scans:** five stand-in-place scans. Stand near the centre each time; note
where. For each scan, read the derived values from the Details panel (or have
an agent extract them from a `scene.json` once `R6` exists, or from the
`GhostMap auto:` / `GhostMap sweep:` console lines).

Record in `docs/status/integration.md` ("R3 accuracy benchmark"):

```text
scan | wall A-D error | height error | door width error | walls found automatically | fallback used
```

**Targets:** median wall absolute error ≤ 0.12 m, max ≤ 0.20 m, height error ≤
0.15 m, no self-crossing rooms.

**If targets fail:** first try standing nearer the centre and turning more
slowly. Only then consider thresholds in `WallPlaneAccumulator`,
`RoomFromWalls` and `WallSweepController` (their constants are listed in
`docs/status/scanner.md` and the source). Never hide errors to pass. ADR-0005
explains why a consistent aim bias enlarges the whole room without raising the
fit residual.
