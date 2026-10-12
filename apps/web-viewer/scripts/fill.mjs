// Reference implementation of the fill step the phone performs at export time
// (task R6), used here by the demo and the tests. The rules it follows are the
// contract in docs/contracts/export-bundle-v1.md, section "room.html data".

export const PLACEHOLDER = '__GHOSTMAP_DATA__';

/**
 * @param {string} template  dist/room-template.html
 * @param {{ scene: object, roomGlb: Uint8Array, objectGlbs: Record<string, Uint8Array>,
 *          objectPhotos?: Record<string, Uint8Array> }} room
 */
export function fillTemplate(template, { scene, roomGlb, objectGlbs, objectPhotos = {} }) {
  if (template.split(PLACEHOLDER).length !== 2) {
    throw new Error('The template must contain the data placeholder exactly once.');
  }

  const blob = {
    format: 'ghostmap-room-html',
    formatVersion: 1,
    scene,
    roomGlb: base64(roomGlb),
    objectGlbs: Object.fromEntries(Object.entries(objectGlbs).map(([id, bytes]) => [id, base64(bytes)])),
    objectPhotos: Object.fromEntries(Object.entries(objectPhotos).map(([id, bytes]) => [id, base64(bytes)])),
  };

  // Escape "<" so no string in the scene (a room name or label containing
  // "</script>") can end the data tag. JSON.parse reads < back as "<".
  const json = JSON.stringify(blob).replace(/</g, '\\u003c');

  return template.replace(PLACEHOLDER, () => json);
}

function base64(bytes) {
  return Buffer.from(bytes.buffer, bytes.byteOffset, bytes.byteLength).toString('base64');
}
