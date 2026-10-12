import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseRoomData, decodeBase64, TEMPLATE_MESSAGE } from '../src/data.js';
import { readFixture, base64, TINY_GLB } from './helpers.mjs';

async function validBlob() {
  const scene = await readFixture('valid-room-v1.json');
  return {
    format: 'ghostmap-room-html',
    formatVersion: 1,
    scene,
    roomGlb: base64(TINY_GLB),
    objectGlbs: Object.fromEntries(scene.room.objects.map((o) => [o.id, base64(TINY_GLB)])),
  };
}

test('a valid blob parses with every object model and no warnings', async () => {
  const result = parseRoomData(JSON.stringify(await validBlob()));
  assert.equal(result.ok, true);
  assert.equal(result.scene.room.name, 'Fixture Bedroom');
  assert.deepEqual([...result.objectGlbs.keys()], ['bed-1', 'desk-1', 'chair-1']);
  assert.deepEqual([...result.roomGlb.slice(0, 4)], [0x67, 0x6c, 0x54, 0x46]);
  assert.deepEqual(result.warnings, []);
});

test('the unfilled template placeholder is recognised', () => {
  const result = parseRoomData('__GHOSTMAP_' + 'DATA__');
  assert.equal(result.ok, false);
  assert.equal(result.isTemplate, true);
  assert.equal(result.error, TEMPLATE_MESSAGE);
});

test('empty data gives a plain message', () => {
  const result = parseRoomData('   ');
  assert.equal(result.ok, false);
  assert.match(result.error, /no room data/);
});

test('damaged JSON gives a plain message', () => {
  const result = parseRoomData('{"format": ');
  assert.equal(result.ok, false);
  assert.match(result.error, /damaged/);
});

test('a different format is refused', async () => {
  const blob = { ...(await validBlob()), format: 'something-else' };
  assert.match(parseRoomData(JSON.stringify(blob)).error, /does not contain GhostMap room data/);
});

test('an unknown format version names both versions', async () => {
  const blob = { ...(await validBlob()), formatVersion: 2 };
  const result = parseRoomData(JSON.stringify(blob));
  assert.equal(result.ok, false);
  assert.match(result.error, /format version 2/);
  assert.match(result.error, /reads version 1/);
});

test('a missing scene is refused', async () => {
  const blob = await validBlob();
  delete blob.scene;
  assert.match(parseRoomData(JSON.stringify(blob)).error, /no scene/);
});

test('a scene without corners is refused', async () => {
  const blob = await validBlob();
  blob.scene.room.corners = [];
  assert.match(parseRoomData(JSON.stringify(blob)).error, /no floor outline/);
});

test('a missing room model is refused', async () => {
  const blob = await validBlob();
  delete blob.roomGlb;
  assert.match(parseRoomData(JSON.stringify(blob)).error, /room model is missing/);
});

test('bad base64 for the room is refused', async () => {
  const blob = { ...(await validBlob()), roomGlb: 'not base64!!' };
  assert.match(parseRoomData(JSON.stringify(blob)).error, /room model is damaged/);
});

test('base64 that is not a .glb is refused', async () => {
  const blob = { ...(await validBlob()), roomGlb: base64(new TextEncoder().encode('hello, not a model')) };
  assert.match(parseRoomData(JSON.stringify(blob)).error, /not a glTF binary/);
});

test('bad base64 for one object names that object', async () => {
  const blob = await validBlob();
  blob.objectGlbs['desk-1'] = '%%%%';
  assert.match(parseRoomData(JSON.stringify(blob)).error, /the desk "desk-1" is damaged/);
});

test('an object with no model is kept, with a warning', async () => {
  const blob = await validBlob();
  delete blob.objectGlbs['chair-1'];
  const result = parseRoomData(JSON.stringify(blob));
  assert.equal(result.ok, true);
  assert.equal(result.objectGlbs.has('chair-1'), false);
  assert.equal(result.warnings.length, 1);
  assert.match(result.warnings[0], /chair "chair-1"/);
});

test('a model with no matching object is ignored, with a warning', async () => {
  const blob = await validBlob();
  blob.objectGlbs['ghost-9'] = base64(TINY_GLB);
  const result = parseRoomData(JSON.stringify(blob));
  assert.equal(result.ok, true);
  assert.equal(result.objectGlbs.has('ghost-9'), false);
  assert.match(result.warnings[0], /"ghost-9" matches no object/);
});

test('an object without an id is refused', async () => {
  const blob = await validBlob();
  delete blob.scene.room.objects[0].id;
  assert.match(parseRoomData(JSON.stringify(blob)).error, /has no id/);
});

test('escaped "<" in the blob reads back as "<"', async () => {
  const blob = await validBlob();
  blob.scene.room.name = 'Den </script><b>';
  const text = JSON.stringify(blob).replace(/</g, '\\u003c');
  assert.equal(text.includes('<'), false);
  assert.equal(parseRoomData(text).scene.room.name, 'Den </script><b>');
});

test('decodeBase64 round-trips bytes and rejects malformed input', () => {
  const bytes = new Uint8Array([0, 1, 2, 250, 251, 255]);
  assert.deepEqual(decodeBase64(base64(bytes)), bytes);
  assert.equal(decodeBase64('abc'), null); // length not a multiple of 4
  assert.equal(decodeBase64('ab$='), null);
  assert.equal(decodeBase64(42), null);
});

// ---- Task R11: object photos ------------------------------------------------

const TINY_JPEG = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 16]);

test('photos are optional: a blob without objectPhotos has none and no warning', async () => {
  const result = parseRoomData(JSON.stringify(await validBlob()));
  assert.equal(result.objectPhotos.size, 0);
  assert.deepEqual(result.warnings, []);
});

test('photos are read per object id', async () => {
  const blob = { ...(await validBlob()), objectPhotos: { 'desk-1': base64(TINY_JPEG) } };
  const result = parseRoomData(JSON.stringify(blob));
  assert.equal(result.ok, true);
  assert.deepEqual([...result.objectPhotos.keys()], ['desk-1']);
  assert.deepEqual([...result.objectPhotos.get('desk-1')], [...TINY_JPEG]);
  assert.deepEqual(result.warnings, []);
});

test('a damaged or non-JPEG photo is a warning, never a failure', async () => {
  const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a]);
  const blob = { ...(await validBlob()), objectPhotos: { 'bed-1': 'not base64!', 'chair-1': base64(png) } };
  const result = parseRoomData(JSON.stringify(blob));
  assert.equal(result.ok, true);
  assert.equal(result.objectPhotos.size, 0);
  assert.equal(result.warnings.length, 2);
  assert.match(result.warnings[0], /photo of the bed "bed-1" is damaged/);
});

test('a photo for no object is ignored with a warning', async () => {
  const blob = { ...(await validBlob()), objectPhotos: { lamp: base64(TINY_JPEG) } };
  const result = parseRoomData(JSON.stringify(blob));
  assert.equal(result.objectPhotos.size, 0);
  assert.match(result.warnings[0], /photo with id "lamp" matches no object/);
});
