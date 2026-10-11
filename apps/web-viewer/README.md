# GhostMap Web Viewer

The source of `room.html`, the page in every GhostMap export that shows the room
in any browser: orbit, pan, zoom, frame the room, dollhouse (hide the ceiling),
click an object for its name and size, measure between two points (3D and
along the floor), and a furniture list. **View and measure only.** Task `R8`,
ADR-0013 section 3. Status: `docs/status/viewer.md`.

It builds into one self-contained file, `dist/room-template.html` (three.js,
the app and the styles inlined, about 0.65 MB). The phone fills its single data
placeholder at export time (task `R6`). The data format and the fill rules are
the contract `docs/contracts/export-bundle-v1.md`, section 2.

## Commands

Node 20 or newer.

```bash
npm ci             # exact versions from package-lock.json
npm test           # node:test, no browser needed
npm run build      # writes dist/room-template.html; fails if it could reach the network
npm run demo       # writes demo/room.html from fixtures/room-with-door-window-v1.json
```

Commit `dist/room-template.html` after every change; CI rebuilds it and fails
if the committed file is stale.

## Layout

| Path | What |
| --- | --- |
| `src/index.html` | Page shell with the data placeholder and the inlining markers |
| `src/main.js` | Entry: reads the data, builds the three.js scene, wires the tools |
| `src/data.js` | Parses and checks the embedded data (pure, tested) |
| `src/placement.js` | `scene.json` position and yaw to three.js (pure, tested) |
| `src/measure.js` | 3D and horizontal distance, formatting (pure, tested) |
| `src/objectInfo.js` | Text for the info panel, list and room summary (pure, tested) |
| `src/style.css` | Styles, light and dark |
| `build.mjs` | esbuild bundle, inlining, self-containment check |
| `scripts/fill.mjs` | Reference fill: exactly what the phone must do with the template |
| `scripts/standin-glb.mjs` | **Demo and test stand-in** for `room.glb`, so Node tests need no Unity. The phone writes the real one (`GlbWriter.TryExportRoom`, task `R6`) |
| `scripts/make-demo.mjs` | Builds `demo/room.html` (git-ignored) |
| `fixtures/objects/*.glb` | Furniture written by the real C# `GlbWriter`; see `fixtures/README.md` |
| `test/*.test.mjs` | The test suite |
| `dist/room-template.html` | Built output, committed |

## Rules

- **No network.** No `fetch`, no external script or stylesheet. The page's
  Content-Security-Policy is `default-src 'none'`, and `build.mjs` refuses any
  URL except XML namespace names and citations inside three.js's GLSL
  comments. Licence comments are kept because MIT requires them.
- **No shared rules in JavaScript** (`AGENTS.md` rule 3). Every number shown
  comes from `scene.json`. Do not port validation or geometry from
  `shared/com.ghostmap.shared`.
- **Coordinates.** `scene.json` is Ghost space (Unity, left-handed); `.glb` is
  glTF (right-handed, Z negated). Objects are placed with
  `position = (x, y, -z)` and `rotation.y = -yaw`. The derivation is in
  `src/placement.js`, and `test/placement.test.mjs` checks it against a real
  exported bed. If placement ever disagrees with `room.glb`'s walls, fix it
  here, never in the exporter.
- **Dependencies** are pinned exactly: `three` 0.186.1 (MIT) and, for the
  build only, `esbuild` 0.28.2 (MIT). Add nothing else without a reason in this
  README.

## Shipping the template to the phone

The scanner ships a copy of `dist/room-template.html` as the Unity `TextAsset`
`apps/scanner/Assets/GhostMap/Scanner/Resources/GhostMapRoomTemplate.html`
(task `R6`). After changing the viewer: `npm run build`, then recopy:

```bash
cp apps/web-viewer/dist/room-template.html apps/scanner/Assets/GhostMap/Scanner/Resources/GhostMapRoomTemplate.html
```

The scanner test `ShippedTemplate_MatchesTheWebViewerBuild` fails while the
copy is stale. The phone fills it in `ExportBundleBuilder.TryFillTemplate`,
which follows `scripts/fill.mjs`.

## Manual check

1. `npm run demo`, then double-click `demo/room.html` (Safari, Chrome, Firefox),
   ideally with Wi-Fi off.
2. Check each tool. The browser's network panel should show only the page.
3. Open `dist/room-template.html` on its own: it should say it is the template.
