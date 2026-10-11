# GhostMap

> A camera normally gives software pixels. GhostMap gives software a room it can reason about.

Stand in the middle of a room, point a **standard non-LiDAR iPhone** around it,
and GhostMap turns the room into a structured, editable 3D model: walls, height,
doors and windows, and the furniture, identified on the phone. One button sends
it to your computer as files you can drop into Unity, plus a page that shows the
room in any browser. Nothing to install on the computer.

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
4. **Build.** When you finalize, the phone writes one zip: `room.html`,
   `room.glb`, one `.glb` per piece of furniture, and `scene.json`. It keeps the
   scan so you can send it again.
5. **Send.** **Send to Computer** opens the iOS share sheet; AirDrop it to your
   Mac (or use Files, iCloud or Mail). Nothing goes to the cloud.
6. **Use it.** Double-click `room.html` to look around and measure in any
   browser. Drag the `.glb` files into Unity.

Direction: [ADR-0012](docs/decisions/ADR-0012-stand-in-place-on-device-capture.md)
and [ADR-0013](docs/decisions/ADR-0013-phone-export-and-browser-viewer.md).
Detail: [`docs/architecture/overview.md`](docs/architecture/overview.md).

## Status

| Stage | State |
| --- | --- |
| Foundation `F0`-`F4` | Complete |
| Assisted-capture MVP: Scanner `S1`-`S6`, Viewer `V1`-`V6` | Complete; scanner verified on a physical iPhone |
| Stand-in-place GhostMap `R1`-`R10` | **`R1` merged; next: confirm the test suites, then `R2`** |

What is where today:

| Piece | Where it is |
| --- | --- |
| Walked-corner capture, manual height/openings/furniture | Built, verified on an iPhone |
| Guided scan UI, stand-in-place room scan, wall sweep, furniture surface measurement, per-object `.glb` exporter | Built (imported from the hackathon repo in `R1`), never run on an iPhone |
| Desktop Unity Viewer, TCP live stream, UDP discovery | Built; frozen and retiring (`R10`) |
| YOLO-n identification, phone-built export bundle, share-sheet Send to Computer, `room.html` | Not built (`R4`, `R6`, `R7`, `R8`) |

The roadmap is plan section 18; progress is tracked in
[`docs/status/integration.md`](docs/status/integration.md).

## Repository

```text
GhostMap/
├── AGENTS.md          working rules for every contributor: read first
├── docs/              spec, plan, architecture, contracts, ADRs, status, handoffs, research
├── shared/            com.ghostmap.shared, the one source of truth for contracts
├── apps/scanner/      iPhone capture app (AR Foundation + ARKit)
├── apps/web-viewer/   browser viewer source for room.html            (planned)
├── apps/viewer/       desktop Unity viewer, frozen and retiring
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
| Output | glTF 2.0 binary (`.glb`), `scene.json`, self-contained `room.html` |

1. Install Unity `6000.3.24f1` through Unity Hub (add iOS Build Support for the
   scanner).
2. Build the scanner to an iPhone: see [`apps/scanner/README.md`](apps/scanner/README.md).
3. Run the tests: `./tools/run_unity_tests.sh`.
4. Optional developer tool: open `apps/viewer` and press Play on `Viewer.unity`
   to watch a scan live over TCP, or load a fixture with **Load Fixture** /
   `python3 tools/send_fixture.py --host 127.0.0.1`.

The scanner must be tested on a **physical iPhone**. Editor behavior never
counts as verification for AR, device or network work.

## Rules that shape the design

- Everything is captured, recognised and exported **on the phone**. No LiDAR, no
  cloud, no generative 3D models (`AGENTS.md` rule 6).
- One button sends the finished files through the iOS share sheet; no IP
  address, no network setup (`AGENTS.md` rule 14).
- The scene stays structured data; walls are always derived from four corners.
- One room, four walls, flat floor and ceiling.

## License

Not yet determined. The planned YOLO-n detector is AGPL-3.0 (ADR-0011).
