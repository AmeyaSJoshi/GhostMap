"""RESEARCH SPIKE — NOT PRODUCTION.

Class ids of Apple's DETRResnet50SemanticSegmentationF16 (COCO-panoptic, 133
real labels in a 201-entry map). Read from the model's own metadata by
docs/research/perception-audit/spikes/labels.swift. Fixed before any scan exists.
"""

import numpy as np

WALL = (171, 175, 176, 177, 199)  # wall (brick / stone / tile / wood / other)
FLOOR = (118, 190, 200)           # floor (wood), floor (other), rug — all lie on the floor plane
CEILING = (186,)
DOOR = (112,)
WINDOW = (180, 181)               # window (blind), window (other)

GROUPS = {
    "wall": (WALL, (230, 159, 0)),
    "floor": (FLOOR, (86, 180, 233)),
    "ceiling": (CEILING, (240, 228, 66)),
    "door": (DOOR, (213, 94, 0)),
    "window": (WINDOW, (0, 158, 115)),
}
OTHER_COLOR = (120, 120, 120)


def mask(labels: np.ndarray, ids) -> np.ndarray:
    return np.isin(labels, ids)


def colorize(labels: np.ndarray) -> np.ndarray:
    """RGB image: structure classes colored, everything else gray."""
    out = np.empty(labels.shape + (3,), np.uint8)
    out[:] = OTHER_COLOR
    for ids, color in GROUPS.values():
        out[mask(labels, ids)] = color
    return out


def class_fractions(labels: np.ndarray) -> dict:
    n = labels.size
    return {name: float(mask(labels, ids).sum()) / n for name, (ids, _) in GROUPS.items()}
