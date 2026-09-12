# Scene Schema v1

**Status:** Frozen as of Task F1.
**Schema version:** `1`
**Owner:** Shared / Integration workstream.
**Source of truth:** `shared/com.ghostmap.shared/Runtime/Domain/`

Changing anything in this document is a shared contract change. It requires an
ADR, a contract-doc update, shared tests, a dedicated commit, and a status and
handoff update. Neither app may declare its own copy of these types
(`AGENTS.md` rules 2, 3 and 8).

---

## 1. Conventions

| Rule | Value |
| --- | --- |
| Units | **meters** throughout, for every distance field |
| Angles | **degrees** |
| Up axis | **+Y** |
| Floor | **Y = 0** after normalization |
| Forward | **+Z**, camera forward projected onto the floor at floor-lock |
| Right | **+X**, `cross(up, forward)` at floor-lock |
| Serializer | Unity `JsonUtility` |

Because `JsonUtility` is the serializer, every type here is a concrete
`[Serializable]` type with **public fields**. No properties, no interfaces, no
polymorphism, no dictionaries, no nullable value types. Collections are
**arrays**, not `List<T>`, and never `null` on the wire — an absent collection is
a zero-length array.

`JsonUtility` silently ignores unknown JSON fields and leaves missing fields at
their default value. It will not raise an error for a malformed room, so
**schema conformance is not validation.** Structural validity is enforced
separately by the validators delivered in F2.

---

## 2. `Vec3Dto`

A serializable 3D vector in GhostMap coordinates.

```csharp
[Serializable]
public struct Vec3Dto
{
    public float x;
    public float y;
    public float z;

    public Vec3Dto(float x, float y, float z);

    public Vector3 ToVector3();
    public static Vec3Dto FromVector3(Vector3 value);
}
```

This exists instead of `UnityEngine.Vector3` so the wire format is owned by
GhostMap rather than by the engine's serialization layout.

---

## 3. `CornerModel`

One floor corner of the room.

```csharp
[Serializable]
public sealed class CornerModel
{
    public string id;
    public Vec3Dto position;
}
```

| Field | Meaning |
| --- | --- |
| `id` | Stable unique identifier, GUID-derived. Referenced by openings. |
| `position` | Corner position in GhostMap coordinates. |

**Rules**

- Corners are ordered clockwise or counter-clockwise.
- **Order must not change after finalization.**
- `position.y` must be `0` within tolerance.
- Order is load bearing: walls are derived from consecutive corners, so
  reordering corners silently reshapes the room.
- MVP requires **exactly four** corners.

---

## 4. `OpeningModel`

A rectangular door or window cut into one wall.

```csharp
[Serializable]
public sealed class OpeningModel
{
    public string id;
    public string type;              // "door" or "window"

    public string wallStartCornerId;
    public string wallEndCornerId;

    public float offsetM;
    public float widthM;
    public float sillHeightM;
    public float heightM;
}
```

| Field | Meaning |
| --- | --- |
| `type` | `"door"` or `"window"`. |
| `wallStartCornerId` | `id` of the corner the wall starts at. |
| `wallEndCornerId` | `id` of the corner the wall ends at. |
| `offsetM` | Distance along the wall from the **start** corner to the opening's near edge. |
| `widthM` | Opening width along the wall. |
| `sillHeightM` | Height of the opening's bottom edge above the floor. |
| `heightM` | Opening height. |

**Rules**

- The parent wall is identified by its **ordered corner pair**, not by a wall ID,
  because walls have no persisted identity.
- A door normally has `sillHeightM = 0`. A window has a positive sill.
- The opening must fit **completely inside** the wall:
  `offsetM >= 0` and `offsetM + widthM <= wallLength`.
- `sillHeightM + heightM` must not exceed `room.heightM`.
- Openings must not overlap each other.
- Validated ranges (enforced in F2): width `0.30`–`4.0` m.

---

## 5. `SceneObjectModel`

One piece of parametric furniture.

