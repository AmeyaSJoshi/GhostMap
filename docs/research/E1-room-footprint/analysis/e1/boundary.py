"""RESEARCH SPIKE — NOT PRODUCTION.

Wall/floor contact extraction from an upright label grid.

A geometry observation is accepted only where WALL sits directly above FLOOR in
the same image column — i.e. where the wall actually meets the floor. A floor
pixel under a bed, desk or chair is NOT a wall contact, and is skipped. All
parameters are fixed here, before any real scan is seen.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np
from scipy import ndimage

from . import segmap

OPEN_SIZE = 3            # morphological opening kernel (cells)
MIN_COMPONENT_FRAC = 0.002  # drop wall/floor components smaller than 0.2% of the image
MIN_RUN = 4              # need >= 4 wall cells above and >= 4 floor cells below the contact
MAX_GAP = 2              # tolerate up to 2 unlabeled cells (skirting boards) between them
COLUMN_STEP = 1          # use every column


@dataclass
class Contacts:
    gx: np.ndarray            # continuous grid x of each contact (cell center)
    gy: np.ndarray            # continuous grid y of the contact (between wall and floor)
    columns_with_floor: int   # columns where any floor was visible
    columns_total: int
    rejected_gx: np.ndarray = field(default_factory=lambda: np.zeros(0))
    rejected_gy: np.ndarray = field(default_factory=lambda: np.zeros(0))


def clean(binary: np.ndarray) -> np.ndarray:
    st = np.ones((OPEN_SIZE, OPEN_SIZE), bool)
    m = ndimage.binary_opening(binary, structure=st)
    lab, n = ndimage.label(m)
    if n == 0:
        return m
    sizes = ndimage.sum(m, lab, index=np.arange(1, n + 1))
    keep = np.zeros(n + 1, bool)
    keep[1:] = sizes >= MIN_COMPONENT_FRAC * binary.size
    return keep[lab]


def extract(labels: np.ndarray) -> Contacts:
    wall = clean(segmap.mask(labels, segmap.WALL))
    floor = clean(segmap.mask(labels, segmap.FLOOR))
    h, w = labels.shape
    gx, gy, rx, ry = [], [], [], []
    floor_cols = 0
    for x in range(0, w, COLUMN_STEP):
        f = floor[:, x]
        if not f.any():
            continue
        floor_cols += 1
        wl = wall[:, x]
        # Scan upward from the bottom for the lowest floor->wall transition.
        y = h - 1
        while y >= MIN_RUN:
            if f[y] and not f[y - 1]:
                top_floor = y  # first floor row of this floor run (from above)
                g = 0
                while g < MAX_GAP and top_floor - 1 - g >= 0 and not wl[top_floor - 1 - g] and not f[top_floor - 1 - g]:
                    g += 1
                wall_bottom = top_floor - 1 - g
                floor_ok = top_floor + MIN_RUN <= h and f[top_floor:top_floor + MIN_RUN].all()
                wall_ok = wall_bottom - MIN_RUN + 1 >= 0 and wl[wall_bottom - MIN_RUN + 1:wall_bottom + 1].all()
                contact_y = (wall_bottom + 1 + top_floor) / 2.0  # boundary between the runs
                if floor_ok and wall_ok:
                    gx.append(x + 0.5)
                    gy.append(contact_y)
                    break
                rx.append(x + 0.5)
                ry.append(contact_y)
            y -= 1
    return Contacts(np.array(gx), np.array(gy), floor_cols, w // COLUMN_STEP, np.array(rx), np.array(ry))
