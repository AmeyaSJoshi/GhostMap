"""RESEARCH SPIKE — NOT PRODUCTION.

Loading a ghostmap-e1-scan-v1 recording, per-frame quality signals, and
keyframe selection. Gate parameters are fixed here before any real scan.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from .camera import GhostFrame, Intrinsics, pitch_deg, yaw_deg_in_ghost

MAX_ANGULAR_SPEED_DEG_S = 45.0   # reject frames captured while turning faster than this
YAW_BIN_DEG = 10.0               # one keyframe per 10 degrees of heading
ANGULAR_SPEED_WINDOW_S = 0.10    # +/- window used to estimate turn rate at a frame


@dataclass(eq=False)
class Frame:
    i: int
    path: Path
    t: float
    w: int
    h: int
    intr: Intrinsics
    T: np.ndarray
    tracking: str
    meta: dict
    yaw: float = 0.0
    pitch: float = 0.0
    angular_speed: float = 0.0
    sharpness: float = 0.0
    brightness: float = 0.0
    rejected: str = ""


@dataclass
class Scan:
    root: Path
    meta: dict
    frame: GhostFrame
    frames: list[Frame]
    poses_t: np.ndarray
    poses_R: np.ndarray
    poses_tracking: list[str]


def load(root) -> Scan:
    root = Path(root)
    meta = json.loads((root / "metadata.json").read_text())
    lock = meta["floorLock"]
    gf = GhostFrame.from_floor_lock(np.array(lock["hitWorld"]), np.array(lock["cameraTransform"]))
    frames = []
    for line in (root / "frames.jsonl").read_text().splitlines():
        if not line.strip():
            continue
        m = json.loads(line)
        frames.append(Frame(m["i"], root / m["file"], m["t"], m["w"], m["h"],
                            Intrinsics.from_matrix_rows(m["K"]), np.array(m["T"], float), m["tracking"], m))
    pt, pr, ptr = [], [], []
    poses = root / "poses.jsonl"
    if poses.exists():
        for line in poses.read_text().splitlines():
            if line.strip():
                p = json.loads(line)
                pt.append(p["t"])
                pr.append(np.array(p["T"], float)[:3, :3])
                ptr.append(p["tracking"])
    scan = Scan(root, meta, gf, frames, np.array(pt), np.array(pr) if pr else np.zeros((0, 3, 3)), ptr)
    for f in frames:
        f.yaw = yaw_deg_in_ghost(f.T, gf)
        f.pitch = pitch_deg(f.T)
        f.angular_speed = angular_speed_at(scan, f.t, f.T[:3, :3])
    return scan


def rotation_angle_deg(ra: np.ndarray, rb: np.ndarray) -> float:
    c = (np.trace(ra.T @ rb) - 1.0) / 2.0
    return float(np.degrees(np.arccos(np.clip(c, -1.0, 1.0))))


def angular_speed_at(scan: Scan, t: float, r_now: np.ndarray) -> float:
    """Turn rate (deg/s) from the full-rate pose log around time t."""
    if len(scan.poses_t) < 2:
        return 0.0
    lo = np.searchsorted(scan.poses_t, t - ANGULAR_SPEED_WINDOW_S)
    hi = min(np.searchsorted(scan.poses_t, t + ANGULAR_SPEED_WINDOW_S), len(scan.poses_t) - 1)
    lo = min(lo, len(scan.poses_t) - 1)
    dt = scan.poses_t[hi] - scan.poses_t[lo]
    if dt <= 0:
        return 0.0
    return rotation_angle_deg(scan.poses_R[lo], scan.poses_R[hi]) / dt


def image_quality(path: Path) -> tuple[float, float]:
    """(sharpness = variance of Laplacian on a 480 px-wide gray image, mean brightness 0..255)."""
    im = Image.open(path).convert("L")
    im = im.resize((480, int(480 * im.height / im.width)))
    a = np.asarray(im, float)
    return float(ndimage.laplace(a).var()), float(a.mean())


def select_keyframes(scan: Scan) -> list[Frame]:
    """Gate (tracking normal, turn rate) then keep the sharpest frame per 10-degree heading bin."""
    best: dict[int, Frame] = {}
    for f in scan.frames:
        if f.tracking != "normal":
            f.rejected = f"tracking {f.tracking}"
            continue
        if f.angular_speed > MAX_ANGULAR_SPEED_DEG_S:
            f.rejected = f"turning {f.angular_speed:.0f} deg/s"
            continue
        f.sharpness, f.brightness = image_quality(f.path)
        b = int(f.yaw // YAW_BIN_DEG)
        if b not in best or f.sharpness > best[b].sharpness:
            best[b] = f
    keep = sorted(best.values(), key=lambda f: f.yaw)
    kept = {id(f) for f in keep}
    for f in scan.frames:
        if not f.rejected and id(f) not in kept:
            f.rejected = "not sharpest in its heading bin"
    return keep
