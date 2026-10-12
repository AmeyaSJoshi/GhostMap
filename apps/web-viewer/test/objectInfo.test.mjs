import { test } from 'node:test';
import assert from 'node:assert/strict';
import { typeName, objectTitle, objectDetails, objectSummary, roomDetails, footprintExtent } from '../src/objectInfo.js';
import { readFixture } from './helpers.mjs';

// The eight GhostMap types (docs/contracts/scene-schema-v1.md, section 5).
const TYPES = ['bed', 'desk', 'chair', 'couch', 'table', 'dresser', 'tv', 'generic'];

test('every GhostMap type has a display name', () => {
  assert.deepEqual(TYPES.map(typeName), ['Bed', 'Desk', 'Chair', 'Couch', 'Table', 'Dresser', 'TV', 'Object']);
  assert.equal(typeName('lamp'), 'lamp');
  assert.equal(typeName(undefined), 'Object');
});

test('details list type and size in metres and centimetres', () => {
  const rows = objectDetails({ id: 'desk-1', type: 'desk', widthM: 1.4, depthM: 0.7, heightM: 0.75 });
  assert.deepEqual(rows, [
    ['Type', 'Desk'],
    ['Width', '1.40 m (140 cm)'],
    ['Depth', '0.70 m (70 cm)'],
    ['Height', '0.75 m (75 cm)'],
    ['Id', 'desk-1'],
  ]);
});

test('the detector label is shown only when present', () => {
  const plant = { id: 'p', type: 'generic', label: 'potted plant', widthM: 0.4, depthM: 0.4, heightM: 0.9 };
  assert.equal(objectTitle(plant), 'Potted plant');
  assert.deepEqual(objectDetails(plant)[1], ['Detected as', 'potted plant']);

  for (const label of [undefined, '', '   ']) {
    const box = { id: 'g', type: 'generic', label, widthM: 1, depthM: 1, heightM: 1 };
    assert.equal(objectTitle(box), 'Object');
    assert.equal(objectDetails(box).some(([name]) => name === 'Detected as'), false);
  }
});

test('a typed object keeps its type as the title; its label is only a detail', () => {
  const desk = { id: 'desk-1', type: 'desk', label: 'dining table', widthM: 1.4, depthM: 0.7, heightM: 0.75 };
  assert.equal(objectTitle(desk), 'Desk');
  assert.deepEqual(objectDetails(desk)[1], ['Detected as', 'dining table']);
  assert.equal(objectSummary(desk), 'Desk · 1.40 × 0.70 × 0.75 m');
});

test('the shared fixture carries a label (task R5)', async () => {
  const scene = await readFixture('room-with-door-window-v1.json');
  const desk = scene.room.objects.find((object) => object.id === 'desk-1');
  assert.equal(desk.label, 'dining table');
  assert.equal(objectTitle(desk), 'Desk');
});

test('list summary', () => {
  assert.equal(objectSummary({ type: 'bed', widthM: 1.52, depthM: 2.03, heightM: 0.6 }), 'Bed · 1.52 × 2.03 × 0.60 m');
});

test('room details come from scene.json', async () => {
  const scene = await readFixture('room-with-door-window-v1.json');
  const rows = Object.fromEntries(roomDetails(scene));
  assert.equal(rows['Ceiling height'], '2.50 m');
  assert.equal(rows.Doors, '1');
  assert.equal(rows.Windows, '1');
  assert.equal(rows.Objects, '3');
  assert.equal(rows.Floor, '4.00 × 3.00 m (outer extent)');
});

test('footprint extent is the box around the corners', () => {
  const corners = [[0, 0], [3, 1], [2, 4], [-1, 3]].map(([x, z]) => ({ position: { x, y: 0, z } }));
  assert.deepEqual(footprintExtent(corners), { x: 4, z: 4 });
  assert.equal(footprintExtent([]), null);
  assert.equal(footprintExtent([{ position: { x: 1 } }]), null);
});
