# E1 — room footprint from a standing 360° scan (RESEARCH SPIKE — NOT PRODUCTION)

Question: can DETR wall/floor boundaries + ARKit pose/intrinsics + the locked
floor recover a room footprint with the right shape and relative proportions?

Nothing here is linked into the Scanner, Viewer, shared package or protocol.

| Path | What |
| --- | --- |
| `recorder/` | Standalone iOS capture app (`xcodegen generate`, bundle `com.ghostmap.research.e1recorder`) |
| `tools/segment.swift` | Runs Apple's DETR seg on 448×448 tiles (Core ML, Neural Engine) |
| `analysis/e1/` | Orientation, rays, floor intersection, Ghost frame, boundary extraction, wall fitting (port of `WallFitting.cs`), evaluation |
| `analysis/tests/` | 22 tests incl. Unity/ARKit frame equivalence and a synthetic end-to-end room with a bed against a wall |
| `analysis/run_blind.py` | Blind prediction (no ground-truth input), sealed with SHA-256 |
| `analysis/run_eval.py` | Scores sealed predictions vs tape measurements; repeatability across scans |
| `scans/`, `out/`, `models/`, `build/`, `.venv/` | Local only (gitignored) |

Model: `apple/coreml-detr-semantic-segmentation`, `DETRResnet50SemanticSegmentationF16.mlpackage`,
Apache-2.0, weight.bin SHA-256 `2f4de3df…6480408` (see `../perception-audit/spikes/README.md`).

```bash
.venv/bin/python -m unittest discover -s analysis/tests
xcrun devicectl device copy from --device <id> --domain-type appDataContainer \
  --domain-identifier com.ghostmap.research.e1recorder --source Documents/scans --destination scans
.venv/bin/python analysis/run_blind.py scans/scan_<stamp>
.venv/bin/python analysis/run_eval.py ground_truth.json out/scan_<stamp> [out/scan_<stamp2> ...]
```
