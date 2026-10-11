"""RESEARCH SPIKE — NOT PRODUCTION.

End-to-end per-frame chain:
  sensor JPEG -> upright rotation (from gravity) -> two square tiles -> DETR
  (tools/segment) -> stitched label grid -> wall-over-floor contacts ->
  sensor pixels -> camera ray -> world ray -> locked floor -> Ghost XZ.
"""

from __future__ import annotations

import subprocess
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
from PIL import Image

from . import boundary, orient, segmap
from .camera import camera_center, camera_to_world_dir, intersect_floor, pixel_to_camera_dir
from .scan_io import Frame, Scan

E1_ROOT = Path(__file__).resolve().parents[2]
SEGMENT_BIN = E1_ROOT / "build" / "segment"
MODEL = E1_ROOT / "models" / "DETRResnet50SemanticSegmentationF16.mlpackage"

_TRANSPOSE = {1: Image.Transpose.ROTATE_270, 2: Image.Transpose.ROTATE_180, 3: Image.Transpose.ROTATE_90}


def upright_image(path: Path, k: int) -> Image.Image:
    im = Image.open(path).convert("RGB")
    return im.transpose(_TRANSPOSE[k]) if k % 4 else im


@dataclass
class FrameResult:
    frame: Frame
    k: int
    labels: np.ndarray | None = None
    cell: float = 1.0
    contacts: boundary.Contacts | None = None
    sensor_uv: np.ndarray = field(default_factory=lambda: np.zeros((0, 2)))
    world_dirs: np.ndarray = field(default_factory=lambda: np.zeros((0, 3)))
    floor_world: np.ndarray = field(default_factory=lambda: np.zeros((0, 3)))
    ghost: np.ndarray = field(default_factory=lambda: np.zeros((0, 3)))
    valid: np.ndarray = field(default_factory=lambda: np.zeros(0, bool))
    fractions: dict = field(default_factory=dict)


def prepare_tiles(frames: list[Frame], tiles_dir: Path) -> dict[int, int]:
    tiles_dir.mkdir(parents=True, exist_ok=True)
    ks = {}
    for f in frames:
        k = orient.choose_upright_rotation(f.T)
        ks[f.i] = k
        im = upright_image(f.path, k)
        for n, t in enumerate(orient.square_tiles(im.width, im.height)):
            box = (int(t.ox), int(t.oy), int(t.ox + t.size), int(t.oy + t.size))
            im.crop(box).resize((orient.MODEL_SIZE, orient.MODEL_SIZE), Image.Resampling.BICUBIC) \
                .save(tiles_dir / f"{f.i:06d}_t{n}.png")
    return ks


def run_segmentation(tiles_dir: Path, labels_dir: Path) -> str:
    if not SEGMENT_BIN.exists():
        raise FileNotFoundError(f"build the tool first: swiftc -O tools/segment.swift -o {SEGMENT_BIN}")
    out = subprocess.run([str(SEGMENT_BIN), str(MODEL), str(tiles_dir), str(labels_dir)],
                         check=True, capture_output=True, text=True)
    return out.stdout.strip()


def project_contacts(f: Frame, k: int, contacts: boundary.Contacts, cell: float, floor_y: float):
    """Grid contacts -> sensor pixels -> world rays -> floor points."""
    u, v = orient.grid_to_sensor(contacts.gx, contacts.gy, cell, k, f.w, f.h)
    d_cam = pixel_to_camera_dir(f.intr, u, v).reshape(-1, 3)
    d_world = camera_to_world_dir(f.T, d_cam)
    pts, valid = intersect_floor(camera_center(f.T), d_world, floor_y)
    return np.stack([u, v], -1).reshape(-1, 2), d_world, pts, valid


def process(scan: Scan, frames: list[Frame], work: Path) -> list[FrameResult]:
    ks = prepare_tiles(frames, work / "tiles")
    log = run_segmentation(work / "tiles", work / "labels")
    print("  ", log)
    results = []
    for f in frames:
        k = ks[f.i]
        uw, uh = orient.upright_size(f.w, f.h, k)
        tiles = orient.square_tiles(uw, uh)
        labs = [np.asarray(Image.open(work / "labels" / f"{f.i:06d}_t{n}.png")) for n in range(len(tiles))]
        grid, cell = orient.stitch_labels(labs, tiles, uw, uh)
        c = boundary.extract(grid)
        r = FrameResult(f, k, grid, cell, c, fractions=segmap.class_fractions(grid))
        if len(c.gx):
            r.sensor_uv, r.world_dirs, r.floor_world, r.valid = project_contacts(f, k, c, cell, scan.frame.floor_y)
            r.ghost = scan.frame.world_to_ghost(r.floor_world)
        results.append(r)
    return results
