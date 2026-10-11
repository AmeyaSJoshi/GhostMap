import { test } from 'node:test';
import assert from 'node:assert/strict';
import { distance3d, distanceHorizontal, formatMetres, describeMeasurement } from '../src/measure.js';

test('3D distance includes height, horizontal ignores it', () => {
  const a = [0, 0, 0];
  const b = [3, 4, -4];
  assert.equal(distance3d(a, b), Math.hypot(3, 4, 4));
  assert.equal(distanceHorizontal(a, b), 5);
});

test('points straight above each other are 0 apart along the floor', () => {
  assert.equal(distanceHorizontal([1, 0, 2], [1, 2.5, 2]), 0);
  assert.equal(distance3d([1, 0, 2], [1, 2.5, 2]), 2.5);
});

test('metres are shown to two decimals', () => {
  assert.equal(formatMetres(1.234), '1.23 m');
  assert.equal(formatMetres(1.235), '1.24 m');
  assert.equal(formatMetres(2), '2.00 m');
  assert.equal(formatMetres(0.004), '0.00 m');
  assert.equal(formatMetres(Number.NaN), '–');
});

test('describeMeasurement returns both texts', () => {
  assert.deepEqual(describeMeasurement([0, 0, 0], [0, 1, 1]), { direct: '1.41 m', horizontal: '1.00 m' });
});
