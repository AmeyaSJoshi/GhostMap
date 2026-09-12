# Protocol v1

**Status:** Frozen as of Task F3.
**Protocol version:** `1`
**Carries scene schema:** v1 (`docs/contracts/scene-schema-v1.md`)
**Owner:** Shared / Integration workstream.
**Source of truth:** `shared/com.ghostmap.shared/Runtime/Protocol/`

Changing anything here is a shared contract change requiring an ADR, a contract
doc update, tests, a dedicated commit, and a status and handoff update. Replacing
full snapshots with delta or event replay additionally requires overturning
`docs/decisions/ADR-0002-snapshot-protocol.md` (`AGENTS.md` rule 5).

---

## 1. Transport

| Property | Value |
| --- | --- |
| Transport | TCP |
| Port | `47831` |
| Encoding | UTF-8 |
| Framing | One JSON object per line, terminated with `\n` |
| Max line length | `262144` bytes |
| Direction | Scanner → Viewer only |

The viewer accepts **one** active scanner connection. When a new connection
arrives it replaces the old disconnected session.

A line longer than the maximum is **rejected, not buffered**, so a malformed or
hostile peer cannot exhaust memory. A serialized message never contains a raw
newline: `JsonUtility` escapes newlines inside string values, and a test asserts
this.

There is no viewer→scanner scene channel in v1. Authority is time-sliced
instead (`docs/decisions/ADR-0003-scanner-authority.md`).

---

## 2. Message header

Every message carries these fields:

```csharp
[Serializable]
public class WireMessageHeader
{
    public int protocolVersion;   // always 1
    public string type;
    public string sessionId;
    public long sequence;
    public long unixTimeMs;
}
```

| Field | Meaning |
| --- | --- |
| `protocolVersion` | Always `1`. A receiver rejects any other value. |
| `type` | One of the five types below. |
| `sessionId` | Identifies one scan session. A reset produces a new ID. |
| `sequence` | Monotonic per connection. Diagnostics only — it is **not** used for ordering or deduplication; `SceneSnapshot.revision` is. |
| `unixTimeMs` | Send time, milliseconds since the Unix epoch. Diagnostics only. |

---

## 3. Message types

Exactly five types exist in v1. Adding one is a contract change.

```text
hello
heartbeat
phone.pose
scene.snapshot
scan.finalized
```

### 3.1 `hello`

Sent immediately on connect, and again after every reconnect.

```csharp
public sealed class HelloMessage : WireMessageHeader
{
    public string appVersion;
    public string deviceName;
}
```

### 3.2 `heartbeat`

Liveness only; carries no scene state. Sent every **2 seconds**.

```csharp
public sealed class HeartbeatMessage : WireMessageHeader { }
```

### 3.3 `phone.pose`

**Debug and display only.** Capped at **5 Hz**.

```csharp
public sealed class PhonePoseMessage : WireMessageHeader
{
    public Vec3Dto position;
    public float yawDeg;
    public string trackingState;
    public string notTrackingReason;
}
```

The room is **never** reconstructed from pose messages. They exist so the viewer
can show tracking quality and where the phone is.

### 3.4 `scene.snapshot`

The entire current scene.

```csharp
public sealed class SceneSnapshotMessage : WireMessageHeader
{
    public SceneSnapshot snapshot;
}
```

Sent immediately after each of:

- floor lock;
- corner add/remove;
- room height update;
- opening add/remove/edit;
- furniture add/remove/edit;
- scan finalization;
- reconnection (the current snapshot is resent).

Never sent per frame. Snapshot JSON must not be generated every frame.

### 3.5 `scan.finalized`

Marks the handover of authority from scanner to viewer.

```csharp
public sealed class ScanFinalizedMessage : WireMessageHeader
{
    public int finalRevision;
}
```

The scanner **must send the final snapshot immediately before** this message, so
the viewer's last received state is complete when it takes ownership.

---

## 4. Serialization

```csharp
public static class ProtocolSerializer
{
    public static string Serialize(object message);
    public static string SerializeLine(object message);   // Serialize + "\n"
    public static bool TryDeserialize(string json, out object message, out string error);
}
```

Deserialization follows a fixed algorithm:

1. parse JSON into `WireMessageHeader`;
2. validate `protocolVersion`;
3. switch on `type`;
4. parse again into the concrete message class;
5. validate fields;
6. return the message for dispatch.

There is **no reflection-based polymorphic serialization.** `JsonUtility` does
not support it, and an explicit switch is easier to diagnose from a wire log.

`TryDeserialize` never throws. A malformed line is a normal event on a local
network: the viewer logs it, rejects it, and keeps its current scene.

### Rejection cases

| Condition | Result |
| --- | --- |
| Empty, whitespace, or null input | rejected |
| Longer than 262144 bytes | rejected |
| `protocolVersion != 1` | rejected |
| Missing or unknown `type` | rejected |
| Malformed JSON | rejected |
| `scene.snapshot` with no snapshot | rejected |
| Snapshot `schemaVersion != 1` | rejected |
| `scene.snapshot` with no room | rejected |
| Unknown extra fields | **accepted and ignored** |

> **Implementation note.** `JsonUtility` materializes a *default* instance for a
> missing nested object rather than leaving it null, so an absent `snapshot`
> field arrives as a zeroed `SceneSnapshot` rather than as `null`. The serializer
> detects that case explicitly; otherwise the caller would be told "unsupported
> schema version 0" when the real problem is that the snapshot is absent.

### Validation is separate

Deserializing successfully means the message is **well-formed**, not that the
room is **valid**. The viewer must additionally run `RoomValidator.ValidateRoom`,
and `OpeningValidator.Validate` for each opening, before applying a snapshot.
`RoomValidator.ValidateRoom` does not validate openings.

