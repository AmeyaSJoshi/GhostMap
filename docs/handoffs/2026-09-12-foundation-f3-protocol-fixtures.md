# Handoff

## Branch
`main`

## Base commit
`2ee62eb` (F2 sha recorded)

## Head commit
`PENDING — recorded in the follow-up docs commit`

## What changed

Task **F3: Protocol v1 + fixtures**. This completes the foundation.

`Runtime/Protocol/`:

- `ProtocolConstants` — version 1, port 47831, 262144-byte line cap, `"\n"`
  terminator, the five type strings, and the heartbeat/reconnect/pose-rate
  constants.
- `WireMessages` — `WireMessageHeader` plus `HelloMessage`, `HeartbeatMessage`,
  `PhonePoseMessage`, `SceneSnapshotMessage`, `ScanFinalizedMessage`. Each
  concrete type sets its own `type` string in its constructor, so a message
  cannot be built with the wrong discriminator.
- `ProtocolSerializer` — `Serialize`, `SerializeLine`, `TryDeserialize`. Follows
  the plan's six-step algorithm exactly and never throws.
- `SnapshotRevisionPolicy` — revision arbitration, with the `SnapshotAcceptance`
  enum.

Fixtures, tools, and `docs/contracts/protocol-v1.md` (replacing the placeholder).

### Design decisions worth knowing

1. **Fixtures hold a bare `SceneSnapshot`, not a wire message.** The viewer's
   "Load fixture" button (task V1) wants the payload directly, and
   `send_fixture.py` wraps it in `hello` → `scene.snapshot` → `scan.finalized`.
   Transport is the tool's concern, not the fixture's.

2. **`SnapshotRevisionPolicy` lives in the shared package**, not inside the
   viewer. Plan section 17 describes this logic as `ViewerSceneStore` behavior,
   but putting it in `shared` means the rule is defined and tested exactly once
   rather than reimplemented per consumer. V1 should call it rather than
   rewriting it. This adds a fourth file to `Runtime/Protocol/`, which plan
   section 3 lists with three; the addition is additive and nothing listed was
   renamed or removed.

3. **The malformed fixture violates exactly one rule** — `heightM` of 5.20 m,
   outside 2.0–4.0 m. Everything else in it is valid, so it is rejected on the
   height rule alone. A tempting alternative, a bow-tie footprint, was rejected
   because a bow-tie also collapses the polygon area to zero and so violates two
   rules at once, which would make the fixture useless for pinpointing a
   regression.

4. **Keys beginning with `_` are documentation only** and are stripped before
   transmission. `JsonUtility` ignores them on the Unity side. This lets the
   malformed fixture carry an inline explanation of what is wrong with it.

5. **`inspect_snapshot.py` re-implements the validation rules in Python.** This
   duplication is deliberate and narrow: it answers "is the data bad, or is the
   renderer bad?" — step 4 of the plan's debugging checklist — without needing
   the Unity editor. If a shared validation limit changes, this script must be
   updated alongside it; the constants sit together at the top of the file for
   that reason.

### One implementation bug found and fixed by a test

`RejectsSnapshotMessageWithoutSnapshot` failed on first run with
*"unsupported scene schema version 0"* instead of a message about the missing
snapshot. `JsonUtility` materializes a **default instance** for a missing nested
object rather than leaving it null, so an absent `snapshot` field arrives as a
zeroed `SceneSnapshot`, and the null guard never fired.

Fixed in `ProtocolSerializer` by detecting an effectively-absent snapshot
explicitly. Left unfixed, anyone debugging a scanner that forgot to attach its
snapshot would have been sent chasing a schema-version problem that did not
exist. The behavior is documented in `docs/contracts/protocol-v1.md` section 4.

## Contract impact

**Protocol v1 is now frozen.** Additive — no prior protocol existed. Scene schema
v1 is unchanged.

`docs/contracts/protocol-v1.md` is authoritative and matches the code.

## How to test

