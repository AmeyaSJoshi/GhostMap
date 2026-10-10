# GhostMap

> A camera normally gives software pixels. GhostMap gives software a room it can reason about.

GhostMap turns one physical room into a structured, editable, machine-readable
3D scene using a **standard non-LiDAR iPhone** and a computer.

It does not produce a mesh. It produces **semantic geometry**: ordered floor
corners, derived walls, rectangular doors and windows, and parametric
furniture. All of it is plain data that can be edited, measured, saved and
reloaded.

## How it works

The iPhone locks the floor once using AR plane detection and builds a
coordinate frame from that moment. Every later capture intersects the
centre-screen camera ray with a plane GhostMap already knows: the floor for
corners and furniture, a wall computed from the corners for height, doors and
windows. No wall detection, no depth sensing.

After each change the phone sends the **entire scene** as one line of JSON over
TCP. The viewer validates it and rebuilds the room. The phone owns the scene
until the scan is finalized; then the viewer owns an editable copy it can save.

The intended product flow is **Scan → Finalize → Send to Computer**, with the
phone finding the computer by itself (ADR-0007). That discovery step is
designed but not built; today the computer's IP is typed into the phone.

Detail: [`docs/architecture/overview.md`](docs/architecture/overview.md).

## Status

| Stage | State |
| --- | --- |
| Foundation `F0`-`F4` | Complete |
| Scanner `S1`-`S6` | Complete, each verified on a physical iPhone |
| Viewer `V1`-`V6` | Complete |
| Integration `I1`-`I4` | **Not started. Next: `I1A` (iPhone → Viewer over typed IP), then `I1B` (one-button Send to Computer)** |

The scanner has not yet been connected to the Viewer; its network test used a
standalone listener. Current behavior and known issues per area are in
[`docs/status/`](docs/status/).

## Repository

```text
GhostMap/
├── AGENTS.md          working rules for every contributor: read first
├── docs/              spec, plan, architecture, contracts, ADRs, status, handoffs
├── shared/            com.ghostmap.shared, the one source of truth for contracts
├── apps/scanner/      iPhone capture app (AR Foundation + ARKit)
├── apps/viewer/       desktop reconstruction and editing app
├── fixtures/          canonical scene files for working without a phone
└── tools/             test runner, snapshot inspector, fixture sender
```

Each folder has a README. [`docs/README.md`](docs/README.md) says which
document answers which question.

## Getting started

| Component | Version |
| --- | --- |
| Unity Editor | `6000.3.24f1`, pinned; do not upgrade mid-project |
| AR Foundation / Apple ARKit XR Plugin | `6.3.1` |
| Tests | Unity Test Framework `1.6.0`, EditMode |
| Transport | TCP port `47831`, newline-delimited UTF-8 JSON |

1. Install Unity `6000.3.24f1` through Unity Hub (add iOS Build Support for the
   scanner).
2. Open `apps/viewer` and press Play on `Viewer.unity`; it listens on port
   47831. Without a phone, use **Load Fixture** or
   `python3 tools/send_fixture.py --host 127.0.0.1`.
3. Build the scanner to an iPhone: see [`apps/scanner/README.md`](apps/scanner/README.md).
4. Run the tests: `./tools/run_unity_tests.sh`.

Both projects resolve the shared package by relative path
(`"com.ghostmap.shared": "file:../../../shared/com.ghostmap.shared"`); never
copy shared code into an app.

The scanner must be tested on a **physical iPhone**. Editor behavior never
counts as verification for AR, device or network work.

## Scope

The MVP is deliberately narrow: **one room, four corners, flat floor and
ceiling, rectangular non-overlapping openings, eight parametric furniture
types.** LiDAR, dense depth, Gaussian splatting, NeRFs, automatic object
recognition and multi-room mapping stay out until the MVP acceptance test in
plan section 23 passes.

## License

Not yet determined.
