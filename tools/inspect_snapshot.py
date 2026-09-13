#!/usr/bin/env python3
"""Inspect and validate a GhostMap SceneSnapshot JSON file.

Prints a readable summary of the structured room and re-implements the shared
validation rules so a snapshot can be checked without opening Unity. This is the
first tool to reach for when the viewer renders something wrong: it answers
"is the data bad, or is the renderer bad?" -- step 4 of the plan's debugging
checklist.

Usage:
    python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json
    python3 tools/inspect_snapshot.py --quiet fixtures/malformed-room-v1.json

Exit codes:
    0  snapshot is structurally valid
    1  snapshot is invalid, or the file could not be read
"""

import argparse
import json
import math
import os
import sys

SCHEMA_VERSION = 1

REQUIRED_CORNERS = 4
MIN_CORNER_SPACING_M = 0.50
MIN_NON_NEIGHBOR_SPACING_M = 0.20
CORNER_FLOOR_TOLERANCE_M = 0.05
MIN_WALL_LENGTH_M = 0.50
MAX_WALL_LENGTH_M = 20.0
MIN_ROOM_AREA_M2 = 2.0
MIN_ROOM_HEIGHT_M = 2.0
MAX_ROOM_HEIGHT_M = 4.0
MIN_INTERIOR_ANGLE_DEG = 35.0
MAX_INTERIOR_ANGLE_DEG = 145.0
MIN_OPENING_WIDTH_M = 0.30
MAX_OPENING_WIDTH_M = 4.0
MIN_OPENING_HEIGHT_M = 0.30
MIN_OBJECT_DIM_M = 0.05
MAX_OBJECT_DIM_M = 5.0

CLOSURE_EXCELLENT_M = 0.08
CLOSURE_ACCEPTABLE_M = 0.15

SUPPORTED_OBJECT_TYPES = {
    "bed", "desk", "chair", "couch", "table", "dresser", "tv", "generic",
}

EPS = 1e-4


def xz(corner):
    p = corner["position"]
    return (float(p["x"]), float(p["z"]))


def dist(a, b):
    return math.hypot(b[0] - a[0], b[1] - a[1])


def signed_polygon_area(points):
    total = 0.0
    n = len(points)
    for i in range(n):
        x1, z1 = points[i]
        x2, z2 = points[(i + 1) % n]
        total += (x1 * z2) - (x2 * z1)
    return total / 2.0


def polygon_area(points):
    return abs(signed_polygon_area(points))


def cross_xz(a, b):
    return (a[0] * b[1]) - (a[1] * b[0])


def interior_angle(points, i):
    """Interior angle in degrees, in [0, 360).

    Mirrors RoomGeometry.InteriorAngleDeg. The unsigned angle between the two
    edges cannot exceed 180 degrees, so a concave "dart" footprint would report
    its reflex corner as the 360-degree complement and pass the angle rule. The
    polygon's winding disambiguates the two.
    """
    n = len(points)
    cur = points[i]
    prv = points[(i - 1) % n]
    nxt = points[(i + 1) % n]

    a = (prv[0] - cur[0], prv[1] - cur[1])
    b = (nxt[0] - cur[0], nxt[1] - cur[1])

    ma, mb = math.hypot(*a), math.hypot(*b)
    if ma < EPS or mb < EPS:
        return 0.0

    cos = max(-1.0, min(1.0, (a[0] * b[0] + a[1] * b[1]) / (ma * mb)))
    unsigned = math.degrees(math.acos(cos))

    area = signed_polygon_area(points)
    if abs(area) < EPS:
        return unsigned

    turn = cross_xz(a, b)
    reflex = (turn > 0.0) if area > 0.0 else (turn < 0.0)

    return 360.0 - unsigned if reflex else unsigned


def segments_cross(p1, p2, q1, q2):
    """True when two segments properly cross or overlap while collinear.

    The collinear branch mirrors RoomGeometry.SegmentsIntersectXZ. Without it
    this tool would report a degenerate footprint as valid where the shared C#
    validator rejects it.
    """
    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    def on_segment(a, b, point):
        return (min(a[0], b[0]) - EPS <= point[0] <= max(a[0], b[0]) + EPS
                and min(a[1], b[1]) - EPS <= point[1] <= max(a[1], b[1]) + EPS)

    d1, d2 = cross(q1, q2, p1), cross(q1, q2, p2)
    d3, d4 = cross(p1, p2, q1), cross(p1, p2, q2)

    if ((d1 > 0 > d2) or (d1 < 0 < d2)) and ((d3 > 0 > d4) or (d3 < 0 < d4)):
        return True

    for d, (a, b, point) in (
        (d1, (q1, q2, p1)),
        (d2, (q1, q2, p2)),
        (d3, (p1, p2, q1)),
        (d4, (p1, p2, q2)),
    ):
        if abs(d) < EPS and on_segment(a, b, point):
            return True

    return False


def has_self_intersection(points):
    n = len(points)
    for i in range(n):
        for j in range(i + 1, n):
            if i == j or (i + 1) % n == j or (j + 1) % n == i:
                continue
            if segments_cross(points[i], points[(i + 1) % n],
                              points[j], points[(j + 1) % n]):
                return True
    return False


