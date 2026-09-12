# GhostMap

> A camera normally gives software pixels. GhostMap gives software a room it can reason about.

GhostMap turns one physical room into a structured, editable, machine-readable 3D
scene using a **standard non-LiDAR iPhone** and a laptop.

It does not produce a photorealistic mesh. It produces **semantic geometry**:
ordered floor corners, derived walls, rectangular openings, and parametric
furniture — all as plain data that can be edited, measured, saved, and reloaded.

---

## Repository shape

```text
GhostMap/
├── AGENTS.md          # execution contract — read this first
├── docs/              # spec, plan, architecture, contracts, ADRs, status, handoffs
├── shared/            # com.ghostmap.shared — the only source of truth for contracts
├── apps/scanner/      # Unity iPhone capture app (AR Foundation + ARKit)
├── apps/viewer/       # Unity desktop reconstruction/editing app
├── fixtures/          # canonical scene JSON used to develop the viewer without a phone
└── tools/             # Python helpers for replaying and inspecting snapshots
```

## Architecture in one paragraph

Two separate Unity projects share one local Unity package. The scanner locks the
floor once using AR plane detection, builds a `GhostCoordinateFrame` from that
moment, and captures everything afterwards by intersecting camera rays with
**mathematically derived** planes rather than waiting for ARKit to detect real
walls. After each structural change it sends the **entire current scene
snapshot** over TCP as one line of JSON. The viewer stores the newest snapshot,
validates it, and rebuilds the room. The scanner is authoritative until the scan
is finalized; after that the viewer owns the editable copy.

Full detail: [`docs/architecture/overview.md`](docs/architecture/overview.md).

## Environment

| Component | Version |
| --- | --- |
| Unity Editor | `6000.3.24f1` (pinned — do not upgrade mid-project) |
| AR Foundation | `6.3.1` |
| Apple ARKit XR Plugin | `6.3.1` |
| Language | C# |
| Tests | Unity Test Framework (EditMode) |
| Transport | Raw TCP, port `47831`, newline-delimited UTF-8 JSON |

The scanner must be built and tested on a **physical iPhone**. Editor-only
behavior never counts as verification for AR, device, or network work.

## Getting started

```bash
git clone <this-repo>
cd GhostMap
```

1. Install Unity `6000.3.24f1` via Unity Hub.
2. Open `apps/viewer` — it resolves `com.ghostmap.shared` by relative path.
3. Open `apps/scanner` — same shared package, plus AR Foundation and ARKit.
4. Read `AGENTS.md`, then your workstream status file in `docs/status/`.

No manual source copying is required or permitted. Both projects reference the
shared package from their own `Packages/manifest.json`:

```json
"com.ghostmap.shared": "file:../../../shared/com.ghostmap.shared"
```

## Workstreams

| Workstream | Owns | Status file |
| --- | --- | --- |
| Scanner | `apps/scanner/**` | `docs/status/scanner.md` |
| Viewer | `apps/viewer/**` | `docs/status/viewer.md` |
| Shared / Integration | `shared/**`, `fixtures/**`, `tools/**`, contracts, ADRs | `docs/status/shared.md`, `docs/status/integration.md` |

Foundation tasks `F0`–`F3` must all be merged before the Scanner (`S*`) and
Viewer (`V*`) workstreams may split and run in parallel.

## MVP scope

The MVP is deliberately narrow: **one room, four ordered corners, flat floor,
flat ceiling, rectangular non-overlapping openings, parametric furniture.**

LiDAR, dense depth, Gaussian splatting, NeRFs, automatic object recognition, and
multi-room mapping are explicitly **out of scope** until the full MVP acceptance
test in the implementation plan passes.

## License

Not yet determined.
