// Coordinate rules from docs/tasks/R8-browser-viewer.md, "Coordinates".
//
// Expected numbers are worked by hand from Unity's placement in
// apps/viewer/.../Rendering/FurnitureFactory.cs (Create: position = center,
// rotation = Quaternion.Euler(0, yawDeg, 0); width along local X, depth along
// local Z) and GlbWriter's Z mirror, never from the code under test.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { objectTransform, ghostToThree } from '../src/placement.js';
import { readFixture, readObjectGlb } from './helpers.mjs';

const close = (actual, expected, label) =>
  assert.ok(Math.abs(actual - expected) < 1e-4, `${label}: expected ${expected}, got ${actual}`);

function place(object) {
  const root = new THREE.Object3D();
  const { position, rotationY } = objectTransform(object);
  root.position.set(...position);
  root.rotation.y = rotationY;
  root.updateMatrixWorld(true);
  return root;
}

test('position mirrors Z and yaw is negated', () => {
  const { position, rotationY } = objectTransform({ center: { x: 1.5, y: 0, z: 2.25 }, yawDeg: 30 });
  assert.deepEqual(position, [1.5, 0, -2.25]);
  close(rotationY, (-30 * Math.PI) / 180, 'rotationY');
});

test('a 2 m x 1 m object at yaw 30 lands where Unity puts it, Z negated', () => {
  // Unity: world = c + R(30) l, with R(t) x' = x cos t + z sin t, z' = -x sin t + z cos t.
  // c = (1, 0, 2); local corner l = (+1, 0, +0.5) (half width, half depth):
  //   x' = 1 + 0.866025 + 0.25      = 2.116025
  //   z' = 2 - 0.5 + 0.433013       = 1.933013   -> glTF (2.116025, 0, -1.933013)
  // and l = (-1, 0, +0.5):
  //   x' = 1 - 0.866025 + 0.25      = 0.383975
  //   z' = 2 + 0.5 + 0.433013       = 2.933013   -> glTF (0.383975, 0, -2.933013)
  const root = place({ center: { x: 1, y: 0, z: 2 }, yawDeg: 30 });

  // The .glb holds local vertices already mirrored: (x, y, -z).
  const a = new THREE.Vector3(1, 0, -0.5).applyMatrix4(root.matrixWorld);
  close(a.x, 2.116025, 'corner A x');
  close(a.z, -1.933013, 'corner A z');

  const b = new THREE.Vector3(-1, 0, -0.5).applyMatrix4(root.matrixWorld);
  close(b.x, 0.383975, 'corner B x');
  close(b.z, -2.933013, 'corner B z');
});

test('missing or non-finite fields fall back to zero', () => {
  const { position, rotationY } = objectTransform({ center: { x: Number.NaN, y: 0 } });
  assert.deepEqual(position, [0, 0, -0]);
  assert.equal(rotationY, -0);
});

test('room points mirror Z', () => {
  assert.deepEqual(ghostToThree({ x: 4, y: 2.5, z: 3 }), [4, 2.5, -3]);
});

test('the real exported bed fills its scene.json footprint, headboard against the x = 0 wall', async () => {
  // fixtures/valid-room-v1.json: bed centre (1.0, 0, 2.2), yaw 90, W 1.52, D 2.03, H 0.60.
  // Yaw 90 turns local depth (Z) onto Ghost X, so the footprint is
  //   x in [1 - 1.015, 1 + 1.015] = [-0.015, 2.015]
  //   z in [2.2 - 0.76, 2.2 + 0.76] = [1.44, 2.96]  -> glTF z in [-2.96, -1.44]
  const scene = await readFixture('valid-room-v1.json');
  const bed = scene.room.objects.find((o) => o.id === 'bed-1');

  const gltf = await new GLTFLoader().parseAsync((await readObjectGlb('bed-1')).buffer, '');
  const root = place(bed);
  root.add(gltf.scene);
  root.updateMatrixWorld(true);

  const box = new THREE.Box3().setFromObject(root);
  close(box.min.x, -0.015, 'min x');
  close(box.max.x, 2.015, 'max x');
  close(box.min.z, -2.96, 'min z');
  close(box.max.z, -1.44, 'max z');
  close(box.min.y, 0, 'min y');
  close(box.max.y, 0.6, 'max y');

  // The headboard is the only part reaching full height, at local -Z
  // (FurnitureFactory.Bed). Unity's yaw 90 sends local -Z to Ghost -X, so the
  // tallest vertices must sit at the x = 0 end. A sign error would put them
  // at x = 2.
  const tall = [];
  root.traverse((node) => {
    if (!node.isMesh) return;
    const positions = node.geometry.attributes.position;
    const v = new THREE.Vector3();
    for (let i = 0; i < positions.count; i += 1) {
      v.fromBufferAttribute(positions, i).applyMatrix4(node.matrixWorld);
      if (v.y > 0.59) tall.push(v.x);
    }
  });
  assert.ok(tall.length > 0, 'found headboard vertices');
  assert.ok(Math.max(...tall) < 0.2, `headboard at x ${Math.min(...tall)}..${Math.max(...tall)}`);
});
