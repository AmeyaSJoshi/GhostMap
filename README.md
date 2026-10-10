# GhostMap

> A camera normally gives software pixels. GhostMap gives software a room it can reason about.

Stand in the middle of a room, point a **standard non-LiDAR iPhone** around it,
and GhostMap turns the room into a structured, editable 3D model: walls, height,
doors and windows, and the furniture, identified on the phone. One button sends
it to your computer, where you can edit it and export it as files for Unity.

It does not produce a scanned mesh. It produces **semantic geometry**: four
ordered floor corners, derived walls, rectangular openings and parametric
furniture, all as plain data.

## How it works

1. **Floor.** ARKit finds the floor once; GhostMap builds its own coordinate
   frame from it.
2. **Room.** While you turn in place, ARKit's detected wall planes are merged
   into four walls and the corners are computed where they meet. If walls are
   missing you sweep along them instead, or walk to the corners.
3. **Furniture.** A small detector (YOLO-n) running on the phone names what it
   sees; each item is measured from the ARKit surface it sits on.
4. **Send.** After you finalize, **Send to Computer** finds the GhostMap Viewer on
   your network and sends the whole scene. Nothing goes to the cloud.
5. **Use it.** The Viewer (a Unity app) shows the room for editing and measuring
   and exports `scene.json`, `room.glb` and one `.glb` per piece of furniture.

Direction: [ADR-0012](docs/decisions/ADR-0012-stand-in-place-on-device-capture.md).
Detail: [`docs/architecture/overview.md`](docs/architecture/overview.md).

## Status

| Stage | State |
| --- | --- |
| Foundation `F0`-`F4` | Complete |
| Assisted-capture MVP: Scanner `S1`-`S6`, Viewer `V1`-`V6` | Complete; scanner verified on a physical iPhone |
| Stand-in-place GhostMap `R1`-`R8` | **Not started. Next: `R1`** |

What is where today:

| Piece | Where it is |
| --- | --- |
| Walked-corner capture, manual height/openings/furniture, TCP streaming, the whole Viewer | This repository, tested |
| Stand-in-place room scan, wall sweep, furniture surface measurement, per-object `.glb` export, Send to Computer discovery | The hackathon repository `GhostMapDublinHacks`, tested there, never run on an iPhone. Task `R1` brings it here |
| YOLO-n identification, whole-room `.glb`, delivery confirmation | Not built (`R4`-`R7`) |

The roadmap is plan section 18; progress is tracked in
[`docs/status/integration.md`](docs/status/integration.md).

## Repository

```text
GhostMap/
├── AGENTS.md          working rules for every contributor: read first
├── docs/              spec, plan, architecture, contracts, ADRs, status, handoffs
├── shared/            com.ghostmap.shared, the one source of truth for contracts
├── apps/scanner/      iPhone capture app (AR Foundation + ARKit)
├── apps/viewer/       desktop viewer, editor and exporter
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

The scanner must be tested on a **physical iPhone**. Editor behavior never
counts as verification for AR, device or network work.

## Rules that shape the design

- Everything is captured and recognised **on the phone**. No LiDAR, no cloud, no
  generative 3D models (`AGENTS.md` rule 6).
- The scene stays structured data; walls are always derived from four corners.
- One room, four walls, flat floor and ceiling.

## License

Not yet determined. The planned YOLO-n detector is AGPL-3.0 (ADR-0011).
