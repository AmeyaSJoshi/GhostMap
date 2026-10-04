"""RESEARCH SPIKE — NOT PRODUCTION. Diagnostic figures. All top-down plots use equal X/Z scaling."""

from __future__ import annotations

from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from PIL import Image, ImageDraw  # noqa: E402

from . import segmap  # noqa: E402
from .camera import camera_center  # noqa: E402

LEGEND = [(name, color) for name, (_, color) in segmap.GROUPS.items()] + [("other", segmap.OTHER_COLOR)]


def _grid_image(r, upright_rgb: Image.Image) -> Image.Image:
    gh, gw = r.labels.shape
    return upright_rgb.resize((gw * 2, gh * 2))


def seg_overlay(r, upright_rgb: Image.Image, out: Path):
    base = _grid_image(r, upright_rgb)
    col = Image.fromarray(segmap.colorize(r.labels)).resize(base.size, Image.Resampling.NEAREST)
    im = Image.blend(base, col, 0.5)
    d = ImageDraw.Draw(im)
    y = 6
    for name, color in LEGEND:
        d.rectangle([6, y, 20, y + 12], fill=tuple(color))
        d.text((26, y), name, fill=(255, 255, 255))
        y += 16
    d.text((6, im.height - 18), f"frame {r.frame.i} yaw {r.frame.yaw:.0f} pitch {r.frame.pitch:+.0f} rot k={r.k}", fill=(255, 255, 255))
    im.save(out, quality=88)


def boundary_overlay(r, upright_rgb: Image.Image, out: Path):
    im = _grid_image(r, upright_rgb)
    d = ImageDraw.Draw(im)
    c = r.contacts
    for x, y in zip(c.rejected_gx * 2, c.rejected_gy * 2):
        d.line([x - 2, y - 2, x + 2, y + 2], fill=(255, 40, 40))
        d.line([x - 2, y + 2, x + 2, y - 2], fill=(255, 40, 40))
    ok = r.valid if len(r.valid) else np.zeros(len(c.gx), bool)
    for x, y, v in zip(c.gx * 2, c.gy * 2, ok):
        d.ellipse([x - 1.5, y - 1.5, x + 1.5, y + 1.5], fill=(40, 255, 80) if v else (255, 200, 0))
    d.text((6, 6), f"green = used wall/floor contact ({int(ok.sum())})", fill=(40, 255, 80))
    d.text((6, 20), "yellow = contact whose ray missed the floor", fill=(255, 200, 0))
    d.text((6, 34), "red x = floor top edge NOT under a wall (furniture etc.), skipped", fill=(255, 80, 80))
    im.save(out, quality=88)


