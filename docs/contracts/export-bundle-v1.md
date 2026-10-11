# Export Bundle v1

**Status:** Complete for v1. Section 2 was fixed by task `R8` (browser viewer)
and the rest by task `R6` (the phone builds the bundle).
**Decision:** `docs/decisions/ADR-0013-phone-export-and-browser-viewer.md`.
**Owner:** Shared / Integration workstream.
**Source of truth:** `shared/com.ghostmap.shared/Runtime/Export/` (geometry and
`.glb`), `apps/scanner/.../Runtime/Export/ExportBundleBuilder.cs` (zip and
`room.html` fill), `apps/web-viewer/` (the reader).

Changing anything here follows `AGENTS.md` rule 8: contract doc, tests on both
sides, a dedicated commit, and status and handoff updates. The two sides are
the phone (`ExportBundleBuilder`, tested in `ExportBundleBuilderTests`) and the
browser viewer (`apps/web-viewer/src/data.js`, tested in `test/data.test.mjs`
and `test/template.test.mjs`). `apps/web-viewer/scripts/fill.mjs` is the
reference fill; the phone does exactly what it does.

---

## 1. Bundle layout

On **Finalize GhostMap** the phone writes one zip and keeps it in
`Application.persistentDataPath/scans/` (`SavedScanStore`):

```text
GhostMap-<room>-<yyyyMMdd-HHmm>.zip
├── room.html           section 2
├── room.glb            section 3.1
├── objects/<id>.glb    section 3.2, one per furniture object
├── scene.json          the finalized SceneSnapshot
└── README.txt          what each file is; how to use them in Unity (section 4)
```

| Rule | Value |
| --- | --- |
| File name | `GhostMap-<room>-<yyyyMMdd-HHmm>.zip`, local time of the export. `<room>` is `room.name` with every character other than letters, digits, `.`, `_` and `-` replaced by `_`, at most 40 characters, leading and trailing `.`/`_` trimmed; `Room` if empty. A second save with the same name gets `-2`, `-3`, ... before `.zip` |
| Entry order | `room.html`, `room.glb`, `objects/*.glb` in `room.objects` order, `scene.json`, `README.txt`. Readers must not depend on it |
| Object entry names | `objects/<id>.glb`, with the id made safe by the same character rule (at most 64 characters, `object` if empty) and a `_2`, `_3` suffix on collision. The id in `scene.json` is never changed; only the file name is |
| `scene.json` | `JsonUtility.ToJson(snapshot, prettyPrint: true)`, UTF-8 without a byte-order mark. Must be `finalized: true` |
| Text encoding | Every text entry is UTF-8 without a byte-order mark |
| Entry timestamps | The export time (never before 1980-01-01) |
| Size | The fixture room with three objects: 182 KB zipped, `room.html` 695 KB. Plan section 24 caps `room.html` at 2 MB |

An unfinalized snapshot, a room without height or fewer than three corners, or
a template without exactly one placeholder is refused, and nothing is saved.

---

## 2. `room.html` data

`room.html` is `apps/web-viewer/dist/room-template.html` with its one
placeholder replaced. The scanner ships a copy as
`apps/scanner/Assets/GhostMap/Scanner/Resources/GhostMapRoomTemplate.html`; a
scanner test fails if the copy differs from the web viewer's build. The
template is self-contained: no `<script src>`, no `<link href>`, no network
access (its Content-Security-Policy is `default-src 'none'`), so it works
opened by double-click from a folder.

### 2.1 The placeholder

The template contains the string `__GHOSTMAP_DATA__` **exactly once**, as the
whole content of this tag:

```html
<script type="application/json" id="ghostmap-data">__GHOSTMAP_DATA__</script>
```

The exporter replaces that string, once, with the JSON object in 2.2. It must
not otherwise modify the template.

### 2.2 The data object

```json
{
  "format": "ghostmap-room-html",
  "formatVersion": 1,
  "scene": { "...": "the finalized SceneSnapshot, schema v1; the same content as scene.json" },
  "roomGlb": "<base64 of room.glb>",
  "objectGlbs": { "<object id>": "<base64 of objects/<id>.glb>" }
}
```

| Field | Rule |
| --- | --- |
| `format` | Exactly `"ghostmap-room-html"` |
| `formatVersion` | `1`. The viewer refuses any other value with a plain message |
| `scene` | A `SceneSnapshot` (`docs/contracts/scene-schema-v1.md`), finalized, already validated on the phone. The viewer does not re-validate room rules |
| `roomGlb` | Standard base64 (RFC 4648, with `=` padding, no line breaks) of `room.glb` |
| `objectGlbs` | Keyed by the real `scene.room.objects[*].id` (not the safe file name), standard base64 of that object's `.glb`. An object with no entry is listed but not drawn, with a warning; an entry with no object is ignored, with a warning |

