// DEMO AND TEST STAND-IN ONLY. Not part of room.html and not a contract.
//
// Until task R6 adds the phone's whole-room writer (GlbWriter.TryExportRoom in
// the shared package), there is no real room.glb to load. This writes a crude
// one from a fixture so the viewer can be tested and demonstrated: a floor
// slab, a ceiling slab under a node named "ceiling", and box walls with
// rectangular gaps for openings. Only axis-aligned rectangular rooms are
// supported. Real furniture .glb files come from the C# GlbWriter (see
// fixtures/README.md); nothing here describes what furniture looks like.

const WALL_THICKNESS = 0.1; // matches the Unity Viewer's WallRenderer
const SLAB = 0.02;

/**
 * Writes a .glb with one node per entry; each node's boxes are given in glTF
 * coordinates (right-handed, +Y up, metres).
 *
 * @param {{ name: string, color: [number, number, number], boxes: { center: number[], size: number[] }[],
 *           children?: object[] }[]} nodes
 * @returns {Uint8Array}
 */
export function writeBoxGlb(nodes) {
  const flat = [];
  const gltfNodes = [];
  const sceneNodes = [];

  const visit = (node, parentIndex) => {
    const index = gltfNodes.length;
    const entry = { name: node.name };
    gltfNodes.push(entry);
    if (parentIndex === null) {
      sceneNodes.push(index);
    } else {
      (gltfNodes[parentIndex].children ??= []).push(index);
    }
    if (node.boxes && node.boxes.length > 0) {
      entry.mesh = flat.length;
      flat.push(node);
    }
    for (const child of node.children ?? []) {
      visit(child, index);
    }
  };
  nodes.forEach((node) => visit(node, null));

  const meshes = [];
  const materials = [];
  const accessors = [];
  const bufferViews = [];
  const chunks = [];
  let offset = 0;

  const addView = (bytes) => {
    bufferViews.push({ buffer: 0, byteOffset: offset, byteLength: bytes.byteLength });
    chunks.push(bytes);
    offset += bytes.byteLength;
    const pad = (4 - (offset % 4)) % 4;
    if (pad) {
      chunks.push(new Uint8Array(pad));
      offset += pad;
    }
    return bufferViews.length - 1;
  };

  for (const node of flat) {
    const { positions, normals, indices, min, max } = boxBuffers(node.boxes);
    const position = accessors.push({
      bufferView: addView(new Uint8Array(positions.buffer)),
      componentType: 5126, count: positions.length / 3, type: 'VEC3', min, max,
    }) - 1;
    const normal = accessors.push({
      bufferView: addView(new Uint8Array(normals.buffer)),
      componentType: 5126, count: normals.length / 3, type: 'VEC3',
    }) - 1;
    const index = accessors.push({
      bufferView: addView(new Uint8Array(indices.buffer)),
      componentType: 5125, count: indices.length, type: 'SCALAR',
    }) - 1;
    const material = materials.push({
      name: `${node.name}_material`,
      pbrMetallicRoughness: { baseColorFactor: [...node.color, 1], metallicFactor: 0, roughnessFactor: 0.9 },
    }) - 1;
    meshes.push({ name: node.name, primitives: [{ attributes: { POSITION: position, NORMAL: normal }, indices: index, material }] });
  }

  const json = {
    asset: { version: '2.0', generator: 'GhostMap web-viewer demo stand-in (not R6)' },
    scene: 0,
    scenes: [{ nodes: sceneNodes }],
    nodes: gltfNodes,
    meshes,
    materials,
    accessors,
    bufferViews,
    buffers: [{ byteLength: offset }],
  };

  return container(json, chunks, offset);
}