```csharp
[Serializable]
public sealed class SceneObjectModel
{
    public string id;
    public string type;   // bed, desk, chair, couch, table, dresser, tv, generic

    public Vec3Dto center;
    public float yawDeg;

    public float widthM;
    public float depthM;
    public float heightM;
}
```

| Field | Meaning |
| --- | --- |
| `type` | One of `bed`, `desk`, `chair`, `couch`, `table`, `dresser`, `tv`, `generic`. |
| `center` | Object center on the floor plane, GhostMap coordinates. |
| `yawDeg` | Rotation about **+Y** in degrees. |
| `widthM` / `depthM` / `heightM` | Bounding dimensions, stored as width × depth × height. |

**Rules**

- MVP furniture is axis-aligned to its own yaw around +Y. There is **no pitch or
  roll**.
- All dimensions must be positive and within sane bounds (enforced in F2).

**Default dimensions** (starting points only — the user may correct them):

| Type | W × D × H (m) |
| --- | --- |
| `bed` | 1.52 × 2.03 × 0.60 |
| `desk` | 1.40 × 0.70 × 0.75 |
| `chair` | 0.50 × 0.50 × 0.90 |
| `couch` | 2.10 × 0.90 × 0.85 |
| `table` | 1.50 × 0.90 × 0.75 |
| `dresser` | 1.20 × 0.50 × 0.90 |
| `tv` | 1.10 × 0.10 × 0.70 |
| `generic` | 1.00 × 1.00 × 1.00 |

---

## 6. `RoomModel`

```csharp
[Serializable]
public sealed class RoomModel
{
    public string id;
    public string name;

    public float heightM;

    public CornerModel[] corners;
    public OpeningModel[] openings;
    public SceneObjectModel[] objects;
}
```

| Field | Meaning |
| --- | --- |
| `heightM` | Floor-to-ceiling height. Valid range `2.0`–`4.0`. `0` before height capture. |

### Walls are derived, never serialized

This is the single most important rule in the schema. There is **no `walls`
field**, and adding one is a breaking change. Walls are computed from
consecutive corners:

```text
wall 0 = corner 0 -> corner 1
wall 1 = corner 1 -> corner 2
wall 2 = corner 2 -> corner 3
wall 3 = corner 3 -> corner 0
```

Storing walls alongside corners would let the two disagree. Deriving them makes
disagreement impossible. `RoomGeometry.BuildWalls` (F2) is the only sanctioned
way to produce them, and a test asserts that no serialized snapshot contains a
`"walls"` key.

---

## 7. `SceneSnapshot`

The entire scene state at one revision. This is the unit of synchronization.

```csharp
[Serializable]
public sealed class SceneSnapshot
{
    public int schemaVersion;
    public string sessionId;
    public int revision;
    public string scanPhase;
    public bool finalized;
    public float closureErrorM;
    public RoomModel room;
}
```

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Always `1` for this schema. |
| `sessionId` | Identifies one scan session. A reset produces a new ID. |
| `revision` | Monotonic within a session; increments on every structural mutation. |
| `scanPhase` | The scanner's `ScanPhase` at capture time, as a string. |
| `finalized` | `true` once the scan is finalized. Gates viewer editing. |
| `closureErrorM` | Corner-closure error measured during `VerifyClosure`. |

**Rules**

- `revision` increments for **every** structural mutation.
- The viewer **ignores** a snapshot whose revision is lower than the latest
  accepted revision for the same session.
- The viewer treats an **equal** revision as a harmless duplicate and ignores it.
- A finalized snapshot cannot be mutated by scanner-side UI.
- Authority moves exactly once per session: the scanner owns this state during
  scanning; the viewer owns it after finalization
  (`docs/decisions/ADR-0003-scanner-authority.md`).

---

## 8. Worked example

A 4.0 m × 3.0 m room, 2.5 m high, with one door and three objects. This is the
shape the F1 tests build and the shape `fixtures/valid-room-v1.json` takes in F3.

