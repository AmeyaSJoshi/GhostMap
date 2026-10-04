"""RESEARCH SPIKE — NOT PRODUCTION. Tests for E1's deterministic math (no Core ML)."""

import sys
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from e1 import boundary, camera, evaluate as ev, orient, segmap, walls  # noqa: E402

K = camera.Intrinsics(1450.0, 1450.0, 960.0, 720.0)
W, H = 1920, 1440


def rot_x(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rot_y(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rot_z(a):
    c, s = np.cos(a), np.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def pose(pos, yaw_deg=0.0, pitch_deg=0.0, roll_deg=0.0):
    """ARKit camera transform: yaw about +Y, pitch about camera X, roll about the optical axis."""
    t = np.eye(4)
    t[:3, :3] = rot_y(np.radians(yaw_deg)) @ rot_x(np.radians(pitch_deg)) @ rot_z(np.radians(roll_deg))
    t[:3, 3] = pos
    return t


class CameraTests(unittest.TestCase):
    def test_principal_point_looks_down_minus_z(self):
        d = camera.pixel_to_camera_dir(K, K.cx, K.cy)
        np.testing.assert_allclose(d, [0, 0, -1])

    def test_pixel_ray_roundtrip(self):
        u, v = np.array([10.0, 960, 1900]), np.array([5.0, 720, 1430])
        d = camera.pixel_to_camera_dir(K, u, v) * 3.7
        np.testing.assert_allclose(camera.camera_point_to_pixel(K, d), np.stack([u, v], -1), atol=1e-9)

    def test_image_top_is_camera_up(self):
        self.assertGreater(camera.pixel_to_camera_dir(K, K.cx, 0)[1], 0)
        self.assertGreater(camera.pixel_to_camera_dir(K, W, K.cy)[0], 0)

    def test_floor_intersection_known_point(self):
        t = pose([0, 1.5, 0], pitch_deg=-45)
        pts, ok = camera.intersect_floor(camera.camera_center(t), camera.camera_forward(t)[None], 0.0)
        self.assertTrue(ok[0])
        np.testing.assert_allclose(pts[0], [0, 0, -1.5], atol=1e-9)

    def test_floor_rejects_upward_and_far_rays(self):
        o = np.array([0, 1.5, 0])
        _, ok = camera.intersect_floor(o, np.array([[0, 0.2, -1], [0, -0.01, -1], [0, -1, 0]]), 0.0)
        self.assertEqual(ok.tolist(), [False, False, True])

    def test_ghost_frame_axes(self):
        g = camera.GhostFrame.from_floor_lock([1, 0.2, 2], pose([1, 1.6, 2], yaw_deg=90, pitch_deg=-30))
        # yaw +90 about +Y turns -Z into -X, so forward is -X and right is -Z (physical right).
        np.testing.assert_allclose(g.forward, [-1, 0, 0], atol=1e-9)
        np.testing.assert_allclose(g.right, [0, 0, -1], atol=1e-9)
        p = np.array([0.3, 0.2, 1.1])
        np.testing.assert_allclose(g.ghost_to_world(g.world_to_ghost(p)), p, atol=1e-9)
        self.assertAlmostEqual(g.world_to_ghost([1, 0.2, 2])[1], 0.0)

    def test_ghost_matches_unity_left_handed_path(self):
        rng = np.random.default_rng(1)
        for _ in range(20):
            t = pose(rng.normal(size=3), rng.uniform(0, 360), rng.uniform(-60, 20), rng.uniform(-180, 180))
            hit = rng.normal(size=3)
            g = camera.GhostFrame.from_floor_lock(hit, t)
            o_u, r_u, up_u, f_u = camera.unity_ghost_from_lock(camera.arkit_to_unity_point(hit),
                                                               camera.arkit_to_unity_point(camera.camera_forward(t)))
            p = rng.normal(size=3)
            pu = camera.arkit_to_unity_point(p) - o_u
            np.testing.assert_allclose(g.world_to_ghost(p), [pu @ r_u, pu @ up_u, pu @ f_u], atol=1e-9)

    def test_yaw_increases_turning_right(self):
        g = camera.GhostFrame.from_floor_lock([0, 0, 0], pose([0, 1.5, 0]))
        self.assertAlmostEqual(camera.yaw_deg_in_ghost(pose([0, 1.5, 0], yaw_deg=-30), g), 30.0, places=6)


class OrientTests(unittest.TestCase):
    def test_roundtrip_all_rotations(self):
        u, v = np.array([0.5, 100.25, 1919.5]), np.array([0.5, 700.0, 1439.5])
        for k in range(4):
            x, y = orient.sensor_to_upright(u, v, k, W, H)
            u2, v2 = orient.upright_to_sensor(x, y, k, W, H)
            np.testing.assert_allclose([u2, v2], [u, v])

    def test_matches_pil_rotation(self):
        from e1.pipeline import _TRANSPOSE
        a = np.zeros((6, 8), np.uint8)
        a[1, 6] = 255  # pixel (u=6, v=1)
        for k in (1, 2, 3):
            r = np.asarray(Image.fromarray(a).transpose(_TRANSPOSE[k]))
            yy, xx = np.argwhere(r == 255)[0]
            x, y = orient.sensor_to_upright(6.5, 1.5, k, 8, 6)
            self.assertEqual((int(x), int(y)), (xx, yy), f"k={k}")

    def test_upright_rotation_puts_up_at_top(self):
        for roll in (0, 90, 180, -90):
            t = pose([0, 1.5, 0], yaw_deg=20, pitch_deg=-10, roll_deg=roll)
            k = orient.choose_upright_rotation(t)
            above = camera.camera_center(t) + camera.camera_forward(t) * 3 + [0, 1, 0]
            below = camera.camera_center(t) + camera.camera_forward(t) * 3 - [0, 1, 0]
            ys = []
            for p in (above, below):
                pc = t[:3, :3].T @ (p - camera.camera_center(t))
                u, v = camera.camera_point_to_pixel(K, pc)
                ys.append(orient.sensor_to_upright(u, v, k, W, H)[1])
            self.assertLess(ys[0], ys[1], f"roll={roll}")

    def test_tiles_cover_portrait_image(self):
        tiles = orient.square_tiles(1440, 1920)
        self.assertEqual([(t.oy, t.size) for t in tiles], [(0.0, 1440), (480.0, 1440)])
        lab = [np.full((448, 448), 1), np.full((448, 448), 2)]
        grid, cell = orient.stitch_labels(lab, tiles, 1440, 1920)
        self.assertEqual(grid.shape, (597, 448))
        self.assertTrue((grid[:100] == 1).all() and (grid[-100:] == 2).all())


class WallTests(unittest.TestCase):
    def test_tls_z_parallel_and_residual(self):
        pts = np.array([[1.0 + off, i * 0.1] for i in range(10) for off in (-0.01, 0.01)])
        line, why = walls.tls_fit(pts)
        self.assertEqual(why, "None")
        self.assertAlmostEqual(abs(line.direction[1]), 1.0, places=6)
        self.assertAlmostEqual(line.rms, 0.01, places=6)

    def test_tls_rejects_short_span_and_few(self):
        self.assertEqual(walls.tls_fit(np.zeros((3, 2)))[1], "TooFewSamples")
        self.assertEqual(walls.tls_fit(np.c_[np.linspace(0, 0.3, 10), np.zeros(10)])[1], "SpanTooShort")

    def test_corners_from_rectangle_and_parallel_rejected(self):
        mk = lambda p, d: walls.Line(np.array(p, float), np.array(d, float) / np.linalg.norm(d), 0, 1, 10)  # noqa: E731
        rect = [mk([2, 0], [1, 0]), mk([4, 1.5], [0, 1]), mk([2, 3], [-1, 0]), mk([0, 1.5], [0, -1])]
        c, err = walls.derive_corners(rect)
        np.testing.assert_allclose(c, [[0, 0], [4, 0], [4, 3], [0, 3]], atol=1e-9)
        self.assertIsNone(walls.intersect(mk([0, 0], [1, 0]), mk([0, 1], [1, 0.05])))

    def test_ransac_finds_four_walls_with_clutter(self):
        rng = np.random.default_rng(0)
        sides = [np.c_[np.linspace(0, 4, 200), np.zeros(200)], np.c_[np.full(150, 4), np.linspace(0, 3, 150)],
                 np.c_[np.linspace(0, 4, 200), np.full(200, 3)], np.c_[np.zeros(150), np.linspace(0, 3, 150)]]
        pts = np.vstack(sides) + rng.normal(0, 0.01, (700, 2))
        pts = np.vstack([pts, rng.uniform([0.5, 0.5], [3.5, 2.5], (40, 2))])
        lines = walls.dominant(walls.merge_collinear(walls.ransac_lines(pts), pts))
        self.assertEqual(len(lines), 4)
        four, c, err = walls.four_wall_polygon(lines, np.array([2.0, 1.5]))
        self.assertEqual(err, "")
        self.assertTrue(walls.is_convex_simple(c))
        np.testing.assert_allclose(sorted(walls.wall_lengths(c)), [3, 3, 4, 4], atol=0.03)


class EvaluateTests(unittest.TestCase):
    def test_similarity_recovers_scale_and_forbids_reflection(self):
        gt = np.array([[0, 0], [5, 0], [5, 4], [0, 4]], float)
        r = np.array([[np.cos(1), -np.sin(1)], [np.sin(1), np.cos(1)]])
        pred = (2.02 * (r @ gt.T)).T + [3, -1]
        s, _, _, al = ev.similarity_align(pred, gt)
        self.assertAlmostEqual(s, 1 / 2.02, places=9)
        np.testing.assert_allclose(al, gt, atol=1e-9)
        mirrored = gt * [-1, 1]
        _, rr, _, _ = ev.similarity_align(mirrored, gt)
        self.assertGreater(np.linalg.det(rr), 0)

    def test_gt_quad_and_iou(self):
        q, d2 = ev.gt_quad_from_measurements(5, 4, 5, 4, np.hypot(5, 4))
        np.testing.assert_allclose(walls.wall_lengths(q), [5, 4, 5, 4], atol=1e-9)
        np.testing.assert_allclose(walls.interior_angles_deg(q), [90] * 4, atol=1e-6)
        self.assertAlmostEqual(d2, np.hypot(5, 4))
        self.assertAlmostEqual(ev.iou_convex(q, q), 1.0)
        sq = np.array([[0, 0], [4, 0], [4, 3], [0, 3]], float)
        self.assertAlmostEqual(ev.iou_convex(sq, sq + [2, 0]), 1 / 3, places=9)
        self.assertAlmostEqual(ev.iou_convex(sq, sq + [5, 0]), 0.0)

    def test_scaled_prediction_scores_perfect_normalized_but_not_raw(self):
        gt, _ = ev.gt_quad_from_measurements(5, 4, 5, 4, np.hypot(5, 4))
        th = 0.7
        r = np.array([[np.cos(th), -np.sin(th)], [np.sin(th), np.cos(th)]])
        pred = np.roll((2.0 * (r @ gt.T)).T + [1, 1], 1, axis=0)
        res = ev.evaluate(pred, gt)
        self.assertAlmostEqual(res["normalized"]["scale"], 0.5, places=9)
        self.assertLess(max(abs(x) for x in res["normalized"]["normalized_length_pct_err"]), 1e-6)
        self.assertAlmostEqual(res["normalized"]["iou"], 1.0, places=6)
        self.assertAlmostEqual(res["raw"]["pct_err"][0], 100.0, places=6)


def synthetic_labels(t, room=(-2.0, 2.0, -1.5, 1.8), ceiling=2.5, k=None, bed=None):
    """Label each stitched-grid cell by casting its ray into an empty box room (floor y=0)."""
    k = orient.choose_upright_rotation(t) if k is None else k
    uw, uh = orient.upright_size(W, H, k)
    gw, gh, cell = orient.stitched_grid_shape(uw, uh)
    gx, gy = np.meshgrid(np.arange(gw) + 0.5, np.arange(gh) + 0.5)
    u, v = orient.grid_to_sensor(gx, gy, cell, k, W, H)
    d = camera.camera_to_world_dir(t, camera.pixel_to_camera_dir(K, u, v).reshape(-1, 3))
    o = camera.camera_center(t)
    x0, x1, z0, z1 = room
    with np.errstate(divide="ignore", invalid="ignore"):
        tx = np.where(d[:, 0] > 0, (x1 - o[0]) / d[:, 0], (x0 - o[0]) / d[:, 0])
        tz = np.where(d[:, 2] > 0, (z1 - o[2]) / d[:, 2], (z0 - o[2]) / d[:, 2])
        tf = np.where(d[:, 1] < 0, -o[1] / d[:, 1], np.inf)
        tc = np.where(d[:, 1] > 0, (ceiling - o[1]) / d[:, 1], np.inf)
    tw = np.minimum(tx, tz)
    lab = np.where(tf < tw, segmap.FLOOR[1], np.where(tc < tw, segmap.CEILING[0], segmap.WALL[-1]))
    if bed is not None:  # an occluder standing against a wall: hide wall/floor where rays hit its box
        bx0, bx1, bz0, bz1, bh = bed
        hit = o[None] + tf[:, None] * d
        blocked = (hit[:, 0] > bx0) & (hit[:, 0] < bx1) & (hit[:, 2] > bz0) & (hit[:, 2] < bz1)
        wallhit = o[None] + tw[:, None] * d
        low = (wallhit[:, 1] < bh) & (wallhit[:, 0] > bx0) & (wallhit[:, 0] < bx1) & (wallhit[:, 2] > bz0 - 0.01) & (wallhit[:, 2] < bz1 + 0.01)
        lab = np.where(blocked | low, 65, lab)  # 65 = bed
    return lab.reshape(gh, gw).astype(np.int32), cell, k


class SyntheticEndToEnd(unittest.TestCase):
    def run_room(self, bed=None):
        room = (-2.0, 2.0, -1.5, 1.8)
        frame = camera.GhostFrame.from_floor_lock([0, 0, 0], pose([0.2, 1.4, 0.1]))
        pts = []
        for yaw in range(0, 360, 12):
            t = pose([0.2 + 0.1 * np.sin(np.radians(yaw)), 1.4, 0.1], yaw_deg=yaw, pitch_deg=-20, roll_deg=90)
            lab, cell, k = synthetic_labels(t, room, bed=bed)
            self.assertNotEqual(k, 0)  # portrait: the image really had to be rotated
            c = boundary.extract(lab)
            u, v = orient.grid_to_sensor(c.gx, c.gy, cell, k, W, H)
            d = camera.camera_to_world_dir(t, camera.pixel_to_camera_dir(K, u, v).reshape(-1, 3))
            p, ok = camera.intersect_floor(camera.camera_center(t), d, 0.0)
            pts.append(frame.world_to_ghost(p[ok])[:, [0, 2]])
        pts = np.vstack(pts)
        lines = walls.dominant(walls.merge_collinear(walls.ransac_lines(pts), pts))
        four, corners, err = walls.four_wall_polygon(lines, np.array([0.2, 0.1]))
        self.assertEqual(err, "")
        # Ghost axes == world axes here (forward -Z at lock => Ghost z = -world z, x = world x).
        true = np.array([[-2, 1.5], [2, 1.5], [2, -1.8], [-2, -1.8]])
        for c in true:
            self.assertLess(np.min(np.linalg.norm(corners - c, axis=1)), 0.05)
        return lines

    def test_empty_room_recovers_corners(self):
        self.assertEqual(len(self.run_room()), 4)

    def test_bed_against_wall_still_recovers_corners(self):
        self.run_room(bed=(-1.0, 0.6, 0.0, 1.8, 0.6))  # covers 40% of the +z wall's junction


class BoundaryTests(unittest.TestCase):
    def test_skips_floor_under_furniture(self):
        g = np.full((120, 100), segmap.WALL[-1], np.int32)
        g[60:] = segmap.FLOOR[0]
        g[40:80, 30:60] = 65  # bed in front of the wall, standing on the floor
        c = boundary.extract(g)
        cols = set(np.floor(c.gx).astype(int))
        self.assertTrue(cols.isdisjoint(range(31, 59)))
        self.assertTrue(np.allclose(c.gy, 60.0))
        self.assertGreater(len(c.rejected_gx), 0)


if __name__ == "__main__":
    unittest.main()
