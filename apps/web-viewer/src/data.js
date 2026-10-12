// Reads the data blob the scanner embeds in room.html and checks its shape.
//
// The blob is the seam between this viewer (task R8) and the phone's export
// bundle (task R6); its format is docs/contracts/export-bundle-v1.md, section
// "room.html data". This file checks only that the blob is well-formed enough
// to display. It never re-checks room rules (wall lengths, heights, overlaps):
// those belong to the shared C# package, which already ran them on the phone
// before export (AGENTS.md rule 3).

export const DATA_FORMAT = 'ghostmap-room-html';
export const SUPPORTED_FORMAT_VERSION = 1;

// The template's placeholder is deliberately not spelled out in this file: the
// build inlines this code into the template, and the template must contain the
// placeholder exactly once. Its prefix is enough to recognise it.
const PLACEHOLDER_PREFIX = '__GHOSTMAP_';

export const TEMPLATE_MESSAGE =
  'This is the GhostMap template. Open a room.html from an export.';

const GLB_MAGIC = [0x67, 0x6c, 0x54, 0x46]; // "glTF"
const JPEG_MAGIC = [0xff, 0xd8, 0xff];

/**
 * Parses the text inside <script id="ghostmap-data">.
 *
 * @param {string} text
 * @returns {{ ok: true, scene: object, roomGlb: Uint8Array,
 *             objectGlbs: Map<string, Uint8Array>, objectPhotos: Map<string, Uint8Array>,
 *             warnings: string[] }
 *          | { ok: false, error: string, isTemplate?: boolean }}
 */
export function parseRoomData(text) {
  const trimmed = typeof text === 'string' ? text.trim() : '';

  if (trimmed === '') {
    return fail('This page has no room data in it. Open a room.html from a GhostMap export.');
  }

  if (trimmed.startsWith(PLACEHOLDER_PREFIX)) {
    return { ok: false, error: TEMPLATE_MESSAGE, isTemplate: true };
  }

  let blob;
  try {
    blob = JSON.parse(trimmed);
  } catch {
    return fail('The room data in this page is damaged and cannot be read.');
  }

  if (!isObject(blob) || blob.format !== DATA_FORMAT) {
    return fail('This page does not contain GhostMap room data.');
  }

  if (blob.formatVersion !== SUPPORTED_FORMAT_VERSION) {
    return fail(
      `This room was exported in format version ${String(blob.formatVersion)}, ` +
        `but this viewer reads version ${SUPPORTED_FORMAT_VERSION}. Export it again with a matching app.`,
    );
  }

  const scene = blob.scene;
  if (!isObject(scene) || !isObject(scene.room)) {
    return fail('The room data has no scene in it.');
  }

  const room = scene.room;
  if (!Array.isArray(room.corners) || room.corners.length < 3) {
    return fail('The scene has no floor outline.');
  }

  const roomGlb = decodeGlb(blob.roomGlb);
  if (!roomGlb.ok) {
    return fail(`The room model is ${roomGlb.reason}.`);
  }

  const objects = Array.isArray(room.objects) ? room.objects : [];
  const encoded = isObject(blob.objectGlbs) ? blob.objectGlbs : {};
  const objectGlbs = new Map();
  const warnings = [];

  for (const object of objects) {
    const id = isObject(object) ? object.id : undefined;
    if (typeof id !== 'string' || id === '') {
      return fail('An object in the scene has no id.');
    }

    if (!Object.prototype.hasOwnProperty.call(encoded, id)) {
      warnings.push(`No 3D model was exported for ${describe(object)}; it is listed but not drawn.`);
      continue;
    }

    const glb = decodeGlb(encoded[id]);
    if (!glb.ok) {
      return fail(`The 3D model for ${describe(object)} is ${glb.reason}.`);
    }

    objectGlbs.set(id, glb.bytes);
  }

  const known = new Set(objects.map((object) => object.id));
  for (const id of Object.keys(encoded)) {
    if (!known.has(id)) {
      warnings.push(`A 3D model with id "${id}" matches no object in the scene and was ignored.`);
    }
  }

  const objectPhotos = readPhotos(blob.objectPhotos, objects, known, warnings);

  return { ok: true, scene, roomGlb: roomGlb.bytes, objectGlbs, objectPhotos, warnings };
}

// Task R11: optional photos, one JPEG per object id. A photo is never needed
// to show the room, so a bad one is a warning, never a failure.
function readPhotos(encoded, objects, known, warnings) {
  const photos = new Map();
  if (!isObject(encoded)) {
    return photos;
  }

  for (const [id, value] of Object.entries(encoded)) {
    if (!known.has(id)) {
      warnings.push(`A photo with id "${id}" matches no object in the scene and was ignored.`);
      continue;
    }

    const bytes = decodeBase64(value);
    if (bytes === null || bytes.length < 4 || JPEG_MAGIC.some((byte, i) => bytes[i] !== byte)) {
      const object = objects.find((candidate) => candidate.id === id);
      warnings.push(`The photo of ${describe(object)} is damaged and is not shown.`);
      continue;
    }

    photos.set(id, bytes);
  }

  return photos;
}

/**
 * Decodes standard base64 (RFC 4648, with padding) to bytes, or returns null.
 * Uses atob, which both browsers and Node (16+) provide.
 */
export function decodeBase64(value) {
  if (typeof value !== 'string') {
    return null;
  }

  const clean = value.replace(/\s+/g, '');
  if (clean.length % 4 !== 0 || !/^[A-Za-z0-9+/]*={0,2}$/.test(clean)) {
    return null;
  }

  let binary;
  try {
    binary = atob(clean);
  } catch {
    return null;
  }

  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

function decodeGlb(value) {
  if (value === undefined || value === null || value === '') {
    return { ok: false, reason: 'missing' };
  }

  const bytes = decodeBase64(value);
  if (bytes === null) {
    return { ok: false, reason: 'damaged (not valid base64)' };
  }

  if (bytes.length < 12 || GLB_MAGIC.some((byte, i) => bytes[i] !== byte)) {
    return { ok: false, reason: 'not a glTF binary (.glb) file' };
  }

  return { ok: true, bytes };
}

function describe(object) {
  return typeof object.type === 'string' && object.type !== ''
    ? `the ${object.type} "${object.id}"`
    : `object "${object.id}"`;
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function fail(error) {
  return { ok: false, error };
}
