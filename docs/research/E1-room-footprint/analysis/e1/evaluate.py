"""RESEARCH SPIKE — NOT PRODUCTION.

Raw-metric and scale-normalized evaluation of a predicted floor polygon against
a tape-measured ground truth. Only ONE uniform scale (plus rotation and
translation) is ever fitted. No reflection, no per-axis or per-wall scaling.
"""

from __future__ import annotations

import numpy as np

from .walls import interior_angles_deg, wall_lengths


def similarity_align(src: np.ndarray, dst: np.ndarray, allow_scale: bool = True):
    """Umeyama: find s, R (proper rotation), t minimizing sum |s R src + t - dst|^2.
    Returns (s, R, t, aligned_src)."""
    src = np.asarray(src, float)
    dst = np.asarray(dst, float)
    mu_s, mu_d = src.mean(0), dst.mean(0)
    xs, xd = src - mu_s, dst - mu_d
    cov = xd.T @ xs / len(src)
    u, d, vt = np.linalg.svd(cov)
    sgn = np.eye(2)
    if np.linalg.det(u) * np.linalg.det(vt) < 0:
        sgn[1, 1] = -1.0  # forbid reflection: a mirrored room is a topology error
    r = u @ sgn @ vt
    var_s = (xs ** 2).sum() / len(src)
    s = float(np.trace(np.diag(d) @ sgn) / var_s) if allow_scale else 1.0
    t = mu_d - s * r @ mu_s
    return s, r, t, (s * (r @ src.T)).T + t


def polygon_area(poly) -> float:
    p = np.asarray(poly, float)
    x, y = p[:, 0], p[:, 1]
    return 0.5 * float(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))


def _ccw(poly):
    p = np.asarray(poly, float)
    return p if polygon_area(p) > 0 else p[::-1]


def clip_convex(subject, clipper):
    """Sutherland–Hodgman intersection of two convex polygons."""
    out = list(_ccw(subject))
    c = _ccw(clipper)
    for i in range(len(c)):
        a, b = c[i], c[(i + 1) % len(c)]
        inp, out = out, []
        if not inp:
            break

        def inside(p):
            return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]) >= 0

        def cross_pt(p, q):
            d1, d2 = q - p, b - a
            den = d1[0] * d2[1] - d1[1] * d2[0]
            t = ((a[0] - p[0]) * d2[1] - (a[1] - p[1]) * d2[0]) / den
            return p + t * d1

        for j in range(len(inp)):
            p, q = np.asarray(inp[j - 1]), np.asarray(inp[j])
            if inside(q):
                if not inside(p):
                    out.append(cross_pt(p, q))
                out.append(q)
            elif inside(p):
                out.append(cross_pt(p, q))
    return np.array(out)


def iou_convex(a, b) -> float:
    inter = clip_convex(a, b)
    ia = abs(polygon_area(inter)) if len(inter) >= 3 else 0.0
    ua = abs(polygon_area(a)) + abs(polygon_area(b)) - ia
    return ia / ua if ua > 0 else 0.0


def gt_quad_from_measurements(a, b, c, d, diag_ab_cd, diag_bc_da=None):
    """Ground-truth quadrilateral from four wall lengths A, B, C, D (walking
    around the room in one direction) and the diagonal between the A/B corner
    and the C/D corner. Corners returned in order: DA, AB, BC, CD so that
    wall i runs corner i -> corner i+1 (A = DA->AB, B = AB->BC, ...).

    With no diagonal the room is assumed rectangular (A~C, B~D averaged).
    Returns (corners, diag2_predicted_from_construction).
    """
    if diag_ab_cd is None:
        w, h = (a + c) / 2.0, (b + d) / 2.0
        q = np.array([[0, 0], [w, 0], [w, h], [0, h]], float)
        return q, float(np.hypot(w, h))
    p_ab = np.array([0.0, 0.0])
    p_cd = np.array([diag_ab_cd, 0.0])
    # Triangle AB, BC, CD on one side (sides B, C), triangle CD, DA, AB on the other (D, A).
    p_bc = _third_vertex(p_ab, p_cd, b, c, side=+1)
    p_da = _third_vertex(p_ab, p_cd, a, d, side=-1)
    q = np.array([p_da, p_ab, p_bc, p_cd])
    return q, float(np.linalg.norm(p_bc - p_da))