def build_walls(corners):
    walls = []
    n = len(corners)
    for i in range(n):
        a, b = corners[i], corners[(i + 1) % n]
        walls.append({
            "start": a["id"],
            "end": b["id"],
            "length": dist(xz(a), xz(b)),
        })
    return walls


def closure_label(value):
    if value <= CLOSURE_EXCELLENT_M:
        return "Excellent"
    if value <= CLOSURE_ACCEPTABLE_M:
        return "Acceptable"
    return "REJECTED"


def validate_partial_chain(corners):
    """Mirrors RoomValidator.ValidatePartialChain: spacing rules only."""
    problems = []
    for i in range(1, len(corners)):
        a, b = xz(corners[i - 1]), xz(corners[i])
        length = dist(a, b)
        if length < MIN_CORNER_SPACING_M:
            problems.append(
                f"corners {corners[i - 1]['id']} and {corners[i]['id']} are only "
                f"{length:.2f} m apart")
        if length > MAX_WALL_LENGTH_M:
            problems.append(
                f"wall {corners[i - 1]['id']}->{corners[i]['id']} is {length:.2f} m, "
                f"above {MAX_WALL_LENGTH_M:.1f} m")
    return problems


def validate(snapshot):
    """Returns a list of human-readable problems. Empty means valid."""
    problems = []

    if snapshot.get("schemaVersion") != SCHEMA_VERSION:
        problems.append(
            f"schemaVersion is {snapshot.get('schemaVersion')!r}, expected {SCHEMA_VERSION}")
        return problems

    room = snapshot.get("room")
    if not isinstance(room, dict):
        problems.append("snapshot has no room")
        return problems

    height = float(room.get("heightM", 0.0))
    if height != 0.0 and not (MIN_ROOM_HEIGHT_M <= height <= MAX_ROOM_HEIGHT_M):
        problems.append(
            f"room height {height:.2f} m is outside "
            f"{MIN_ROOM_HEIGHT_M:.1f}-{MAX_ROOM_HEIGHT_M:.1f} m")

    corners = room.get("corners") or []

    ids = [c.get("id") for c in corners]
    if len(set(ids)) != len(ids):
        problems.append("corner ids are not unique")

    for corner in corners:
        y = float(corner["position"]["y"])
        if abs(y) > CORNER_FLOOR_TOLERANCE_M:
            problems.append(f"corner {corner['id']} is {y:.3f} m off the floor plane")

    if not corners:
        return problems

    if len(corners) < REQUIRED_CORNERS:
        # Capture still in progress. RoomValidator.ValidateRoom treats a partial
        # corner chain as valid-so-far, so this tool must too, or it would report
        # every legitimate mid-scan snapshot as bad data.
        problems.extend(validate_partial_chain(corners))
        return problems

    if len(corners) > REQUIRED_CORNERS:
        problems.append(f"expected {REQUIRED_CORNERS} corners, found {len(corners)}")
        return problems

    points = [xz(c) for c in corners]

    if has_self_intersection(points):
        problems.append("room footprint is self-intersecting")

    area = polygon_area(points)
    if area < MIN_ROOM_AREA_M2:
        problems.append(f"room area {area:.2f} m2 is below {MIN_ROOM_AREA_M2:.1f} m2")

    walls = build_walls(corners)
    for wall in walls:
        if wall["length"] < MIN_WALL_LENGTH_M:
            problems.append(
                f"wall {wall['start']}->{wall['end']} is {wall['length']:.2f} m, "
                f"below {MIN_WALL_LENGTH_M:.2f} m")
        if wall["length"] > MAX_WALL_LENGTH_M:
            problems.append(
                f"wall {wall['start']}->{wall['end']} is {wall['length']:.2f} m, "
                f"above {MAX_WALL_LENGTH_M:.1f} m")

    for i, corner in enumerate(corners):
        angle = interior_angle(points, i)
        if not (MIN_INTERIOR_ANGLE_DEG <= angle <= MAX_INTERIOR_ANGLE_DEG):
            problems.append(
                f"interior angle at {corner['id']} is {angle:.1f} deg, outside "
                f"{MIN_INTERIOR_ANGLE_DEG:.0f}-{MAX_INTERIOR_ANGLE_DEG:.0f} deg")

    by_wall = {}
    for opening in room.get("openings") or []:
        key = (opening.get("wallStartCornerId"), opening.get("wallEndCornerId"))
        wall = next((w for w in walls if (w["start"], w["end"]) == key), None)

        if wall is None:
            problems.append(
                f"opening {opening.get('id')} references no wall {key[0]}->{key[1]}")
            continue

        width = float(opening.get("widthM", 0.0))
        offset = float(opening.get("offsetM", 0.0))
        sill = float(opening.get("sillHeightM", 0.0))
        oheight = float(opening.get("heightM", 0.0))

        if opening.get("type") not in ("door", "window"):
            problems.append(
                f"opening {opening.get('id')} has type {opening.get('type')!r}")
        if not (MIN_OPENING_WIDTH_M <= width <= MAX_OPENING_WIDTH_M):
            problems.append(
                f"opening {opening.get('id')} width {width:.2f} m is outside "
                f"{MIN_OPENING_WIDTH_M:.2f}-{MAX_OPENING_WIDTH_M:.1f} m")
        if oheight < MIN_OPENING_HEIGHT_M:
            problems.append(
                f"opening {opening.get('id')} height {oheight:.2f} m is too small")
        if sill < 0:
            problems.append(f"opening {opening.get('id')} has a negative sill")
        if offset < -EPS or offset + width > wall["length"] + EPS:
            problems.append(
                f"opening {opening.get('id')} spans {offset:.2f}-{offset + width:.2f} m "
                f"but the wall is {wall['length']:.2f} m")
        if height <= 0:
            # OpeningValidator.Validate refuses to judge an opening before the
            # room height exists, because its vertical extent is unbounded.
            problems.append(
                f"opening {opening.get('id')} cannot be validated: room height "
                f"has not been captured yet")
        elif sill + oheight > height + EPS:
            problems.append(
                f"opening {opening.get('id')} top {sill + oheight:.2f} m exceeds "
                f"room height {height:.2f} m")

        for other_id, o_start, o_end in by_wall.get(key, []):
            if offset < o_end - EPS and o_start < offset + width - EPS:
                problems.append(
                    f"opening {opening.get('id')} overlaps {other_id} on the same wall")

        by_wall.setdefault(key, []).append(
            (opening.get("id"), offset, offset + width))

    for obj in room.get("objects") or []:
        if obj.get("type") not in SUPPORTED_OBJECT_TYPES:
            problems.append(f"object {obj.get('id')} has type {obj.get('type')!r}")
        for field in ("widthM", "depthM", "heightM"):
            value = float(obj.get(field, 0.0))
            if not (MIN_OBJECT_DIM_M <= value <= MAX_OBJECT_DIM_M):
                problems.append(
                    f"object {obj.get('id')} {field} {value:.2f} m is outside "
                    f"{MIN_OBJECT_DIM_M:.2f}-{MAX_OBJECT_DIM_M:.1f} m")

    return problems


