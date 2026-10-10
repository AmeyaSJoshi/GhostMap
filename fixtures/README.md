# Fixtures

Canonical scene files for developing and testing without a phone. Each file is a
bare `SceneSnapshot` (scene schema v1), not a wire message;
`tools/send_fixture.py` wraps one in protocol messages, and the Viewer's
**Load Fixture** button reads `valid-room-v1.json` directly.

| File | Contents | Valid |
| --- | --- | --- |
| `valid-room-v1.json` | 4.0 × 3.0 m room, 2.5 m high, one door, bed + desk + chair, finalized | Yes |
| `room-with-door-window-v1.json` | The same room plus a window on wall `c1 → c2` | Yes |
| `malformed-room-v1.json` | Height 5.20 m, outside 2.0-4.0 m. Breaks exactly one rule | No, on purpose |

Keys starting with `_` are comments; tools strip them before sending.

Fixtures are owned by the Shared/Integration workstream. Check any change with
`python3 tools/inspect_snapshot.py <file>`.
