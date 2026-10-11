# Export Bundle v1

**Status:** Partial. Section 2 (`room.html` data) and section 3.1 (the `ceiling`
node) are fixed by task `R8` and implemented by `apps/web-viewer`. The rest of
the bundle (file layout, `room.glb` contents beyond 3.1, `README.txt`) is
written by task `R6`, which builds the bundle on the phone.
**Decision:** `docs/decisions/ADR-0013-phone-export-and-browser-viewer.md`.
**Owner:** Shared / Integration workstream.

Changing anything here follows `AGENTS.md` rule 8: contract doc, tests on both
sides, a dedicated commit, and status and handoff updates. Today the two sides
are `apps/web-viewer` (reader, `src/data.js`) and the reference fill in
`apps/web-viewer/scripts/fill.mjs`; the phone's bundle builder (`R6`) must do
exactly what that fill does.

---

## 1. Bundle layout

Defined by ADR-0013 section 1; the exact rules are written by `R6`.

```text
GhostMap-<room>-<yyyyMMdd-HHmm>.zip
├── room.html           section 2
├── room.glb            section 3
├── objects/<id>.glb    one per furniture object
├── scene.json          the finalized SceneSnapshot, schema v1
└── README.txt          what each file is; how to import into Unity
```

---

## 2. `room.html` data

`room.html` is `apps/web-viewer/dist/room-template.html` with its one
placeholder replaced. The template is self-contained: no `<script src>`, no
`<link href>`, no network access (its Content-Security-Policy is
`default-src 'none'`), so it works opened by double-click from a folder.

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
| `objectGlbs` | One entry per `scene.room.objects[*].id`, standard base64 of that object's `.glb`. An object with no entry is listed but not drawn, with a warning; an entry with no object is ignored, with a warning |

### 2.3 Escaping

The JSON must not end the `<script>` element early. The exporter writes every
`<` in the serialized JSON as the JSON escape `<` (so a room name or label
containing `</script>` stays inert). `JSON.parse` reads it back as `<`. No other
escaping is applied, and the exporter must not HTML-escape the JSON.

### 2.4 What the viewer does with it

- Places each `objects/<id>.glb` from `scene.json` (each object file is centred
  on the origin with no node transform): `position = (x, y, -z)` of
  `object.center`, `rotation.y = -yawDeg` in radians. See
  `apps/web-viewer/src/placement.js` for the derivation.
- Shows only values from `scene` (type, optional `label`, width, depth, height,
  ids, room name, height, opening counts). It never recomputes a room rule.
- Measures between clicked surface points in metres (3D and horizontal).

---

## 3. `.glb` files

glTF 2.0 binary, metres, +Y up, right-handed: Ghost-space coordinates with Z
negated (as `GlbExporter` writes today).

### 3.1 `room.glb` ceiling node

`room.glb` must contain exactly one node named `ceiling` whose subtree holds
all ceiling geometry and nothing else. The viewer's dollhouse toggle hides that
node. If it is missing, the viewer still shows the room but disables
dollhouse. (Name chosen in `R8`; `R6` writes it.)

### 3.2 Everything else about `room.glb` and `objects/*.glb`

To be written by `R6`.
