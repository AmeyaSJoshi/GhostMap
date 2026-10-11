"""RESEARCH SPIKE — NOT PRODUCTION.

E1 scoring. Runs ONLY against an existing, unmodified blind prediction (its
SHA-256 must match the sealed value) and a ground-truth file. Never changes the
prediction. With several scans it also reports repeatability.

usage: .venv/bin/python analysis/run_eval.py ground_truth.json out/<scan1> [out/<scan2> ...]

ground_truth.json:
  {
    "walls_m": {"A": 4.02, "B": 3.51, "C": 4.00, "D": 3.49},
    "diagonal_AB_CD_m": 5.33,      # corner between walls A,B  ->  corner between C,D
    "diagonal_BC_DA_m": 5.31,      # optional cross-check
    "notes": "..."
  }
Wall A = the wall you faced when you tapped Lock Floor; B, C, D follow as you
turn to your RIGHT (clockwise seen from above).
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

from e1 import evaluate as ev  # noqa: E402
from e1 import plots, walls  # noqa: E402


def load_sealed(out: Path) -> dict:
    text = (out / "prediction.json").read_text()
    sealed = (out / "prediction.sha256").read_text().strip()
    if hashlib.sha256(text.encode()).hexdigest() != sealed:
        sys.exit(f"{out}: prediction.json was modified after sealing — refusing to score it")
    return json.loads(text)


def forward_wall_index(corners: np.ndarray, center: np.ndarray) -> int:
    """Which predicted wall a ray from the scan center along Ghost +Z hits first."""
    best, idx = np.inf, -1
    for i in range(len(corners)):
        a, b = corners[i], corners[(i + 1) % len(corners)]
        m = np.array([[0.0, a[0] - b[0]], [1.0, a[1] - b[1]]])
        if abs(np.linalg.det(m)) < 1e-9:
            continue
        t, s = np.linalg.solve(m, a - center)
        if t > 0 and 0 <= s <= 1 and t < best:
            best, idx = t, i
    return idx


def fmt(xs, nd=2):
    return " | ".join(f"{x:.{nd}f}" for x in xs)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    gt_raw = json.loads(Path(sys.argv[1]).read_text())
    w = gt_raw["walls_m"]
    gt, diag2_constructed = ev.gt_quad_from_measurements(w["A"], w["B"], w["C"], w["D"],
                                                         gt_raw.get("diagonal_AB_CD_m"), gt_raw.get("diagonal_BC_DA_m"))
    names = ["A", "B", "C", "D"]
    report = ["# E1 evaluation (scored AFTER the sealed blind prediction)\n"]
    if gt_raw.get("diagonal_BC_DA_m"):
        report.append(f"Ground-truth consistency: measured diagonal BC-DA {gt_raw['diagonal_BC_DA_m']:.3f} m vs "
                      f"{diag2_constructed:.3f} m implied by the other five measurements.\n")
    per_scan, shapes = [], []
    for arg in sys.argv[2:]:
        out = Path(arg).resolve()
        pred = load_sealed(out)
        fp = pred.get("footprint")
        if not fp:
            report.append(f"## {pred['scan']}\n\nNO FOOTPRINT — blind run failed: {pred['room_validator_issues']}\n")
            per_scan.append(dict(scan=pred["scan"], failed=True))
            continue
        corners = np.array(fp["corners_xz"])
        res = ev.evaluate(corners, gt)
        al = np.array(res["normalized"]["aligned_corners"])
        shift, rev = res["correspondence"]["shift"], res["correspondence"]["reversed"]
        order = list(range(4))[::-1] if rev else list(range(4))
        order = order[shift:] + order[:shift]  # predicted corner index used for GT corner j
        fwd = forward_wall_index(corners, np.array(pred["points"]["scan_center_xz"]))
        # predicted wall j (after correspondence) == GT wall j; which predicted wall maps to GT wall A?
        pred_wall_for_A = order[0] if not rev else (order[0] - 1) % 4
        plots.aligned(gt, al, corners, out / "aligned_vs_ground_truth.png",
                      f"{pred['scan']}: scale s={res['normalized']['scale']:.3f}, IoU={res['normalized']['iou']:.3f}")
        r, n = res["raw"], res["normalized"]
        topology = (pred["natural_dominant_line_count"] == 4 and not pred["room_validator_issues"])
        report += [
            f"## {pred['scan']}\n",
            f"Prediction sha256 {(out / 'prediction.sha256').read_text().strip()[:16]}…, sealed {pred['created']}.\n",
            "### Raw metric (rigid alignment only, no scaling)\n",
            "| wall | " + " | ".join(names) + " |", "|---|---|---|---|---|",
            "| predicted (m) | " + fmt(r["pred_lengths"]) + " |",
            "| tape (m) | " + fmt(r["gt_lengths"]) + " |",
            "| abs error (m) | " + fmt(r["abs_err"]) + " |",
            "| error % | " + fmt(r["pct_err"], 1) + " |",
            "",
            "| corner | DA | AB | BC | CD |", "|---|---|---|---|---|",
            "| predicted angle | " + fmt(r["pred_angles"], 1) + " |",
            "| true angle | " + fmt(r["gt_angles"], 1) + " |",
            "",
            f"Area predicted {r['area_pred']:.2f} m² vs {r['area_gt']:.2f} m². Rigid IoU {r['rigid_iou']:.3f}. "
            f"Corner RMS after rigid alignment {r['rigid_corner_rms']:.3f} m.\n",
            "### Scale-normalized (ONE uniform scale + rotation + translation)\n",
            f"Global scale factor s = {n['scale']:.4f} (prediction × s ≈ truth).\n",
            "| wall | " + " | ".join(names) + " |", "|---|---|---|---|---|",
            "| normalized length error % | " + fmt(n["normalized_length_pct_err"], 1) + " |",
            "| perimeter-share error % | " + fmt(n["perimeter_share_pct_err"], 1) + " |",
            "| adjacent ratio error % (X/next) | " + fmt(n["adjacent_ratio_pct_err"], 1) + " |",
            "",
            "| corner | DA | AB | BC | CD |", "|---|---|---|---|---|",
            "| position error (% of diagonal) | " + fmt(n["corner_err_pct_of_diag"], 1) + " |",
            "| angle error (deg) | " + fmt(n["angle_err"], 1) + " |",
            "",
            f"Polygon IoU after similarity alignment: **{n['iou']:.3f}**. Corner RMS: {n['corner_rms_pct_of_diag']:.1f}% of the room diagonal.\n",
            f"Topology: natural dominant line count = {pred['natural_dominant_line_count']}, validator issues = {pred['room_validator_issues'] or 'none'} "
            f"→ **{'YES' if topology else 'NO'}**.\n",
            f"Orientation check: the wall straight ahead at floor lock is predicted wall {fwd}; the alignment maps "
            f"predicted wall {pred_wall_for_A} to wall A → {'consistent' if fwd == pred_wall_for_A else 'INCONSISTENT'}.\n",
            f"Weakest wall (boundary coverage): {names[int(np.argmin(fp['wall_boundary_coverage']))] if fp['wall_boundary_coverage'] else '?'} "
            f"— coverage by predicted wall index {fmt(fp['wall_boundary_coverage'])}.\n",
        ]
        per_scan.append(dict(scan=pred["scan"], eval=res, topology=topology))
        shapes.append(dict(scan=pred["scan"], len_share=np.array(walls.wall_lengths(al)) / np.sum(walls.wall_lengths(al)),
                           angles=walls.interior_angles_deg(al), aligned=al, scale=n["scale"]))
    if len(shapes) >= 2:
        ls = np.array([s["len_share"] for s in shapes])
        an = np.array([s["angles"] for s in shapes])
        sc = np.array([s["scale"] for s in shapes])
        pair_iou = [ev.iou_convex(shapes[i]["aligned"], shapes[j]["aligned"]) for i in range(len(shapes)) for j in range(i + 1, len(shapes))]
        report += [
            "## Repeatability\n",
            f"Scans scored: {len(shapes)} (failed blind runs: {sum(1 for p in per_scan if p.get('failed'))}).\n",
            "| wall | " + " | ".join(names) + " |", "|---|---|---|---|---|",
            "| perimeter share mean % | " + fmt(ls.mean(0) * 100, 1) + " |",
            "| perimeter share spread (std, pts) | " + fmt(ls.std(0) * 100, 2) + " |",
            "",
            "| corner | DA | AB | BC | CD |", "|---|---|---|---|---|",
            "| angle mean | " + fmt(an.mean(0), 1) + " |",
            "| angle spread (std deg) | " + fmt(an.std(0), 1) + " |",
            "",
            f"Global scale factor across scans: {fmt(sc, 3)} (std {sc.std():.3f}).",
            f"Pairwise IoU between aligned predictions: min {min(pair_iou):.3f}, mean {np.mean(pair_iou):.3f}.\n",
        ]
    dest = Path(sys.argv[2]).resolve().parent / "E1_evaluation.md"
    dest.write_text("\n".join(report))
    (dest.with_suffix(".json")).write_text(json.dumps(per_scan, indent=2, default=lambda o: o.tolist() if hasattr(o, "tolist") else str(o)))
    print("\n".join(report))
    print(f"\nwritten: {dest}")


if __name__ == "__main__":
    main()
