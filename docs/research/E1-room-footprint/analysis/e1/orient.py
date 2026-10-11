"""RESEARCH SPIKE — NOT PRODUCTION.

Image orientation and segmentation-tile geometry.

ARKit's captured image is always in the sensor's landscape orientation. When the
phone is held in portrait the room appears rotated 90 degrees in that image, and
a segmentation model trained on upright photos should be fed an upright image.
We rotate by k quarter-turns clockwise (k chosen from gravity, not from UI
orientation), segment, and map every result pixel back to sensor pixels before
any geometry is done. All coordinates here are *continuous* pixel coordinates:
pixel index i covers [i, i+1) and has its center at i + 0.5.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

MODEL_SIZE = 448  # Apple DETR segmentation input/output is 448x448.


def upright_size(w: int, h: int, k: int) -> tuple[int, int]:
    return (w, h) if k % 2 == 0 else (h, w)


def sensor_to_upright(u, v, k: int, w: int, h: int):
    """Map continuous sensor coords to the image rotated k*90 degrees clockwise."""
    u = np.asarray(u, float)
    v = np.asarray(v, float)
    k %= 4
    if k == 0:
        return u, v
    if k == 1:
        return h - v, u
    if k == 2:
        return w - u, h - v
    return v, w - u


def upright_to_sensor(x, y, k: int, w: int, h: int):
    """Inverse of sensor_to_upright. w, h are the *sensor* image size."""
    x = np.asarray(x, float)
    y = np.asarray(y, float)
    k %= 4
    if k == 0:
        return x, y
    if k == 1:
        return y, h - x
    if k == 2:
        return w - x, h - y
    return w - y, x


def rotate_direction(du, dv, k: int):
    """Rotate an image-plane direction by k quarter-turns clockwise (v points down)."""
    for _ in range(k % 4):
        du, dv = -dv, du
    return du, dv


def choose_upright_rotation(transform) -> int:
    """Quarter-turns (clockwise) that make world-up point to the top of the image."""
    r = np.asarray(transform)[:3, :3]
    up_cam = r.T @ np.array([0.0, 1.0, 0.0])
    du, dv = up_cam[0], -up_cam[1]  # camera +y is image -v
    best_k, best = 0, -np.inf
    for k in range(4):
        _, rv = rotate_direction(du, dv, k)
        if -rv > best:
            best_k, best = k, -rv
    return best_k


@dataclass(frozen=True)
class Tile:
    """A square crop of the upright image, resized to MODEL_SIZE for the model."""
    ox: float
    oy: float
    size: float

    def grid_to_upright(self, i, j):
        s = self.size / MODEL_SIZE
        return self.ox + (np.asarray(i, float) + 0.5) * s, self.oy + (np.asarray(j, float) + 0.5) * s

    def upright_to_index(self, x, y):
        s = self.size / MODEL_SIZE
        i = np.floor((np.asarray(x, float) - self.ox) / s).astype(int)
        j = np.floor((np.asarray(y, float) - self.oy) / s).astype(int)
        return i, j

    def edge_distance(self, x, y):
        x = np.asarray(x, float) - self.ox
        y = np.asarray(y, float) - self.oy
        return np.minimum(np.minimum(x, self.size - x), np.minimum(y, self.size - y))


def square_tiles(uw: int, uh: int) -> list[Tile]:
    """Two overlapping full-height/width square crops that cover the upright image
    at its native aspect ratio (the model card evaluates with square crops; a
    single center crop would cut off the bottom of a portrait frame, which is
    exactly where the floor/wall junction usually is)."""
    s = min(uw, uh)
    if uh >= uw:
        return [Tile(0.0, 0.0, s), Tile(0.0, float(uh - s), s)]
    return [Tile(0.0, 0.0, s), Tile(float(uw - s), 0.0, s)]


def stitched_grid_shape(uw: int, uh: int) -> tuple[int, int, float]:
    """(grid_w, grid_h, cell) for the stitched label grid at model resolution."""
    cell = min(uw, uh) / MODEL_SIZE
    return int(round(uw / cell)), int(round(uh / cell)), cell


def stitch_labels(tile_labels: list[np.ndarray], tiles: list[Tile], uw: int, uh: int) -> tuple[np.ndarray, float]:
    """Merge per-tile label maps into one upright grid, taking each cell from the
    tile where it lies farthest from a crop edge."""
    gw, gh, cell = stitched_grid_shape(uw, uh)
    gx, gy = np.meshgrid(np.arange(gw), np.arange(gh))
    cx, cy = (gx + 0.5) * cell, (gy + 0.5) * cell
    best = np.full((gh, gw), -np.inf)
    out = np.zeros((gh, gw), dtype=np.int32)
    for lab, t in zip(tile_labels, tiles):
        d = t.edge_distance(cx, cy)
        i, j = t.upright_to_index(cx, cy)
        ok = (d > best) & (i >= 0) & (j >= 0) & (i < MODEL_SIZE) & (j < MODEL_SIZE)
        out[ok] = lab[j[ok], i[ok]]
        best[ok] = d[ok]
    return out, cell


def grid_to_sensor(gx, gy, cell: float, k: int, w: int, h: int):
    """Stitched-grid continuous coords (cell units) -> sensor pixel coords."""
    return upright_to_sensor(np.asarray(gx, float) * cell, np.asarray(gy, float) * cell, k, w, h)