/** A stand-in room.glb for an axis-aligned rectangular scene. */
export function standInRoomGlb(scene) {
  const room = scene.room;
  const corners = room.corners.map((c) => c.position);
  const xs = corners.map((p) => p.x);
  const zs = corners.map((p) => p.z);
  const [x0, x1, z0, z1] = [Math.min(...xs), Math.max(...xs), Math.min(...zs), Math.max(...zs)];
  const h = room.heightM;
  const toGl = (x, y, z) => [x, y, -z];

  const walls = [];
  for (let i = 0; i < corners.length; i += 1) {
    const a = corners[i];
    const b = corners[(i + 1) % corners.length];
    const alongX = Math.abs(a.z - b.z) < 1e-6;
    const alongZ = Math.abs(a.x - b.x) < 1e-6;
    if (!alongX && !alongZ) {
      throw new Error('The stand-in room writer supports only axis-aligned walls.');
    }

    const length = alongX ? Math.abs(b.x - a.x) : Math.abs(b.z - a.z);
    const dir = alongX ? Math.sign(b.x - a.x) : Math.sign(b.z - a.z);
    const startId = room.corners[i].id;
    const endId = room.corners[(i + 1) % corners.length].id;
    const openings = (room.openings ?? [])
      .filter((o) => o.wallStartCornerId === startId && o.wallEndCornerId === endId)
      .sort((p, q) => p.offsetM - q.offsetM);

    // Pieces in wall-local (u along the wall, v up): full-height columns
    // between openings, plus the strips above and below each opening.
    const pieces = [];
    let u = 0;
    for (const o of openings) {
      if (o.offsetM > u) pieces.push([u, o.offsetM, 0, h]);
      if (o.sillHeightM > 0) pieces.push([o.offsetM, o.offsetM + o.widthM, 0, o.sillHeightM]);
      const top = o.sillHeightM + o.heightM;
      if (top < h) pieces.push([o.offsetM, o.offsetM + o.widthM, top, h]);
      u = o.offsetM + o.widthM;
    }
    if (u < length) pieces.push([u, length, 0, h]);

    const boxes = pieces.map(([u0, u1, v0, v1]) => {
      const mid = (u0 + u1) / 2;
      const cx = alongX ? a.x + dir * mid : a.x;
      const cz = alongZ ? a.z + dir * mid : a.z;
      const span = u1 - u0;
      return {
        center: toGl(cx, (v0 + v1) / 2, cz),
        size: alongX ? [span, v1 - v0, WALL_THICKNESS] : [WALL_THICKNESS, v1 - v0, span],
      };
    });
    walls.push({ name: `wall_${i}`, color: [0.86, 0.86, 0.84], boxes });
  }

  const slab = (y) => ({ center: toGl((x0 + x1) / 2, y, (z0 + z1) / 2), size: [x1 - x0, SLAB, z1 - z0] });

  return writeBoxGlb([
    { name: 'floor', color: [0.72, 0.68, 0.62], boxes: [slab(-SLAB / 2)] },
    { name: 'ceiling', color: [0.93, 0.93, 0.92], boxes: [slab(h + SLAB / 2)] },
    { name: 'walls', color: [0.86, 0.86, 0.84], boxes: [], children: walls },
  ]);
}

// ---- internals -----------------------------------------------------------

const FACES = [
  [[1, 0, 0], [0, 1, 0], [0, 0, 1]],
  [[-1, 0, 0], [0, 0, 1], [0, 1, 0]],
  [[0, 1, 0], [0, 0, 1], [1, 0, 0]],
  [[0, -1, 0], [1, 0, 0], [0, 0, 1]],
  [[0, 0, 1], [1, 0, 0], [0, 1, 0]],
  [[0, 0, -1], [0, 1, 0], [1, 0, 0]],
];

function boxBuffers(boxes) {
  const positions = [];
  const normals = [];
  const indices = [];
  const min = [Infinity, Infinity, Infinity];
  const max = [-Infinity, -Infinity, -Infinity];

  for (const { center, size } of boxes) {
    const half = size.map((s) => Math.abs(s) / 2);
    for (const [n, u, v] of FACES) {
      const first = positions.length / 3;
      const fc = center.map((c, k) => c + n[k] * half[k]);
      for (const [su, sv] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) {
        const p = fc.map((c, k) => c + su * u[k] * half[k] + sv * v[k] * half[k]);
        positions.push(...p);
        normals.push(...n);
        p.forEach((value, k) => { min[k] = Math.min(min[k], value); max[k] = Math.max(max[k], value); });
      }
      indices.push(first, first + 1, first + 2, first, first + 2, first + 3);
    }
  }

  return { positions: new Float32Array(positions), normals: new Float32Array(normals), indices: new Uint32Array(indices), min, max };
}

function container(json, chunks, binLength) {
  const encoder = new TextEncoder();
  let jsonBytes = encoder.encode(JSON.stringify(json));
  const jsonPad = (4 - (jsonBytes.length % 4)) % 4;
  if (jsonPad) {
    const padded = new Uint8Array(jsonBytes.length + jsonPad).fill(0x20);
    padded.set(jsonBytes);
    jsonBytes = padded;
  }

  const total = 12 + 8 + jsonBytes.length + 8 + binLength;
  const out = new Uint8Array(total);
  const view = new DataView(out.buffer);
  view.setUint32(0, 0x46546c67, true);
  view.setUint32(4, 2, true);
  view.setUint32(8, total, true);
  view.setUint32(12, jsonBytes.length, true);
  view.setUint32(16, 0x4e4f534a, true);
  out.set(jsonBytes, 20);
  let cursor = 20 + jsonBytes.length;
  view.setUint32(cursor, binLength, true);
  view.setUint32(cursor + 4, 0x004e4942, true);
  cursor += 8;
  for (const chunk of chunks) {
    out.set(chunk, cursor);
    cursor += chunk.byteLength;
  }
  return out;
}
