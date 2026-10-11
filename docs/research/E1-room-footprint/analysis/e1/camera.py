"""RESEARCH SPIKE — NOT PRODUCTION.

Pixel -> camera ray -> world ray -> locked floor plane -> Ghost coordinates.

Conventions (all ARKit, right-handed, meters):

* ARKit world: +Y is up (gravity-aligned), right-handed.
* ARKit camera space: +X right, +Y up, +Z toward the viewer, defined for the
  sensor's native landscape-right orientation. The camera looks down -Z.
* Pixel coordinates are in the *captured image* (sensor orientation, e.g.
  1920x1440), origin top-left, +u right, +v down. ARKit's intrinsics are in
  these pixels.

So a pixel (u, v) has the camera-space direction

    ((u - cx) / fx, -(v - cy) / fy, -1)

and projecting a camera-space point back gives

    u = cx + fx * x / -z,   v = cy - fy * y / -z.

The Ghost frame matches shared/.../GhostCoordinateFrame.cs and
FloorLockController: origin is the floor hit, up is +Y, forward is the camera
forward projected onto the floor, right = physical right. Ghost coordinates are
projections onto those physical axes, so they come out identical whether they
are computed from ARKit's right-handed world or from Unity's left-handed
conversion of it (z negated). test_camera.py proves that.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

UP = np.array([0.0, 1.0, 0.0])


@dataclass(frozen=True)
class Intrinsics:
    fx: float
    fy: float
    cx: float
    cy: float

    @staticmethod
    def from_matrix_rows(rows) -> "Intrinsics":
        k = np.asarray(rows, dtype=float)
        return Intrinsics(fx=k[0, 0], fy=k[1, 1], cx=k[0, 2], cy=k[1, 2])


def pixel_to_camera_dir(intr: Intrinsics, u, v) -> np.ndarray:
    """Unnormalized camera-space ray directions for pixels. Shape (..., 3)."""
    u = np.asarray(u, dtype=float)
    v = np.asarray(v, dtype=float)
    return np.stack(
        [(u - intr.cx) / intr.fx, -(v - intr.cy) / intr.fy, -np.ones_like(u)], axis=-1
    )


def camera_point_to_pixel(intr: Intrinsics, p_cam) -> np.ndarray:
    """Inverse of pixel_to_camera_dir for points in front of the camera (z < 0)."""
    p = np.asarray(p_cam, dtype=float)
    depth = -p[..., 2]
    return np.stack([intr.cx + intr.fx * p[..., 0] / depth, intr.cy - intr.fy * p[..., 1] / depth], axis=-1)


def camera_to_world_dir(transform: np.ndarray, d_cam: np.ndarray) -> np.ndarray:
    """Rotate camera-space directions into world space with a 4x4 camera transform."""
    return np.asarray(d_cam) @ np.asarray(transform)[:3, :3].T


def camera_center(transform: np.ndarray) -> np.ndarray:
    return np.asarray(transform)[:3, 3].copy()


def camera_forward(transform: np.ndarray) -> np.ndarray:
    """World direction the camera looks along (-Z column)."""
    return -np.asarray(transform)[:3, 2]


def intersect_floor(origin: np.ndarray, dirs: np.ndarray, floor_y: float,
                    max_range_m: float = 12.0):
    """Intersect rays with the horizontal plane y = floor_y.

    Returns (points (N,3), valid (N,)). A ray is valid only if it points
    downward, hits in front of the camera, and lands within max_range_m
    horizontally. Rays parallel to or above the floor are rejected, never
    extrapolated (same rule as WallSweepController).
    """
    dirs = np.atleast_2d(np.asarray(dirs, dtype=float))
    dy = dirs[:, 1]
    with np.errstate(divide="ignore", invalid="ignore"):
        t = (floor_y - origin[1]) / dy
    pts = origin[None, :] + t[:, None] * dirs
    horiz = np.hypot(pts[:, 0] - origin[0], pts[:, 2] - origin[2])
    valid = (dy < -1e-6) & (t > 0) & np.isfinite(t) & (horiz <= max_range_m)
    return pts, valid


@dataclass(frozen=True)
class GhostFrame:
    origin: np.ndarray
    right: np.ndarray
    up: np.ndarray
    forward: np.ndarray

    @staticmethod
    def from_floor_lock(floor_hit_world, lock_camera_transform) -> "GhostFrame":
        """Same construction as GhostMap's floor lock, in ARKit's right-handed world.

        Unity computes right = cross(up, forward) in its left-handed world. In a
        right-handed world the same *physical* right is cross(forward, up).
        """
        fwd = camera_forward(lock_camera_transform)
        fwd = fwd - UP * fwd.dot(UP)
        n = np.linalg.norm(fwd)
        if n < 1e-6:
            raise ValueError("camera was looking straight up/down at floor lock")
        fwd = fwd / n
        right = np.cross(fwd, UP)
        return GhostFrame(np.asarray(floor_hit_world, float), right, UP.copy(), fwd)

    def world_to_ghost(self, p_world) -> np.ndarray:
        d = np.asarray(p_world, float) - self.origin
        return np.stack([d @ self.right, d @ self.up, d @ self.forward], axis=-1)

    def ghost_to_world(self, p_ghost) -> np.ndarray:
        g = np.asarray(p_ghost, float)
        return self.origin + g[..., 0:1] * self.right + g[..., 1:2] * self.up + g[..., 2:3] * self.forward

    @property
    def floor_y(self) -> float:
        return float(self.origin[1])


def arkit_to_unity_point(p) -> np.ndarray:
    """AR Foundation's ARKit -> Unity conversion: negate Z (right- to left-handed)."""
    q = np.array(p, dtype=float, copy=True)
    q[..., 2] *= -1.0
    return q


def unity_ghost_from_lock(floor_hit_unity, cam_forward_unity):
    """Literal port of the Unity-side frame (FloorLockController): right = cross(up, forward)."""
    f = np.asarray(cam_forward_unity, float).copy()
    f[1] = 0.0
    f /= np.linalg.norm(f)
    r = np.cross(UP, f)
    return np.asarray(floor_hit_unity, float), r, UP.copy(), f


def pixels_to_floor_ghost(intr: Intrinsics, transform, frame: GhostFrame, u, v,
                          max_range_m: float = 12.0):
    """Full chain for a batch of sensor pixels. Returns (ghost_points (N,3), valid (N,))."""
    d_cam = pixel_to_camera_dir(intr, u, v).reshape(-1, 3)
    d_world = camera_to_world_dir(transform, d_cam)
    pts, valid = intersect_floor(camera_center(transform), d_world, frame.floor_y, max_range_m)
    return frame.world_to_ghost(pts), valid


def yaw_deg_in_ghost(transform, frame: GhostFrame) -> float:
    """Heading of the camera's forward, projected on the floor, in Ghost XZ (0 = +Z, 90 = +X)."""
    f = frame.world_to_ghost(frame.origin + camera_forward(transform))
    return float(np.degrees(np.arctan2(f[0], f[2])) % 360.0)


def pitch_deg(transform) -> float:
    """Camera pitch: negative when looking down."""
    f = camera_forward(transform)
    return float(np.degrees(np.arcsin(np.clip(f[1] / np.linalg.norm(f), -1, 1))))
