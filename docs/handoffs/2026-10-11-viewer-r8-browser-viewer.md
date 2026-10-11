# Handoff

## Branch
`viewer/r8-browser-viewer`, stacked on `integration/r1-test-confirmation`
(merge that first; this branch's status pages build on it).

## Base commit
`68fae97` (R1 suites recorded)

## Head commit
The commit that adds this file.

## What changed
Task `R8`: the browser viewer that every export's `room.html` is made from.

- **`apps/web-viewer/`** (new): a three.js app (`src/`), a build that bundles
  and inlines everything into `dist/room-template.html` (643 KB, committed),
  41 Node tests, a reference fill (`scripts/fill.mjs`) and a demo
  (`npm run demo`). Pinned: `three` 0.186.1, `esbuild` 0.28.2 (both MIT).
- **Features:** orbit, pan, zoom; Frame room (`F`); Dollhouse (`D`) hides the
  `room.glb` node named `ceiling`; click or pick from the list to select an
  object (outline from its `scene.json` box; panel with type, optional
  `label`, W × D × H in m and cm, id); Measure (`M`), two clicks, 3D and along
  the floor, `Esc` cancels; room summary; plain-sentence errors; light and dark.
- **No network:** CSP `default-src 'none'`; `build.mjs` fails on any URL other
  than XML namespace names and citations in three.js's GLSL comments.
- **Contract:** `docs/contracts/export-bundle-v1.md` created with section 2
  (the `room.html` data object, the placeholder, `<` escaped as `<`) and
  3.1 (`room.glb` must have one node named `ceiling`). The rest is left to `R6`.
- **CI:** new `web-viewer` job: `npm ci`, `npm test`, `npm run build`, then
  `git diff --exit-code dist/`.
- **Test fixtures:** `apps/web-viewer/fixtures/objects/{bed-1,desk-1,chair-1}.glb`
  were written by the **real C# `GlbExporter`** from
  `fixtures/valid-room-v1.json`, using a throwaway EditMode test in
  `apps/viewer` that was deleted afterwards (`apps/viewer` is unchanged). This
  is also the first time an exported `.glb` was loaded by an outside glTF
  reader: three.js's GLTFLoader reads all three, and the bed's bounds match its
  `scene.json` box.
- Docs: viewer, integration and shared status; plan R8; tasks index and R8
  brief (fixed its `<` typo); spec FR-15; ADR index; README; docs map;
  `AGENTS.md` quick reference; architecture overview 3.3 and new 12.4; CI doc;
  onboarding (status, repo tour, testing, four new gotchas).

## Decisions taken while building
- **Placement:** `position = (x, y, -z)`, `rotation.y = -yaw`, derived in
  `src/placement.js` from Unity's `Quaternion.Euler(0, yaw, 0)` and the
  exporter's Z mirror. Tested on a hand-worked yaw-30 case and on the real
  exported bed (headboard must sit against the x = 0 wall); a flipped sign
  fails the bed test (checked by mutation).
- **Starts in dollhouse mode**, from a high corner: a closed box seen from a
  corner shows only its roof.
- **Framing a selected object** looks from above and from the room's middle;
  keeping the current direction put the camera behind the wall that furniture
  usually stands against, which also blocked clicks.
- **Missing object model** is a warning, not an error: the object is listed
  but not drawn. Bad base64 or a non-GLB payload is an error.
- **Room summary "Floor"** is the outer extent of the corners, labelled as
  such; no area, wall length or other rule is computed in JavaScript.

## Contract impact
- New `docs/contracts/export-bundle-v1.md` (sections 2 and 3.1). Additive; no
  change to scene schema v1 or protocol v1. `R6` must fill the template
  exactly as `apps/web-viewer/scripts/fill.mjs` does and name the ceiling node
  `ceiling`.

## How to test
1. `cd apps/web-viewer && npm ci && npm test && npm run build`;
   `git diff --exit-code dist/` must be clean.
2. `npm run demo`, then double-click `demo/room.html` in Safari with Wi-Fi off.
   Try Frame room, Dollhouse, click the desk, pick the bed in the list, Measure.
3. Open `dist/room-template.html` alone: it says it is the template.

## Test results
- `npm test`: 41 tests, 41 passed (Node 24, owner's Mac). Rebuilt template
  byte-identical to the committed one, also after a clean `npm ci`.
- Headless Chrome 154, `file://`, driven over the DevTools protocol, at
  1280×800 and 390×844: the page is the only request; no console errors or
  exceptions; canvas click selects the desk; list selection frames the object;
  Frame room, Dollhouse and Measure (bed top to chair seat: 2.30 m, along floor
  2.30 m) work; the unfilled template shows its message; no horizontal scroll
  at phone width.
- Not run: Safari, Firefox, a real double-click. The CI job has not run yet
  (this branch is not pushed).
- Unity suites not re-run: no Unity source changed (the temporary export test
  was removed before commit).

## Known failures
- None in the tests.
- Two bugs found and fixed during the browser check: the message overlay's
  `display: grid` overrode `hidden` and covered the whole page (fixed with a
  global `[hidden]` rule); object framing could put the camera behind a wall.

## Files most important to read next
- `docs/contracts/export-bundle-v1.md`
- `apps/web-viewer/README.md`, `src/main.js`, `src/placement.js`
- `apps/web-viewer/scripts/fill.mjs` (what `R6` must reproduce in C#)

## Next task
- Owner: the Safari double-click check, recorded in `docs/status/viewer.md`.
- `R6`: real `room.glb` (with the `ceiling` node), and the phone-side fill of
  the template, shipping a copy of `dist/room-template.html` as a TextAsset.
- Owner: `R2` on the iPhone.