```bash
# Shared EditMode suite.
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-f3.xml -logFile /tmp/ghostmap-f3.log

# Fixture validation, outside Unity.
python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json            # exits 0
python3 tools/inspect_snapshot.py --quiet fixtures/malformed-room-v1.json # exits 1

# Wire format, without a scanner.
python3 tools/send_fixture.py --dry-run
python3 tools/send_fixture.py --host <viewer-ip>
```

## Test results

**Actually executed on Unity `6000.3.24f1`, EditMode. Unity exit code `0`.**

```text
total=143  passed=143  failed=0  skipped=0  result=Passed
```

Per fixture: SceneSchemaTests 14, CoordinateFrameTests 10, RayPlaneMathTests 10,
RoomGeometryTests 19, MeasurementMathTests 5, RoomValidatorTests 26,
OpeningValidatorTests 19, FurnitureValidatorTests 11, ProtocolSerializerTests 29.

This run was performed **after deleting `shared/TestProject/Library/`**, so it
also proves the shared package resolves from a clean state.

Every test the plan's F3 section requires is present and passing: round trip of
each message type, rejection of unknown protocol version, rejection of unknown
message type, snapshot round trip, the stale-revision helper, and all three
fixtures parsing and validating.

**Test-driven, verified red before green.** The suite was written first and run
against an empty `Runtime/Protocol/`, producing compile failures and Unity exit
`1`. One test then failed for a real reason — the `JsonUtility` default-instance
bug above — which was fixed in the implementation, not by weakening the test.

### Also executed outside Unity

- `inspect_snapshot.py` on all three fixtures: valid room **VALID** (exit 0),
  door+window **VALID** (exit 0), malformed **INVALID with exactly one problem**,
  `room height 5.20 m is outside 2.0-4.0 m` (exit 1).
- `send_fixture.py --dry-run`: 3 messages, 177 / 1214 / 142 bytes.
- `send_fixture.py` over **real TCP to a listener on 127.0.0.1:47831**: the
  listener received exactly 3 newline-delimited messages, 1376 bytes total,
  parsed each as protocol v1, and asserted the trailing terminator was present
  and that documentation-only `_` keys had been stripped. Both processes exited 0.

## Known failures

None.

## Known limitations

- `RoomValidator.ValidateRoom` does **not** validate openings. The viewer must
  call `OpeningValidator.Validate` for each opening separately before applying a
  snapshot. This is stated in `docs/contracts/protocol-v1.md` section 4.
- No physical-device testing applies to this workstream. Nothing here has been
  run on an iPhone, and no AR code exists yet.

## Files most important to read next

1. `AGENTS.md`
2. `docs/contracts/scene-schema-v1.md` and `docs/contracts/protocol-v1.md` — the
   two frozen contracts both apps build against.
3. `docs/status/scanner.md` or `docs/status/viewer.md` — your workstream.
4. `docs/plans/ghostmap-implementation-plan.md` section 16 (S1–S6) or 17 (V1–V6).
5. `shared/TestProject/README.md` — how to run the shared tests.

## Next task

**The foundation is complete and the gate passes. The workstreams may now split.**

- Scanner: **S1 — Scanner Unity project + physical-device AR smoke test.**
  Note that S1 cannot be declared done on Editor behavior alone; it requires a
  real iPhone.
- Viewer: **V1 — Viewer project + TCP server + fixture ingestion.** V1 can be
  developed entirely against `fixtures/` and `tools/send_fixture.py` without
  waiting for the scanner. Use `SnapshotRevisionPolicy` for revision
  arbitration rather than reimplementing it.

Neither app may duplicate a shared type, and neither may change a shared contract
without following the procedure in implementation plan section 27.

### Note on status files

This task updated `docs/status/scanner.md` and `docs/status/viewer.md` in
addition to `docs/status/shared.md`, which the directory-ownership rule would
normally reserve to those workstreams. Both files asserted "Blocked by the
foundation gate", which is now false, and neither workstream exists yet to
correct it. The edits only record that the gate passed and name each
workstream's first task.
