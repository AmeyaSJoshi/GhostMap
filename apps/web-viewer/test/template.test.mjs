import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { buildTemplate, checkTemplate, PLACEHOLDER } from '../build.mjs';
import { fillTemplate } from '../scripts/fill.mjs';
import { standInRoomGlb } from '../scripts/standin-glb.mjs';
import { parseRoomData } from '../src/data.js';
import { readFixture, readObjectGlb, webViewerRoot } from './helpers.mjs';

const built = await buildTemplate();
const committed = await readFile(join(webViewerRoot, 'dist', 'room-template.html'), 'utf8').catch(() => null);

const dataTag = (html) => {
  const match = html.match(/<script type="application\/json" id="ghostmap-data">([\s\S]*?)<\/script>/);
  assert.ok(match, 'data tag present and closed');
  return match[1];
};

test('the committed template matches a fresh build (run npm run build)', () => {
  assert.ok(committed !== null, 'dist/room-template.html exists');
  assert.equal(committed, built);
});

test('the template holds the placeholder exactly once, inside the data tag', () => {
  assert.equal(built.split(PLACEHOLDER).length - 1, 1);
  assert.equal(dataTag(built), PLACEHOLDER);
});

test('the template references nothing outside itself', () => {
  assert.doesNotThrow(() => checkTemplate(built));
  assert.doesNotMatch(built, /<script[^>]*\ssrc=/i);
  assert.doesNotMatch(built, /<link[^>]*\shref=/i);
  assert.match(built, /Content-Security-Policy" content="default-src 'none'/);
});

test('the template stays under the 1 MB target', () => {
  assert.ok(Buffer.byteLength(built) < 1024 * 1024, `${Buffer.byteLength(built)} bytes`);
});

test('checkTemplate refuses external URLs and a missing placeholder', () => {
  assert.throws(() => checkTemplate(built.replace('</body>', '<img src="https://example.com/x.png"></body>')), /example\.com/);
  assert.throws(() => checkTemplate(built.replace(PLACEHOLDER, '')), /exactly once/);
  assert.throws(() => checkTemplate(built.replace('</body>', '<script src="a.js"></script></body>')), /script src/);
});

test('the unfilled template shows the template message', () => {
  const result = parseRoomData(dataTag(built));
  assert.equal(result.isTemplate, true);
});

test('a filled template carries the scene and models back intact, even with "</script>" in a label', async () => {
  const scene = await readFixture('room-with-door-window-v1.json');
  scene.room.name = 'Studio </script><script>alert(1)</script>';
  scene.room.objects[0].label = 'bed </script>';

  const objectGlbs = {};
  for (const object of scene.room.objects) {
    objectGlbs[object.id] = await readObjectGlb(object.id);
  }
  const roomGlb = standInRoomGlb(scene);

  const html = fillTemplate(built, { scene, roomGlb, objectGlbs });

  assert.equal(html.includes(PLACEHOLDER), false);
  // Exactly one data tag, and the hostile strings never appear raw.
  assert.equal(html.split('id="ghostmap-data"').length - 1, 1);
  assert.equal(html.includes('alert(1)</script>'), false);

  const result = parseRoomData(dataTag(html));
  assert.equal(result.ok, true, result.error);
  assert.deepEqual(result.scene, scene);
  assert.deepEqual(result.roomGlb, roomGlb);
  for (const [id, bytes] of Object.entries(objectGlbs)) {
    assert.deepEqual(result.objectGlbs.get(id), bytes);
  }
  assert.deepEqual(result.warnings, []);
});

test('fillTemplate refuses a template without exactly one placeholder', () => {
  assert.throws(() => fillTemplate('<html></html>', { scene: {}, roomGlb: new Uint8Array(), objectGlbs: {} }), /exactly once/);
});

test('a room.glb with a node named "ceiling" exposes it to the dollhouse toggle', async () => {
  const { GLTFLoader } = await import('three/examples/jsm/loaders/GLTFLoader.js');
  const scene = await readFixture('valid-room-v1.json');
  const bytes = standInRoomGlb(scene);
  const gltf = await new GLTFLoader().parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
  const ceiling = gltf.scene.getObjectByName('ceiling'); // CEILING_NODE in src/main.js
  assert.ok(ceiling, 'ceiling node found');
  assert.ok(gltf.scene.getObjectByName('floor'), 'floor node found');
});

test('a filled template carries object photos back intact, and its CSP lets them show (task R11)', async () => {
  const scene = await readFixture('room-with-door-window-v1.json');
  const objectGlbs = {};
  for (const object of scene.room.objects) {
    objectGlbs[object.id] = await readObjectGlb(object.id);
  }
  const photo = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4]);

  const html = fillTemplate(built, { scene, roomGlb: standInRoomGlb(scene), objectGlbs, objectPhotos: { 'bed-1': photo } });

  const result = parseRoomData(dataTag(html));
  assert.equal(result.ok, true, result.error);
  assert.deepEqual(result.objectPhotos.get('bed-1'), photo);
  assert.match(built, /img-src[^;"]*blob:/);
  assert.match(built, /id="selection-photo"/);
});
