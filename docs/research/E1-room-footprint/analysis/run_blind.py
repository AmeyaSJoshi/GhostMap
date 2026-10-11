"""RESEARCH SPIKE — NOT PRODUCTION.

E1 blind prediction. Takes a recorded scan and produces the predicted room
footprint plus every diagnostic artifact. It has NO ground-truth input: it
cannot see tape measurements. The prediction is sealed with a SHA-256 that
run_eval.py checks before scoring.

usage: .venv/bin/python analysis/run_blind.py scans/<scan_dir> [--out out/<name>]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

from e1 import boundary, plots, scan_io, walls  # noqa: E402
from e1.camera import camera_center, pixel_to_camera_dir  # noqa: E402
from e1.pipeline import E1_ROOT, MODEL, process, upright_image  # noqa: E402

ROOM_RULES = dict(min_angle=35.0, max_angle=145.0, min_wall=0.5, max_wall=20.0, min_area=2.0)  # RoomValidator


def params_snapshot():
    mods = {"boundary": boundary, "walls": walls, "scan_io": scan_io}
    out = {}
    for name, m in mods.items():
        out[name] = {k: getattr(m, k) for k in dir(m) if k.isupper() and isinstance(getattr(m, k), (int, float))}
    out["room_rules"] = ROOM_RULES
    return out


def diagnostics_table(results, frame, n_frames=3, n_px=5) -> str:
    rows = ["| frame | grid (x,y) | sensor px (u,v) | camera ray (unit) | world ray (unit) | floor hit (ARKit world) | Ghost (x,y,z) |",
            "|---|---|---|---|---|---|---|"]
    usable = [r for r in results if len(r.valid) and r.valid.sum() >= n_px]
    for r in [usable[int(i)] for i in np.linspace(0, len(usable) - 1, min(n_frames, len(usable)))] if usable else []:
        idx = np.flatnonzero(r.valid)
        for j in idx[np.linspace(0, len(idx) - 1, n_px).astype(int)]:
            u, v = r.sensor_uv[j]
            dc = pixel_to_camera_dir(r.frame.intr, u, v)
            dc = dc / np.linalg.norm(dc)
            dw = r.world_dirs[j] / np.linalg.norm(r.world_dirs[j])
            fw, g = r.floor_world[j], r.ghost[j]
            f3 = lambda a: "(" + ", ".join(f"{x:+.3f}" for x in a) + ")"  # noqa: E731
            rows.append(f"| {r.frame.i} | ({r.contacts.gx[j]:.1f}, {r.contacts.gy[j]:.1f}) | ({u:.1f}, {v:.1f}) | {f3(dc)} | {f3(dw)} | {f3(fw)} | {f3(g)} |")
    return "\n".join(rows)


def validate_room(corners):
    if corners is None:
        return ["no polygon"]
    issues = []
    ang = walls.interior_angles_deg(corners)
    lens = walls.wall_lengths(corners)
    from e1.evaluate import polygon_area
    if not walls.is_convex_simple(corners):
        issues.append("not convex/simple")
    if (ang < ROOM_RULES["min_angle"]).any() or (ang > ROOM_RULES["max_angle"]).any():
        issues.append(f"interior angle outside {ROOM_RULES['min_angle']}-{ROOM_RULES['max_angle']}")
    if (lens < ROOM_RULES["min_wall"]).any() or (lens > ROOM_RULES["max_wall"]).any():
        issues.append("wall length outside 0.5-20 m")
    if abs(polygon_area(corners)) < ROOM_RULES["min_area"]:
        issues.append("area < 2 m^2")
    return issues


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scan")
    ap.add_argument("--out")
    a = ap.parse_args()
    scan_dir = Path(a.scan).resolve()
    out = Path(a.out).resolve() if a.out else E1_ROOT / "out" / scan_dir.name
    if (out / "prediction.json").exists():
        sys.exit(f"{out}/prediction.json already exists — a blind prediction is written once. Delete it deliberately to redo.")
    out.mkdir(parents=True, exist_ok=True)
    t0 = time.time()

    scan = scan_io.load(scan_dir)
    keys = scan_io.select_keyframes(scan)
    print(f"{len(scan.frames)} frames, {len(keys)} keyframes")
    if not keys:
        sys.exit("no usable keyframes")
    results = process(scan, keys, out / "work")

    # Artifacts per keyframe.
    (out / "seg").mkdir(exist_ok=True)
    (out / "contacts").mkdir(exist_ok=True)
    seg_files, con_files = [], []
    for r in results:
        up = upright_image(r.frame.path, r.k)
        sp, cp = out / "seg" / f"{r.frame.i:06d}.jpg", out / "contacts" / f"{r.frame.i:06d}.jpg"
        plots.seg_overlay(r, up, sp)
        plots.boundary_overlay(r, up, cp)
        seg_files.append(sp)
        con_files.append(cp)
    plots.contact_sheet(seg_files, out / "sheet_segmentation.jpg")
    plots.contact_sheet(con_files, out / "sheet_contacts.jpg")

    # Raw projected points.
    pts_list, src = [], []
    for r in results:
        if len(r.valid):
            g = r.ghost[r.valid]
            pts_list.append(g[:, [0, 2]])
            src += [r.frame.i] * len(g)
    pts = np.vstack(pts_list) if pts_list else np.zeros((0, 2))
    np.savez_compressed(out / "projected_points.npz", xz=pts, frame=np.array(src))
    plots.topdown_raw(results, scan.frame, out / "topdown_raw.png",
                      f"{scan_dir.name}: {len(pts)} raw wall/floor contacts projected to the locked floor")

    # Wall extraction.
    center = np.mean([scan.frame.world_to_ghost(camera_center(r.frame.T))[[0, 2]] for r in results], axis=0)
    lines = walls.merge_collinear(walls.ransac_lines(pts), pts) if len(pts) else []
    dom = walls.dominant(lines)
    four, corners, err = walls.four_wall_polygon(dom, center)
    issues = validate_room(corners) if corners is not None else [err]
    cover = []
    if corners is not None:
        for i, l in enumerate(four):
            cover.append(walls.wall_coverage(l, pts, corners[i], corners[(i + 1) % 4]))
    plots.topdown_fit(pts, lines, four, corners, results, scan.frame, out / "topdown_fit.png",
                      f"{scan_dir.name}: {len(lines)} lines found, {len(dom)} dominant, 4 used")

    pred = dict(
        experiment="E1", note="RESEARCH SPIKE — NOT PRODUCTION. Blind prediction: produced without ground truth.",
        created=time.strftime("%Y-%m-%dT%H:%M:%S"), runtime_s=round(time.time() - t0, 1),
        scan=scan_dir.name, device=scan.meta.get("device"), os=scan.meta.get("systemVersion"),
        model=dict(file=MODEL.name, weight_sha256="2f4de3dfed1aeced35ad7096d7c8721fcfb59571110a9e9138d73a7e56480408"),
        params=params_snapshot(),
        capture=dict(
            frames_saved=len(scan.frames), poses_logged=len(scan.poses_t), keyframes=len(keys),
            yaw_bins_covered=len({int(f.yaw // 10) for f in keys}),
            rejected={k: sum(1 for f in scan.frames if f.rejected.startswith(k)) for k in ("tracking", "turning", "not sharpest")},
            tracking_states=sorted({f.tracking for f in scan.frames}),
            pitch_deg_median=float(np.median([f.pitch for f in keys])),
            camera_height_m_median=float(np.median([f.meta.get("cameraHeightM", np.nan) for f in keys])),
            camera_wander_m=float(np.max(np.linalg.norm(
                np.array([scan.frame.world_to_ghost(camera_center(f.T))[[0, 2]] for f in keys]) - center, axis=1))),
        ),
        segmentation=dict(
            per_frame=[dict(i=r.frame.i, yaw=round(r.frame.yaw, 1), pitch=round(r.frame.pitch, 1), k=r.k,
                            fractions={k: round(v, 4) for k, v in r.fractions.items()},
                            floor_columns=r.contacts.columns_with_floor, columns=r.contacts.columns_total,
                            contacts=int(len(r.contacts.gx)), on_floor=int(r.valid.sum()) if len(r.valid) else 0)
                       for r in results],
        ),
        points=dict(total=int(len(pts)), scan_center_xz=center.tolist()),
        lines=[dict(point=l.point.tolist(), direction=l.direction.tolist(), count=l.count, rms_m=l.rms, span_m=l.span,
                    dominant=any(l is d for d in dom), used=any(l is f for f in (four or [])))
               for l in lines],
        natural_dominant_line_count=len(dom),
        footprint=None if corners is None else dict(
            corners_xz=corners.tolist(),
            wall_lengths_m=walls.wall_lengths(corners).tolist(),
            interior_angles_deg=walls.interior_angles_deg(corners).tolist(),
            wall_boundary_coverage=cover,
            wall_inliers=[l.count for l in four],
            wall_rms_m=[l.rms for l in four],
        ),
        room_validator_issues=issues,
    )
    text = json.dumps(pred, indent=2)
    (out / "prediction.json").write_text(text)
    digest = hashlib.sha256(text.encode()).hexdigest()
    (out / "prediction.sha256").write_text(digest + "\n")
    (out / "diagnostics_pixel_to_ghost.md").write_text(
        "# Pixel -> Ghost diagnostic (blind run)\n\nRays are unit vectors. Floor hit is in ARKit world; Ghost is relative to the floor lock.\n\n"
        + diagnostics_table(results, scan.frame) + "\n")
    print(f"prediction sealed: {digest[:16]}…  lines={len(lines)} dominant={len(dom)} issues={issues}")
    if corners is not None:
        print("wall lengths (m):", np.round(walls.wall_lengths(corners), 3), " angles:", np.round(walls.interior_angles_deg(corners), 1))
    print(f"artifacts in {out}")


if __name__ == "__main__":
    main()
