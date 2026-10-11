// Distances for the measure tool. Points are three.js world coordinates in
// metres; mirroring Z (see placement.js) changes no distance, so these equal
// the distances in Ghost space.

export function distance3d(a, b) {
  return Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]);
}

/** Distance in the floor plane (X and Z), ignoring height. */
export function distanceHorizontal(a, b) {
  return Math.hypot(b[0] - a[0], b[2] - a[2]);
}

/** Metres to two decimals, e.g. "1.23 m". */
export function formatMetres(metres) {
  if (!Number.isFinite(metres)) {
    return '–';
  }
  return `${(Math.round(metres * 100) / 100).toFixed(2)} m`;
}

/** Both distances as display text. */
export function describeMeasurement(a, b) {
  return {
    direct: formatMetres(distance3d(a, b)),
    horizontal: formatMetres(distanceHorizontal(a, b)),
  };
}