```json
{
  "schemaVersion": 1,
  "sessionId": "session-abc",
  "revision": 7,
  "scanPhase": "AddObjects",
  "finalized": false,
  "closureErrorM": 0.054,
  "room": {
    "id": "room-1",
    "name": "Bedroom",
    "heightM": 2.5,
    "corners": [
      { "id": "c0", "position": { "x": 0.0, "y": 0.0, "z": 0.0 } },
      { "id": "c1", "position": { "x": 4.0, "y": 0.0, "z": 0.0 } },
      { "id": "c2", "position": { "x": 4.0, "y": 0.0, "z": 3.0 } },
      { "id": "c3", "position": { "x": 0.0, "y": 0.0, "z": 3.0 } }
    ],
    "openings": [
      {
        "id": "door-1",
        "type": "door",
        "wallStartCornerId": "c0",
        "wallEndCornerId": "c1",
        "offsetM": 1.2,
        "widthM": 0.9,
        "sillHeightM": 0.0,
        "heightM": 2.05
      }
    ],
    "objects": [
      {
        "id": "bed-1",
        "type": "bed",
        "center": { "x": 1.0, "y": 0.0, "z": 2.2 },
        "yawDeg": 90.0,
        "widthM": 1.52,
        "depthM": 2.03,
        "heightM": 0.6
      },
      {
        "id": "desk-1",
        "type": "desk",
        "center": { "x": 3.2, "y": 0.0, "z": 0.5 },
        "yawDeg": 0.0,
        "widthM": 1.4,
        "depthM": 0.7,
        "heightM": 0.75
      },
      {
        "id": "chair-1",
        "type": "chair",
        "center": { "x": 3.2, "y": 0.0, "z": 1.3 },
        "yawDeg": 180.0,
        "widthM": 0.5,
        "depthM": 0.5,
        "heightM": 0.9
      }
    ]
  }
}
```

---

## 9. Versioning

`schemaVersion` is `1`. A receiver must reject a snapshot whose `schemaVersion`
it does not recognize rather than attempting a partial read.

- **Additive change** (new optional field with a safe default): allowed within
  version 1, but still requires a contract-doc update and tests.
- **Breaking change** (removing or renaming a field, changing units, changing a
  meaning, adding `walls`): increment `schemaVersion`, update this document, add
  an old-version rejection test, and merge the shared change before any app
  change.

---

## 10. Verification

Covered by `shared/com.ghostmap.shared/Tests/Editor/SceneSchemaTests.cs`.

Executed on Unity `6000.3.24f1`, EditMode: **14 tests, 14 passed, 0 failed.**

| Test | Asserts |
| --- | --- |
| `Vec3Dto_ConvertsToAndFromVector3` | `Vector3` conversion round trip |
| `Snapshot_PreservesSchemaVersionAndRevision` | `schemaVersion` and `revision` survive |
| `Snapshot_PreservesSessionMetadata` | `sessionId`, `scanPhase`, `finalized`, `closureErrorM` survive |
| `Snapshot_PreservesFinalizedFlag` | `finalized = true` survives |
| `Snapshot_PreservesFourCornersInOrder` | four corners, order preserved |
| `Snapshot_PreservesCornerPositions` | corner coordinates survive |
| `Snapshot_AllCornersLieOnFloorPlane` | every corner has `y = 0` |
| `Snapshot_PreservesOneDoor` | all eight opening fields survive |
| `Snapshot_PreservesThreeObjects` | three objects survive |
| `Snapshot_PreservesObjectTransformAndDimensions` | object transform and W/D/H survive |
| `Snapshot_PreservesObjectYaw` | yaw survives |
| `Snapshot_PreservesRoomIdentityAndHeight` | room id, name, height survive |
| `Snapshot_DoesNotSerializeWalls` | no `"walls"` key is ever emitted |
| `Snapshot_HandlesEmptyRoomAfterFloorLock` | post-floor-lock empty room round trips |