---

## 5. Revision arbitration

`SceneSnapshot.revision` is monotonic within a `sessionId` and is the **only**
ordering authority. Implemented once, in the shared package, as
`SnapshotRevisionPolicy`:

```csharp
public enum SnapshotAcceptance
{
    AcceptNewSession,
    AcceptNewerRevision,
    IgnoreDuplicateRevision,
    IgnoreStaleRevision
}

public static SnapshotAcceptance Evaluate(SceneSnapshot current, SceneSnapshot incoming);
public static bool ShouldApply(SnapshotAcceptance acceptance);
```

| Situation | Outcome |
| --- | --- |
| Nothing held yet | `AcceptNewSession` |
| Different `sessionId` | `AcceptNewSession` — even at a lower revision |
| Same session, `revision >` current | `AcceptNewerRevision` |
| Same session, `revision ==` current | `IgnoreDuplicateRevision` — harmless resend |
| Same session, `revision <` current | `IgnoreStaleRevision` |

Duplicates are **expected**, because reconnection resends the current snapshot.
They must be harmless.

---

## 6. Connection lifecycle

### Scanner

1. Keep the latest `SceneSnapshot` in memory at all times.
2. On disconnect, show a disconnected state. **Never discard scene data.**
3. Retry every **2 seconds** while the app is foregrounded.
4. After reconnect: send `hello`, then immediately send the current snapshot,
   then resume heartbeats.

### Viewer

1. Accept one active scanner connection.
2. Replace an old disconnected session when a new connection arrives.
3. **Never erase the displayed scene because the network dropped.**
4. Show `Disconnected — displaying last snapshot.`

---

## 7. Wire example

Three lines, exactly as `tools/send_fixture.py` transmits them (pretty-printed
here for readability; on the wire each is a single line ending in `\n`).

```json
{"protocolVersion":1,"type":"hello","sessionId":"fixture-valid-room-v1","sequence":0,"unixTimeMs":1757721600000,"appVersion":"fixture-tool/1.0","deviceName":"send_fixture.py"}
```

```json
{"protocolVersion":1,"type":"scene.snapshot","sessionId":"fixture-valid-room-v1","sequence":1,"unixTimeMs":1757721600001,"snapshot":{ /* full SceneSnapshot, see scene-schema-v1.md */ }}
```

```json
{"protocolVersion":1,"type":"scan.finalized","sessionId":"fixture-valid-room-v1","sequence":2,"unixTimeMs":1757721600002,"finalRevision":12}
```

Measured sizes for the reference fixture: `hello` 177 bytes, `scene.snapshot`
1214 bytes, `scan.finalized` 142 bytes — well inside both the 262144 byte line
limit and the 100 KB snapshot target.

---

## 8. Fixtures

Fixture files hold a **bare `SceneSnapshot`** — the payload, not the envelope.
`tools/send_fixture.py` wraps one in the messages above. This keeps the fixtures
usable directly by the viewer's "Load fixture" button without unwrapping.

| File | Contents |
| --- | --- |
| `fixtures/valid-room-v1.json` | 4.0 × 3.0 m room, 2.5 m high, 1 door, bed + desk + chair |
| `fixtures/room-with-door-window-v1.json` | the same room plus a window on wall `c1→c2` |
| `fixtures/malformed-room-v1.json` | **intentionally invalid**: `heightM` is 5.20 m, outside 2.0–4.0 m |

The malformed fixture violates **exactly one** rule. Everything else in it —
corner order, spacing, footprint area, wall lengths, interior angles, its door
and its object — is valid, so it is rejected on the height rule alone. It exists
to prove the viewer rejects bad scenes instead of rendering them.

Keys beginning with `_` are documentation only and are stripped before
transmission.

---

## 9. Tools

```bash
# Inspect and validate a snapshot without opening Unity.
python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json
python3 tools/inspect_snapshot.py --quiet fixtures/malformed-room-v1.json

# Send a fixture to a running viewer.
python3 tools/send_fixture.py --host 192.168.1.20
python3 tools/send_fixture.py --dry-run --fixture fixtures/room-with-door-window-v1.json
```

`inspect_snapshot.py` exits `0` when valid and `1` when invalid, so it can gate a
script. It re-implements the shared validation rules in Python deliberately: it
answers "is the data bad, or is the renderer bad?" — step 4 of the plan's
debugging checklist — without needing the Unity editor.

---

## 10. Versioning

`protocolVersion` is `1`. A receiver rejects any version it does not recognize
rather than attempting a partial read.

- **Additive change** (new optional field with a safe default): allowed within
  version 1, but still requires a contract doc update and tests.
- **Breaking change** (new or removed message type, changed field meaning,
  different framing or port, delta replay): increment `protocolVersion`, update
  this document, add an old-version rejection test, and merge the shared change
  before any app change.

---

## 11. Verification

Covered by `shared/com.ghostmap.shared/Tests/Editor/ProtocolSerializerTests.cs`.

Executed on Unity `6000.3.24f1`, EditMode: **143 tests across the shared suite,
143 passed, 0 failed** (29 of them protocol tests).

Verified: round trip of all five message types; no raw newline in output; single
trailing terminator; port and line-length constants; rejection of unknown
protocol version, unknown type, missing type, malformed JSON, empty input,
oversized line, missing snapshot and unknown schema version; tolerance of unknown
fields; all five revision-arbitration outcomes; and that each of the three
fixtures parses, validates as intended, and survives a wire round trip.

Additionally verified outside Unity: `send_fixture.py` transmitted the reference
fixture over real TCP to a listener on port 47831, which received exactly three
newline-delimited messages, parsed each as protocol v1, and confirmed the
trailing terminator and the stripping of documentation-only keys.
