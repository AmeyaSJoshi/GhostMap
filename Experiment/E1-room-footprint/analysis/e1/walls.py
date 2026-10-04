"""RESEARCH SPIKE — NOT PRODUCTION.

Wall-line extraction from projected floor-contact samples (Ghost XZ, meters).

`tls_fit`, `intersect` and `derive_corners` are line-for-line ports of
shared/com.ghostmap.shared/Runtime/Geometry/WallFitting.cs (same gates: 8
samples, 0.40 m span, 5 degree crossing). Finding *which* samples belong to
which wall is new: the sweep had the user do that by holding a button per wall;
here a sequential RANSAC does it. Every parameter is fixed before real data.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

MIN_SAMPLE_COUNT = 8
MIN_SPAN_M = 0.40
DEGENERATE_EPS = 1e-8
MIN_INTERSECTION_ANGLE_DEG = 5.0

RANSAC_ITERATIONS = 3000
RANSAC_INLIER_M = 0.05
RANSAC_MIN_INLIERS = 40
MAX_LINES = 8
MERGE_ANGLE_DEG = 8.0
MERGE_OFFSET_M = 0.15
DOMINANT_FRACTION = 0.15   # a line is "dominant" if it has >= 15% of the best line's support
COVERAGE_BIN_M = 0.05


@dataclass
class Line:
    point: np.ndarray       # centroid (x, z)
    direction: np.ndarray   # unit (x, z)
    rms: float
    span: float
    count: int
    inliers: np.ndarray | None = None   # indices into the sample array

    @property
    def normal(self):
        return np.array([-self.direction[1], self.direction[0]])

    def distance(self, pts):
        return np.abs((np.asarray(pts) - self.point) @ self.normal)


def tls_fit(samples) -> tuple[Line | None, str]:
    """Port of WallFitting.TryFitWallLine. Returns (line, rejection)."""
    p = np.asarray(samples, float)
    if len(p) < MIN_SAMPLE_COUNT:
        return None, "TooFewSamples"
    if not np.isfinite(p).all():
        return None, "NonFiniteSample"
    c = p.mean(axis=0)
    d = p - c
    sxx, szz, sxz = (d[:, 0] ** 2).sum(), (d[:, 1] ** 2).sum(), (d[:, 0] * d[:, 1]).sum()
    trace, delta = sxx + szz, sxx - szz
    disc = np.sqrt(delta * delta + 4 * sxz * sxz)
    emax, emin = 0.5 * (trace + disc), max(0.5 * (trace - disc), 0.0)
    if emax <= DEGENERATE_EPS:
        return None, "Degenerate"
    c1 = np.array([sxz, emax - sxx])
    c2 = np.array([emax - szz, sxz])
    v = c1 if c1 @ c1 >= c2 @ c2 else c2
    n = np.linalg.norm(v)
    if n <= DEGENERATE_EPS:
        return None, "Degenerate"
    v = v / n
    proj = d @ v
    span = float(proj.max() - proj.min())
    if span < MIN_SPAN_M:
        return None, "SpanTooShort"
    return Line(c, v, float(np.sqrt(emin / len(p))), span, len(p)), "None"


def intersect(a: Line, b: Line):
    """Port of WallFitting.TryIntersectWallLinesXZ. None if within 5 degrees of parallel."""
    den = a.direction[0] * b.direction[1] - a.direction[1] * b.direction[0]
    if abs(den) < np.sin(np.radians(MIN_INTERSECTION_ANGLE_DEG)):
        return None
    off = b.point - a.point
    t = (off[0] * b.direction[1] - off[1] * b.direction[0]) / den
    r = a.point + a.direction * t
    return r if np.isfinite(r).all() else None


def derive_corners(walls: list[Line]):
    """Port of WallFitting.TryDeriveCorners: corner[i] = intersect(wall[i-1], wall[i])."""
    n = len(walls)
    out = []
    for i in range(n):
        c = intersect(walls[i - 1], walls[i])
        if c is None:
            return None, f"Walls {(i - 1) % n + 1} and {i + 1} are too close to parallel to form a corner."
        out.append(c)
    return np.array(out), ""


def ransac_lines(pts: np.ndarray, seed: int = 0) -> list[Line]:
    """Sequential RANSAC: repeatedly take the best-supported line, TLS-refine it,
    remove its inliers. Deterministic for a given seed."""
    rng = np.random.default_rng(seed)
    remaining = np.arange(len(pts))
    lines: list[Line] = []
    while len(remaining) >= RANSAC_MIN_INLIERS and len(lines) < MAX_LINES:
        sub = pts[remaining]
        best = None
        for _ in range(RANSAC_ITERATIONS):
            i, j = rng.choice(len(sub), 2, replace=False)
            d = sub[j] - sub[i]
            n = np.linalg.norm(d)
            if n < 0.2:
                continue
            nrm = np.array([-d[1], d[0]]) / n
            inl = np.abs((sub - sub[i]) @ nrm) < RANSAC_INLIER_M
            cnt = int(inl.sum())
            if best is None or cnt > best[0]:
                best = (cnt, inl)
        if best is None or best[0] < RANSAC_MIN_INLIERS:
            break
        inl = best[1]
        for _ in range(3):  # refine: TLS on inliers, recompute inliers
            line, why = tls_fit(sub[inl])
            if line is None:
                break
            inl = line.distance(sub) < RANSAC_INLIER_M
        if line is None or inl.sum() < RANSAC_MIN_INLIERS:
            remaining = remaining[~best[1]]
            continue
        line, _ = tls_fit(sub[inl])
        if line is None:
            remaining = remaining[~inl]
            continue
        line.inliers = remaining[inl]
        lines.append(line)
        remaining = remaining[~inl]
    return lines


def merge_collinear(lines: list[Line], pts: np.ndarray) -> list[Line]:
    """Merge lines that are the same wall split in two (angle < 8 deg, offset < 0.15 m)."""
    lines = list(lines)
    changed = True
    while changed:
        changed = False
        for a in range(len(lines)):
            for b in range(a + 1, len(lines)):
                la, lb = lines[a], lines[b]
                ang = np.degrees(np.arccos(min(1.0, abs(la.direction @ lb.direction))))
                off = max(la.distance(lb.point[None])[0], lb.distance(la.point[None])[0])
                if ang < MERGE_ANGLE_DEG and off < MERGE_OFFSET_M:
                    idx = np.concatenate([la.inliers, lb.inliers])
                    m, _ = tls_fit(pts[idx])
                    if m is not None:
                        m.inliers = idx
                        lines[a] = m
                        del lines[b]
                        changed = True
                        break
            if changed:
                break
    return lines


def azimuth_deg(point, center) -> float:
    d = np.asarray(point) - np.asarray(center)
    return float(np.degrees(np.arctan2(d[0], d[1])) % 360.0)


def dominant(lines: list[Line]) -> list[Line]:
    if not lines:
        return []
    top = max(l.count for l in lines)
    return [l for l in lines if l.count >= DOMINANT_FRACTION * top]


def four_wall_polygon(lines: list[Line], center):
    """Top-4 lines by support, ordered by azimuth of their inlier centroid around
    the scan center, then corners exactly as WallFitting derives them."""
    if len(lines) < 4:
        return None, None, f"only {len(lines)} lines available"
    four = sorted(lines, key=lambda l: -l.count)[:4]
    four = sorted(four, key=lambda l: azimuth_deg(l.point, center))
    corners, err = derive_corners(four)
    return four, corners, err


def wall_coverage(line: Line, pts: np.ndarray, start, end) -> float:
    """Fraction of the wall segment start->end (5 cm bins) with at least one inlier."""
    seg = np.asarray(end) - np.asarray(start)
    length = np.linalg.norm(seg)
    if length < 1e-6 or line.inliers is None:
        return 0.0
    u = (pts[line.inliers] - start) @ (seg / length)
    nb = max(1, int(np.ceil(length / COVERAGE_BIN_M)))
    bins = np.floor(u / COVERAGE_BIN_M).astype(int)
    bins = bins[(bins >= 0) & (bins < nb)]
    return len(np.unique(bins)) / nb


def interior_angles_deg(poly: np.ndarray) -> np.ndarray:
    n = len(poly)
    out = []
    for i in range(n):
        a, b, c = poly[i - 1], poly[i], poly[(i + 1) % n]
        v1, v2 = a - b, c - b
        cosang = v1 @ v2 / (np.linalg.norm(v1) * np.linalg.norm(v2))
        out.append(np.degrees(np.arccos(np.clip(cosang, -1, 1))))
    return np.array(out)


def is_convex_simple(poly: np.ndarray) -> bool:
    n = len(poly)
    signs = []
    for i in range(n):
        a, b, c = poly[i], poly[(i + 1) % n], poly[(i + 2) % n]
        signs.append(np.sign((b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0])))
    return all(s > 0 for s in signs) or all(s < 0 for s in signs)


def wall_lengths(poly: np.ndarray) -> np.ndarray:
    return np.linalg.norm(np.roll(poly, -1, axis=0) - poly, axis=1)
