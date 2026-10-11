// npm run demo  ->  demo/room.html, filled from the repository's fixture room.
//
// Furniture comes from fixtures/objects/*.glb, written by the real C#
// GlbWriter. The room shell is a stand-in (standin-glb.mjs) so the demo needs
// no Unity; a bundle built on the phone carries the real room.glb.

import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildTemplate } from '../build.mjs';
import { fillTemplate } from './fill.mjs';
import { standInRoomGlb } from './standin-glb.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const fixture = process.argv[2] ?? join(root, '..', '..', 'fixtures', 'room-with-door-window-v1.json');

const scene = JSON.parse(await readFile(fixture, 'utf8'));
const objectGlbs = {};
for (const object of scene.room.objects) {
  objectGlbs[object.id] = await readFile(join(root, 'fixtures', 'objects', `${object.id}.glb`));
}

const html = fillTemplate(await buildTemplate(), { scene, roomGlb: standInRoomGlb(scene), objectGlbs });
await mkdir(join(root, 'demo'), { recursive: true });
await writeFile(join(root, 'demo', 'room.html'), html);
console.log(`wrote demo/room.html (${(Buffer.byteLength(html) / 1024).toFixed(0)} KB) from ${fixture}`);
