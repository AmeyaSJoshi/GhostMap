// Builds dist/room-template.html: one self-contained HTML file holding the
// viewer, three.js and the styles, with a single data placeholder for the
// scanner to fill at export time (task R8; contract in
// docs/contracts/export-bundle-v1.md).
//
//   npm run build
//
// The build fails rather than writing a template that could reach the network.

import { build } from 'esbuild';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(fileURLToPath(import.meta.url));
const out = join(root, 'dist', 'room-template.html');

export const PLACEHOLDER = '__GHOSTMAP_DATA__';

// XML namespace identifiers that three.js passes to document.createElementNS.
// They are names, not addresses: nothing is ever fetched from them.
const ALLOWED_URLS = ['http://www.w3.org/1999/xhtml', 'http://www.w3.org/2000/svg'];

const TARGET_BYTES = 1024 * 1024;

export async function buildTemplate() {
  const result = await build({
    entryPoints: [join(root, 'src', 'main.js')],
    bundle: true,
    format: 'iife',
    minify: true,
    target: ['es2020', 'safari15'],
    legalComments: 'inline',
    charset: 'utf8',
    write: false,
    logLevel: 'silent',
  });

  // "</script" inside the inlined code would end the script tag early. It can
  // only occur inside a string, regex or comment, where "<\/" means the same.
  const script = result.outputFiles[0].text.replace(/<\/(script)/gi, '<\\/$1');
  const style = await readFile(join(root, 'src', 'style.css'), 'utf8');
  const shell = await readFile(join(root, 'src', 'index.html'), 'utf8');

  // Replacer functions, so a "$" in the bundle is never read as a pattern.
  const html = shell
    .replace('<!--GHOSTMAP:STYLE-->', () => `<style>\n${style}</style>`)
    .replace('<!--GHOSTMAP:SCRIPT-->', () => `<script>\n${script}</script>`);

  checkTemplate(html);
  return html;
}

/** Throws if the template could make a network request or cannot be filled. */
export function checkTemplate(html) {
  const problems = [];

  const placeholders = html.split(PLACEHOLDER).length - 1;
  if (placeholders !== 1) {
    problems.push(`expected the data placeholder exactly once, found ${placeholders}`);
  }

  if (html.includes('<!--GHOSTMAP:')) {
    problems.push('an inlining marker was not replaced');
  }

  if (/<script[^>]*\ssrc\s*=/i.test(html)) {
    problems.push('contains <script src=…>');
  }

  if (/<link[^>]*\shref\s*=/i.test(html)) {
    problems.push('contains <link href=…>');
  }

  const text = withoutLegalComments(html);
  for (const match of text.matchAll(/https?:\/\/[^\s"'`)<>]*/gi)) {
    if (ALLOWED_URLS.some((allowed) => match[0].startsWith(allowed))) {
      continue;
    }
    // three.js keeps citations in its GLSL shader source, which is string
    // text compiled on the GPU: a line that is a "//" comment cites a paper,
    // it never loads anything.
    const lineStart = text.lastIndexOf('\n', match.index) + 1;
    if (/^\s*\/\/ /.test(text.slice(lineStart, match.index))) {
      continue;
    }
    problems.push(`contains the URL ${match[0]}`);
  }

  if (problems.length > 0) {
    throw new Error(`room-template.html is not self-contained:\n  - ${[...new Set(problems)].join('\n  - ')}`);
  }
}

// Licence comments (/*! ... */ and /** @license ... */) may cite URLs; they are
// kept because MIT requires the notice to travel with the code.
function withoutLegalComments(html) {
  return html.replace(/\/\*[!*][\s\S]*?\*\//g, (comment) =>
    /^\/\*!|@license|@preserve/.test(comment) ? '' : comment,
  );
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const html = await buildTemplate();
  await mkdir(dirname(out), { recursive: true });
  await writeFile(out, html);
  const bytes = Buffer.byteLength(html);
  console.log(`wrote dist/room-template.html (${(bytes / 1024).toFixed(0)} KB)`);
  if (bytes > TARGET_BYTES) {
    console.warn(`warning: over the 1 MB target by ${((bytes - TARGET_BYTES) / 1024).toFixed(0)} KB`);
  }
}
