# R8: Browser viewer, `room.html`

**Decision:** ADR-0013 section 3. **Workstream:** Viewer (`apps/web-viewer/**`).
**Who:** an agent, end to end. Everything here runs under Node; no Unity, no
iPhone. The owner only double-clicks the result in Safari once.

## Goal

A single self-contained HTML file that, opened by double-click from a folder
with no internet, shows the room in 3D: orbit, pan, zoom, frame room, dollhouse
(hide ceiling), click an object for its name/type/label/dimensions, two-click
measuring (3D and horizontal), and an object list. **View and measure only.**

The scanner (`R6`) fills the template at export time. This task builds the
template, the app, and the tests; it does not touch C#.

## Nothing exists yet

`apps/web-viewer/` does not exist. Create it. Node 22 and npm are available in
agent containers (`/opt/node22/bin`); check what the network allows before
assuming `npm install` works (see the session's network documentation).

## Layout

```text
apps/web-viewer/
├── README.md               what this is, how to build, how to test
├── package.json            exact versions (no ^ or ~), scripts: build, test
├── package-lock.json       committed
├── src/
│   ├── index.html          page shell, contains the data placeholder
│   ├── main.js             entry: read data, build scene, wire UI
│   ├── data.js             parse + check embedded data (pure, tested)
│   ├── placement.js        scene.json object → three.js transform (pure, tested)
│   ├── measure.js          3D and horizontal distance, formatting (pure, tested)
│   ├── objectInfo.js       info-panel text for an object (pure, tested)
│   └── style.css
├── build.mjs               esbuild bundle + inline into one HTML
├── test/*.test.mjs         node:test
├── fixtures/               a sample data blob built from fixtures/*.json
└── dist/room-template.html built output, committed
```

Dependencies, pinned exactly: `three` (MIT) and `esbuild` (dev). Nothing else
unless it is MIT/BSD/Apache and justified in the README. Use three.js's
`GLTFLoader` and `OrbitControls` from `three/examples/jsm/...`.

## The data contract with the scanner

This is the seam between `R8` and `R6`. Write it into
`docs/contracts/export-bundle-v1.md` (which `R6` creates; if `R8` lands first,
create the file with just this section and let `R6` extend it).

The template contains exactly one placeholder, inside a JSON script tag:

```html
<script type="application/json" id="ghostmap-data">__GHOSTMAP_DATA__</script>
```

The scanner replaces `__GHOSTMAP_DATA__` with one JSON object:

```json
{
  "format": "ghostmap-room-html",
  "formatVersion": 1,
  "scene": { "...": "the finalized SceneSnapshot, schema v1, exactly scene.json" },
  "roomGlb": "<base64 of room.glb>",
  "objectGlbs": { "<object id>": "<base64 of objects/<id>.glb>" }
}
```

- The scanner must escape `<` as `<` in the JSON it inserts, so a label
  containing `</script>` cannot end the tag. Test this on both sides.
- When the placeholder is still present (opening the raw template), the page
  shows "This is the GhostMap template. Open a room.html from an export."
- Unknown `formatVersion` → a plain error message, no crash.

## Coordinates (get this right first)

- `scene.json` is in **Ghost space**: Unity axes, left-handed, metres, +Y up
  (`docs/architecture/overview.md`, scene schema v1).
- The `.glb` files are glTF: right-handed, +Y up. `GlbExporter` converts by
  negating Z.
- `room.glb` is already in room position (Ghost positions, Z negated).
- Each `objects/<id>.glb` is at the **origin** with **no node transform**
  (checked in `GlbExporter.Build`: the node has only `mesh` and `name`). The
  viewer places it from `scene.json`:
  - `position = (center.x, center.y, -center.z)`
  - `rotation.y = -yawDeg × π / 180` (mirroring Z flips the rotation sense)
- Why `-yaw`: the Unity Viewer applies `Quaternion.Euler(0f, model.yawDeg, 0f)`
  (`apps/viewer/.../Rendering/FurnitureFactory.cs`, in `Create`). That matrix,
  conjugated by the Z mirror, is the same matrix form with the angle negated,
  and three.js `rotation.y` uses that same form.
- Put both rules in `placement.js` and test them with an asymmetric case: a
  2 m × 1 m bed at yaw 30° must have its footprint corners where Unity would put
  them, Z negated. Write the expected numbers by hand from the matrix above
  (width along local X, depth along local Z; confirm against
  `FurnitureFactory.BuildParts`) and cite the file in the test. If the result disagrees with the walls in `room.glb`, the rule is wrong;
  fix it here, never by changing the exporter.

Measurements and the info panel report **scene.json values** (width, depth,
height in metres), not values recomputed from meshes.

## Features

| Feature | Behaviour |
| --- | --- |
| Load | Decode base64 → `ArrayBuffer` → `GLTFLoader.parse`. No `fetch`, no `file://` reads |
| Camera | `OrbitControls`; start framed on the room from a high corner |
| Frame room | Button resets the camera to the start view |
| Dollhouse | Toggle hides the ceiling mesh. Find it by node name; agree the name with `R6` (`ceiling`) and put it in the contract |
| Select | Click → raycast → highlight object; panel shows type, label (if `R5` landed), W × D × H in m and cm, id |
| Object list | Side list of objects; click selects and frames it |
| Measure | Button enters measure mode; two clicks on any surface; shows 3D distance and horizontal (XZ) distance to 0.01 m; Esc cancels |
| Room info | Room name, floor size from the corners (read from `scene.json`, not recomputed rules), height, number of doors/windows/objects |
| Errors | Bad or missing data shows one plain sentence, never a blank page |

No editing, no saving, no network. Do not port any shared validation or
geometry rule (`AGENTS.md` rule 3); if a number is not in `scene.json`, do not
invent it.

## Build

`npm run build`:

1. esbuild bundles `src/main.js` (with three.js) to one minified IIFE.
2. `build.mjs` inlines the bundle and `style.css` into `src/index.html`, keeping
   the placeholder, and writes `dist/room-template.html`.
3. Fails if the output contains `http://` or `https://` anywhere except inside
   licence comments, or contains `<script src=` / `<link href=`.
4. Prints the template size. Target under 1 MB (three.js minified is roughly
   0.6 MB).

Commit `dist/room-template.html`. The scanner ships a copy as a Unity
`TextAsset` (for example
`apps/scanner/Assets/GhostMap/Scanner/Resources/RoomTemplate.html.txt`); that
copy is made by `R6`/integration, not by this task, because it is in the
scanner's directory (`AGENTS.md` rule 7). Document the copy step in the README
and in `docs/onboarding.md`.

## Tests (`npm test`, node:test, no browser)

- `data.js`: valid blob parses; placeholder detected; wrong version, missing
  `scene`, bad base64, object id with no GLB, GLB with no object → each gives
  its message.
- `placement.js`: the coordinate rules above, including the asymmetric case.
- `measure.js`: 3D vs horizontal distance; formatting and rounding.
- `objectInfo.js`: every furniture type; label shown only when present.
- Template: `dist/room-template.html` exists, contains the placeholder exactly
  once, and no external URL. Run the build in the test (or check the committed
  file is fresh by rebuilding and diffing).
- Fill test: a Node script fills the template from `fixtures/valid-room-v1.json`
  plus tiny hand-made GLBs and checks the result is valid HTML with the data
  tag parsed back to the same JSON, including a label containing `</script>`.

Add a `web-viewer` job to `.github/workflows/tests.yml` running `npm ci`,
`npm test`, and `npm run build` followed by `git diff --exit-code dist/`.

## Manual check

1. Fill the template from a fixture (`npm run demo` writes `demo/room.html`).
2. Turn off Wi-Fi; double-click it in Safari, Chrome and Firefox.
3. Check every feature row. In Safari's Web Inspector, the Network tab shows no
   requests.

## Done when

`npm test` and the build pass in CI; the template is committed; the contract
section is written; the owner (or an agent with a browser via Playwright, which
is installed in agent containers) has opened a filled demo in Chromium and
recorded the result in `docs/status/viewer.md`; a handoff is written.