### 2.3 Escaping

The JSON must not end the `<script>` element early. The exporter writes every
`<` in the serialized JSON as the JSON escape `<` (so a room name or label
containing `</script>` stays inert). `JSON.parse` reads it back as `<`. No other
escaping is applied, and the exporter must not HTML-escape the JSON.

### 2.4 What the viewer does with it

- Places each `objects/<id>.glb` from `scene.json` (each object file is centred
  on the origin with no node transform): `position = (x, y, -z)` of
  `object.center`, `rotation.y = -yawDeg` in radians, in glTF axes. See
  `apps/web-viewer/src/placement.js` for the derivation.
- Shows only values from `scene` (type, optional `label`, width, depth, height,
  ids, room name, height, opening counts). It never recomputes a room rule.
- Measures between clicked surface points in metres (3D and horizontal).

---

## 3. `.glb` files

All `.glb` files are glTF 2.0 binary, written by `GlbWriter`:

| Rule | Value |
| --- | --- |
| Units | Metres |
| Axes | glTF: right-handed, +Y up. Positions and normals are Ghost space (`scene.json`) with **Z negated** |
| Winding | Counter-clockwise front faces; every face's normal points out of its box (floor up, ceiling down) |
| Accessors | Per mesh: `POSITION` (float VEC3, `min`/`max` equal to the data exactly), `NORMAL` (float VEC3), indices (`UNSIGNED_SHORT`) |
| Buffer views | One per accessor, 4-byte aligned, with `target` 34962 (vertices) or 34963 (indices) |
| Materials | One per mesh, `pbrMetallicRoughness` with `baseColorFactor`, metallic 0, roughness 0.8. No textures |
| Validity | 0 errors, 0 warnings, 0 hints from the Khronos glTF validator (`gltf-validator` 2.0.0-dev.3.10) on the fixture room, axis-aligned and rotated 30 degrees |

### 3.1 `room.glb`

In room position (Ghost coordinates, Z negated). Nodes, in this order:

| Node | Content |
| --- | --- |
| `floor` | The footprint at y = 0, normal up, `doubleSided` material, colour `RoomPalette.Floor` |
| `ceiling` | The footprint at y = `room.heightM`, normal down, `doubleSided` material, colour `RoomPalette.Ceiling` |
| `walls` | No mesh; children `wall_0`, `wall_1`, ... one per derived wall (`RoomGeometry.BuildWalls` order) |
| `wall_<i>` | The wall's solid segments from `WallLayout` (0.10 m thick, centred on the corner line, grid-cut around doors and windows so openings are real holes), colour `RoomPalette.Wall` |

The scene's root nodes are `floor`, `ceiling` and `walls`. Exactly one node is
named `ceiling`; the browser viewer's dollhouse toggle hides it. If it were
missing the viewer would still show the room but disable dollhouse.

### 3.2 `objects/<id>.glb`

One node named `<type>_<id up to its first "-">`, one mesh built from
`FurnitureParts.BuildParts` (the same part table the Unity Viewer renders),
coloured `FurnitureParts.ColorFor(type)`. The object sits on y = 0, centred on
the origin, width along X, depth along Z (Z negated), and its node has **no
transform**: its place in the room is `center` and `yawDeg` in `scene.json`.

---

## 4. Placing furniture in Unity

Checked on 2026-10-11 by importing the fixture bundle into Unity
`6000.3.24f1` with glTFast `6.20.0` (`com.unity.cloud.gltfast`):

- **glTFast converts glTF to Unity axes by negating X.** Combined with the
  exporter's Z negation, everything imports **turned 180 degrees about Y**
  relative to `scene.json`: the fixture's floor (Ghost x 0..4, z 0..3) imports
  at x -4..0, z -3..0. Scale and +Y up are unchanged; nothing is mirrored.
- So, with `room.glb` dragged in at the origin with no rotation, each
  furniture model goes at

  ```text
  Position = (-center.x, center.y, -center.z)
  Rotation = (0, yawDeg, 0)
  ```

  Verified on the bed: placed this way it covers exactly its `scene.json`
  footprint with its headboard against the right wall.

`README.txt` in every bundle gives these steps. Whether the phone should also
write furniture already placed (for example a `room-furnished.glb`) is an open
owner decision (`docs/status/integration.md`); it would be an additive change
to this contract.