def _third_vertex(p0, p1, r0, r1, side):
    """Point at distance r0 from p0 and r1 from p1, on the given side of p0->p1."""
    base = np.linalg.norm(p1 - p0)
    x = (r0 ** 2 - r1 ** 2 + base ** 2) / (2 * base)
    y2 = r0 ** 2 - x ** 2
    if y2 < 0:
        raise ValueError("measurements are inconsistent (triangle inequality fails)")
    ex = (p1 - p0) / base
    ey = np.array([-ex[1], ex[0]])
    return p0 + x * ex + side * np.sqrt(y2) * ey


def best_correspondence(pred: np.ndarray, gt: np.ndarray, allow_scale: bool):
    """Try every cyclic start and both traversal directions of the predicted
    corners; keep the proper similarity with the lowest corner RMS. Traversal
    reversal is a relabeling (clockwise vs counter-clockwise), not a mirror."""
    n = len(pred)
    best = None
    for rev in (False, True):
        base = pred[::-1] if rev else pred
        for shift in range(n):
            p = np.roll(base, -shift, axis=0)
            s, r, t, al = similarity_align(p, gt, allow_scale)
            rms = float(np.sqrt(((al - gt) ** 2).sum(1).mean()))
            if best is None or rms < best["rms"]:
                best = dict(rms=rms, s=s, R=r, t=t, aligned=al, ordered=p, shift=shift, reversed=rev)
    return best


def evaluate(pred_corners: np.ndarray, gt_corners: np.ndarray) -> dict:
    pred = np.asarray(pred_corners, float)
    gt = np.asarray(gt_corners, float)
    gt_len = wall_lengths(gt)
    gt_ang = interior_angles_deg(gt)
    size = float(np.mean([np.linalg.norm(gt[0] - gt[2]), np.linalg.norm(gt[1] - gt[3])]))

    raw = best_correspondence(pred, gt, allow_scale=False)
    p_len = wall_lengths(raw["ordered"])
    p_ang = interior_angles_deg(raw["ordered"])
    raw_res = dict(
        pred_lengths=p_len.tolist(), gt_lengths=gt_len.tolist(),
        abs_err=(p_len - gt_len).tolist(), pct_err=((p_len - gt_len) / gt_len * 100).tolist(),
        pred_angles=p_ang.tolist(), gt_angles=gt_ang.tolist(),
        angle_err=(p_ang - gt_ang).tolist(),
        area_pred=abs(polygon_area(pred)), area_gt=abs(polygon_area(gt)),
        rigid_corner_rms=raw["rms"],
        rigid_iou=iou_convex(raw["aligned"], gt),
    )

    norm = best_correspondence(pred, gt, allow_scale=True)
    al = norm["aligned"]
    n_len = wall_lengths(al)
    pn = wall_lengths(norm["ordered"])  # scale-free ratios use the similarity correspondence
    ratio_pred = pn / pn.sum()
    ratio_gt = gt_len / gt_len.sum()
    pair = []
    for i in range(len(gt)):
        j = (i + 1) % len(gt)
        pair.append(((pn[i] / pn[j]) / (gt_len[i] / gt_len[j]) - 1) * 100)
    corner_err = np.linalg.norm(al - gt, axis=1)
    norm_res = dict(
        scale=norm["s"],
        normalized_lengths=n_len.tolist(),
        normalized_length_pct_err=((n_len - gt_len) / gt_len * 100).tolist(),
        perimeter_share_pct_err=((ratio_pred - ratio_gt) / ratio_gt * 100).tolist(),
        adjacent_ratio_pct_err=pair,
        corner_err_m=corner_err.tolist(),
        corner_err_pct_of_diag=(corner_err / size * 100).tolist(),
        corner_rms_pct_of_diag=float(np.sqrt((corner_err ** 2).mean()) / size * 100),
        angle_err=(interior_angles_deg(al) - gt_ang).tolist(),
        iou=iou_convex(al, gt),
        same_correspondence_as_raw=(norm["shift"] == raw["shift"] and norm["reversed"] == raw["reversed"]),
        aligned_corners=al.tolist(),
    )
    return dict(raw=raw_res, normalized=norm_res, gt_diag_mean=size,
                correspondence=dict(shift=norm["shift"], reversed=norm["reversed"]))
