import { readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const webViewerRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
export const repoRoot = join(webViewerRoot, '..', '..');

export async function readFixture(name) {
  return JSON.parse(await readFile(join(repoRoot, 'fixtures', name), 'utf8'));
}

/** Bytes of a .glb written by the C# GlbWriter (see fixtures/README.md). */
export async function readObjectGlb(id) {
  return new Uint8Array(await readFile(join(webViewerRoot, 'fixtures', 'objects', `${id}.glb`)));
}

export function base64(bytes) {
  return Buffer.from(bytes).toString('base64');
}

/** The smallest valid-looking GLB header, enough for data.js's magic check. */
export const TINY_GLB = new Uint8Array([0x67, 0x6c, 0x54, 0x46, 2, 0, 0, 0, 12, 0, 0, 0]);