def contact_sheet(images: list[Path], out: Path, cols: int = 6, width: int = 300):
    if not images:
        return
    ims = [Image.open(p) for p in images]
    h = int(width * ims[0].height / ims[0].width)
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * width, rows * h), (0, 0, 0))
    for n, im in enumerate(ims):
        sheet.paste(im.resize((width, h)), ((n % cols) * width, (n // cols) * h))
    sheet.save(out, quality=85)


def _cameras(ax, results, frame):
    cams = np.array([frame.world_to_ghost(camera_center(r.frame.T)) for r in results])
    if len(cams):
        ax.scatter(cams[:, 0], cams[:, 2], c="k", s=8, marker="^", label="camera positions", zorder=5)
    ax.annotate("", xy=(0, 0.5), xytext=(0, 0), arrowprops=dict(arrowstyle="->", color="k"))
    ax.text(0.05, 0.5, "+Z (forward at floor lock)", fontsize=7)
    ax.scatter([0], [0], c="k", marker="x", s=30, label="floor-lock origin")


def topdown_raw(results, frame, out: Path, title: str):
    fig, ax = plt.subplots(figsize=(8, 8))
    cmap = plt.get_cmap("hsv")
    for r in results:
        g = r.ghost[r.valid] if len(r.valid) else np.zeros((0, 3))
        if len(g):
            ax.scatter(g[:, 0], g[:, 2], s=1.5, color=cmap(r.frame.yaw / 360.0), alpha=0.6)
    _cameras(ax, results, frame)
    sm = plt.cm.ScalarMappable(cmap=cmap, norm=plt.Normalize(0, 360))
    fig.colorbar(sm, ax=ax, shrink=0.6, label="camera heading of source frame (deg)")
    ax.set_aspect("equal", adjustable="datalim")
    ax.grid(alpha=0.3)
    ax.set_xlabel("Ghost X (m)")
    ax.set_ylabel("Ghost Z (m)")
    ax.set_title(title)
    ax.legend(loc="lower right", fontsize=7)
    fig.savefig(out, dpi=140, bbox_inches="tight")
    plt.close(fig)


def topdown_fit(pts, lines, chosen, corners, results, frame, out: Path, title: str):
    fig, ax = plt.subplots(figsize=(8, 8))
    ax.scatter(pts[:, 0], pts[:, 1], s=1, c="0.75", label="all projected contacts")
    colors = plt.get_cmap("tab10")
    for n, l in enumerate(lines):
        inl = pts[l.inliers]
        ax.scatter(inl[:, 0], inl[:, 1], s=2, color=colors(n % 10))
        ends = l.point + np.outer([-6, 6], l.direction)
        ls = "-" if any(l is c for c in (chosen or [])) else ":"
        ax.plot(ends[:, 0], ends[:, 1], ls, color=colors(n % 10), lw=1,
                label=f"line {n}: {l.count} pts, rms {l.rms * 100:.1f} cm" + (" (used)" if ls == "-" else ""))
    if corners is not None:
        poly = np.vstack([corners, corners[:1]])
        ax.plot(poly[:, 0], poly[:, 1], "k-", lw=2, label="predicted footprint")
        ax.scatter(corners[:, 0], corners[:, 1], c="r", s=40, zorder=6, label="corners")
        for i, c in enumerate(corners):
            ax.text(c[0], c[1], f" c{i}", color="r")
    _cameras(ax, results, frame)
    ax.set_aspect("equal", adjustable="datalim")
    lim = np.vstack([pts[:, :2], corners]) if corners is not None else pts[:, :2]
    if len(lim) == 0:
        lim = np.array([[-3.0, -3.0], [3.0, 3.0]])
        ax.text(0, 0, "NO wall/floor contacts", ha="center", color="r")
    pad = 0.5
    ax.set_xlim(lim[:, 0].min() - pad, lim[:, 0].max() + pad)
    ax.set_ylim(lim[:, 1].min() - pad, lim[:, 1].max() + pad)
    ax.grid(alpha=0.3)
    ax.set_xlabel("Ghost X (m)")
    ax.set_ylabel("Ghost Z (m)")
    ax.set_title(title)
    ax.legend(loc="upper left", fontsize=6)
    fig.savefig(out, dpi=140, bbox_inches="tight")
    plt.close(fig)


def aligned(gt, pred_aligned, raw_pred, out: Path, title: str):
    fig, ax = plt.subplots(figsize=(7, 7))
    for poly, style, label in [(gt, "k-", "ground truth"), (pred_aligned, "r--", "prediction after ONE uniform scale + rotation/translation")]:
        p = np.vstack([poly, poly[:1]])
        ax.plot(p[:, 0], p[:, 1], style, lw=2, label=label)
    for i, (g, q) in enumerate(zip(gt, pred_aligned)):
        ax.plot([g[0], q[0]], [g[1], q[1]], "b:", lw=1)
        ax.text(g[0], g[1], f" {'DA AB BC CD'.split()[i]}", fontsize=8)
    ax.set_aspect("equal", adjustable="datalim")
    ax.grid(alpha=0.3)
    ax.legend(fontsize=7)
    ax.set_title(title)
    fig.savefig(out, dpi=140, bbox_inches="tight")
    plt.close(fig)
