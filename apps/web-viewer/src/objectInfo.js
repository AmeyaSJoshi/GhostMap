// Display text for the info panel, object list and room summary. Every number
// shown comes straight from scene.json; nothing is recomputed from meshes and
// no room rule is re-checked here (AGENTS.md rule 3).

import { formatMetres } from './measure.js';

const TYPE_NAMES = {
  bed: 'Bed',
  desk: 'Desk',
  chair: 'Chair',
  couch: 'Couch',
  table: 'Table',
  dresser: 'Dresser',
  tv: 'TV',
  generic: 'Object',
};

/** Human name for a scene object type; unknown types are shown as written. */
export function typeName(type) {
  if (typeof type !== 'string' || type === '') {
    return 'Object';
  }
  return TYPE_NAMES[type] ?? type;
}

/** The detector's label (task R5) when present, else the type name. */
export function objectTitle(object) {
  const label = typeof object.label === 'string' ? object.label.trim() : '';
  return label !== '' ? capitalise(label) : typeName(object.type);
}

/** Rows for the info panel: [label, value] pairs. */
export function objectDetails(object) {
  const rows = [['Type', typeName(object.type)]];

  const label = typeof object.label === 'string' ? object.label.trim() : '';
  if (label !== '') {
    rows.push(['Detected as', label]);
  }

  rows.push(['Width', size(object.widthM)]);
  rows.push(['Depth', size(object.depthM)]);
  rows.push(['Height', size(object.heightM)]);
  rows.push(['Id', String(object.id)]);
  return rows;
}

/** One line for the object list, e.g. "Desk · 1.40 × 0.70 × 0.75 m". */
export function objectSummary(object) {
  const dims = [object.widthM, object.depthM, object.heightM]
    .map((value) => (Number.isFinite(value) ? value.toFixed(2) : '–'))
    .join(' × ');
  return `${objectTitle(object)} · ${dims} m`;
}

/** Rows for the room summary panel. */
export function roomDetails(scene) {
  const room = scene.room;
  const openings = Array.isArray(room.openings) ? room.openings : [];
  const objects = Array.isArray(room.objects) ? room.objects : [];
  const doors = openings.filter((opening) => opening.type === 'door').length;
  const windows = openings.filter((opening) => opening.type === 'window').length;

  const rows = [];
  rows.push(['Room', typeof room.name === 'string' && room.name !== '' ? room.name : 'Untitled room']);

  const extent = footprintExtent(room.corners);
  if (extent !== null) {
    rows.push(['Floor', `${extent.x.toFixed(2)} × ${extent.z.toFixed(2)} m (outer extent)`]);
  }

  rows.push(['Ceiling height', formatMetres(room.heightM)]);
  rows.push(['Doors', String(doors)]);
  rows.push(['Windows', String(windows)]);
  rows.push(['Objects', String(objects.length)]);
  return rows;
}

/**
 * Width and length of the axis-aligned box around the floor corners. This is
 * a display convenience, not a room measurement rule: for a rotated or
 * non-rectangular footprint it is larger than any wall.
 */
export function footprintExtent(corners) {
  if (!Array.isArray(corners) || corners.length === 0) {
    return null;
  }

  const xs = corners.map((corner) => corner?.position?.x).filter(Number.isFinite);
  const zs = corners.map((corner) => corner?.position?.z).filter(Number.isFinite);
  if (xs.length !== corners.length || zs.length !== corners.length) {
    return null;
  }

  return { x: Math.max(...xs) - Math.min(...xs), z: Math.max(...zs) - Math.min(...zs) };
}

function size(metres) {
  if (!Number.isFinite(metres)) {
    return '–';
  }
  return `${formatMetres(metres)} (${Math.round(metres * 100)} cm)`;
}

function capitalise(text) {
  return text.charAt(0).toUpperCase() + text.slice(1);
}