def summarize(snapshot):
    room = snapshot["room"]
    corners = room.get("corners") or []
    points = [xz(c) for c in corners]

    print(f"session   : {snapshot.get('sessionId')}")
    print(f"revision  : {snapshot.get('revision')}")
    print(f"phase     : {snapshot.get('scanPhase')}   finalized={snapshot.get('finalized')}")

    closure = float(snapshot.get("closureErrorM", 0.0))
    print(f"closure   : {closure * 100:.1f} cm  ({closure_label(closure)})")

    print(f"\nroom      : {room.get('name')!r}  height {room.get('heightM')} m")

    if points:
        print(f"area      : {polygon_area(points):.2f} m2")

    print(f"\ncorners ({len(corners)}):")
    for corner in corners:
        x, z = xz(corner)
        print(f"  {corner['id']:<6} x={x:7.3f}  z={z:7.3f}")

    if len(corners) >= 2:
        print(f"\nwalls (derived, {len(corners)}):")
        for wall in build_walls(corners):
            print(f"  {wall['start']}->{wall['end']:<4} {wall['length']:6.3f} m")

    openings = room.get("openings") or []
    print(f"\nopenings ({len(openings)}):")
    for opening in openings:
        print(f"  {opening.get('id'):<10} {opening.get('type'):<7} "
              f"wall {opening.get('wallStartCornerId')}->{opening.get('wallEndCornerId')}  "
              f"offset {opening.get('offsetM')} m  "
              f"{opening.get('widthM')}x{opening.get('heightM')} m  "
              f"sill {opening.get('sillHeightM')} m")

    objects = room.get("objects") or []
    print(f"\nobjects ({len(objects)}):")
    for obj in objects:
        c = obj.get("center", {})
        print(f"  {obj.get('id'):<10} {obj.get('type'):<8} "
              f"at ({c.get('x')}, {c.get('z')})  yaw {obj.get('yawDeg')} deg  "
              f"{obj.get('widthM')}x{obj.get('depthM')}x{obj.get('heightM')} m")


def main():
    parser = argparse.ArgumentParser(
        description="Inspect and validate a GhostMap SceneSnapshot JSON file.")
    parser.add_argument("path", help="Path to a snapshot or fixture JSON file.")
    parser.add_argument("--quiet", action="store_true",
                        help="Only report validation problems.")
    args = parser.parse_args()

    if not os.path.isfile(args.path):
        print(f"error: file not found: {args.path}", file=sys.stderr)
        return 1

    try:
        with open(args.path, "r", encoding="utf-8") as handle:
            snapshot = json.load(handle)
    except json.JSONDecodeError as exc:
        print(f"error: not valid JSON: {exc}", file=sys.stderr)
        return 1

    if not args.quiet:
        summarize(snapshot)

    problems = validate(snapshot)

    print()
    if problems:
        print(f"INVALID - {len(problems)} problem(s):")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    print("VALID - snapshot satisfies every shared validation rule.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
