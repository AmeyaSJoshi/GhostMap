# Handoff

## Branch
`main`

Foundation tasks run on `main` per implementation plan section 28, Stage A.

## Base commit
`4a6d636` (F3 sha recorded)

## Head commit
`<filled in below after the commit>`

## What changed

Task **F4: independent review of the F0-F3 shared foundation.** No new feature
work. The whole foundation was re-read against `AGENTS.md`, the project spec, the
implementation plan, both contract documents, all four prior handoffs, and the
shared source and tests; then every claim in those documents was re-executed.

**Verdict: the foundation gate passes, after one real defect was fixed.**

### Defect found and fixed

**`RoomGeometry.InteriorAngleDeg` did not compute an interior angle.**

It returned `Vector3.Angle(a, b)` — the *unsigned* angle between the two edges
meeting at a corner. That value cannot exceed 180°, so a reflex interior angle
was reported as its 360° complement.

*Root cause.* The unsigned angle between two edges is ambiguous: it does not say
which side of the corner is inside the room. The polygon's winding is what
disambiguates it, and the original implementation never consulted it.

*Consequence.* Plan section 9.1 and `docs/architecture/overview.md` section 9
both require every internal angle to be between 35° and 145°. A concave "dart"
footprint — a simple, non-self-intersecting quad whose fourth corner is pulled
inside the triangle formed by the other three — slipped through. Concretely, the
footprint

```text
c0 (0.00, 0.00)   c1 (4.98, 0.00)   c2 (1.19, 4.71)   c3 (1.07, 1.36)
```

has a true interior angle of **216.1°** at `c3`. The old code reported it as
143.9°, inside the permitted band. Area (10.02 m²) and all four wall lengths
(1.73 m – 6.05 m) are legal, so **no other rule caught it and `ValidateRoom`
returned valid.** `ValidateNewCorner` does not catch it either: it checks
spacing and self-intersection, and a dart is not self-intersecting.

This is exactly the class of defect the review was looking for. The scanner would
have accepted the room, transmitted it, and the viewer — running the same shared
validator — would have accepted it too, then triangulated a floor around a reflex
corner. Both apps would have agreed, and both would have been wrong.

*Fix.* `InteriorAngleDeg` now reads the footprint's winding from
`SignedPolygonAreaXZ` and returns the true interior angle in `[0, 360)`. A
degenerate footprint, where winding cannot be read, falls back to the unsigned
angle rather than guessing a side. Six lines of arithmetic; no signature change,
no schema change, no protocol change.

*Verified numerically before and after*: interior angles now sum to 360° for
every quad tested, a rectangle reads 90° at each corner in either winding, and
the dart reads 216.1° in either winding.

**This is not a contract change.** The published rule (35°–145°) is unchanged.
The implementation now enforces the rule it always claimed to. No ADR is
required. `docs/architecture/overview.md` section 9 was reworded to make the rule
unambiguous about reflex corners.

### Parity gaps fixed in `tools/inspect_snapshot.py`

The script deliberately re-implements the shared validation rules in Python so a
snapshot can be checked without Unity. The review compared it rule by rule
against the C# validators and found **four divergences**, each confirmed by
running the script on a constructed snapshot:

| # | Case | Python said | C# says |
| --- | --- | --- | --- |
| 1 | dart footprint | VALID | invalid (same defect as above) |
| 2 | mid-scan snapshot with 2 corners | INVALID, "expected 4 corners" | valid so far |
| 3 | opening on a room whose height is not yet captured | VALID | invalid |
| 4 | collinear degenerate footprint | self-intersection missed | self-intersection detected |

Divergence 2 is the worst of these for its stated purpose. The tool exists to
answer "is the data bad, or is the renderer bad?" and it was calling every
legitimate live mid-scan snapshot bad data.

All four are fixed: the interior-angle formula now matches, partial corner chains
are validated the way `RoomValidator.ValidatePartialChain` does, openings on a
height-less room are reported as unjudgeable, and the collinear-overlap branch of
`RoomGeometry.SegmentsIntersectXZ` was ported across.

### Coverage added where behavior was documented but not locked

These found no defect. Each locks a documented decision that every existing test
would have passed even if the code were wrong.

- **Handedness.** Nothing asserted that world-right maps to ghost **+X**. All ten
  coordinate-frame tests pass for a *mirrored* basis, because a round trip
  through a mirrored frame still round-trips. A scanner that built its frame as
  `cross(forward, up)` instead of the documented `cross(up, forward)` would have
  mirrored every room, and the viewer would have rendered the mirror image with
  no test objecting. Three tests added, including one that asserts signed
  footprint area keeps its sign through the conversion.
- **`TryFindWall` rejects a reversed corner pair.** F2 handoff decision 3; an
  opening's `offsetM` is measured from its declared start corner, so a reversed
  pair must fail loudly instead of mirroring the opening along the wall.
- **`InteriorAngleDeg` had no direct unit test at all.** Four added.
- **Absent `protocolVersion` is read as v1**, because the field initializer runs
  before JSON overwrites it. Deliberate, previously undocumented; now documented
  in protocol-v1.md section 4 and locked by a test.
- **The malformed fixture breaks exactly one rule.** Asserted by repairing its
  height and requiring the result to be fully valid, so the fixture cannot
  silently acquire a second defect.

### Documentation corrected

- `docs/contracts/protocol-v1.md` section 7 attributed wire sizes of
  177 / 1214 / 142 bytes to "the reference fixture". Those are
  `room-with-door-window-v1.json`'s. The reference fixture
  (`valid-room-v1.json`, which `send_fixture.py` uses by default) is
  176 / 1059 / 141. Replaced with a measured table covering all three fixtures.
- `docs/status/shared.md` claimed `docs/contracts/protocol-v1.md` "is still a
  placeholder" — it has been authoritative since F3 — and recorded the last
  verified commit as F1's, two tasks stale.
- `docs/status/integration.md` claimed "No Unity installation on the current
  development machine." Unity `6000.3.24f1` is installed and ran the suite.
- `fixtures/malformed-room-v1.json` described "all three objects"; it carries one.
- Test counts in both contract documents updated from 143 to 156.

### Reviewed and found correct — no change needed

Recorded so the next reviewer does not repeat the work:

- Scene schema v1: all six domain types match the contract field for field;
  `JsonUtility` constraints (public fields, arrays, no properties) are respected
  throughout; no `walls` key is emitted, asserted by test and re-asserted over
  real TCP.
- Protocol v1: five message types, the six-step deserialization algorithm, and
  every row of the rejection table match the code.
- `GhostCoordinateFrame` matches plan section 8.1-8.3 exactly, including
  `right = cross(up, forward)`, which preserves Unity's handedness.
- `RayPlaneMath` rejects parallel, grazing and behind-the-origin intersections
  rather than extrapolating, as plan section 8.4 requires.
- Validation limits match the table in `docs/architecture/overview.md` section 9
  and the ranges in `scene-schema-v1.md` field by field.
- `SnapshotRevisionPolicy` implements all five arbitration outcomes correctly.
- No workstream ownership violation: nothing under `apps/scanner/**` or
  `apps/viewer/**` was touched by F0-F3 beyond the empty scaffold, and F3's
  edits to the two app status files are disclosed in its own handoff.
- Generated Unity folders are gitignored and were not committed.

## Contract impact

**None.** Scene schema v1 and protocol v1 are unchanged — no field, type, unit,
message, port or framing rule was altered. Two documentation clarifications were
made (`protocol-v1.md` section 4 on an absent `protocolVersion`,
`overview.md` section 9 on reflex angles), both describing behavior that already
existed.

`RoomGeometry.InteriorAngleDeg` changes its returned value for concave
footprints. It is a published shared interface, but the change makes it match its
own documented rule, so it is a bug fix rather than a contract change. Nothing
consumes it yet: neither app exists.

## How to test

```bash
# Shared EditMode suite.
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath shared/TestProject \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ghostmap-f4.xml -logFile /tmp/ghostmap-f4.log

# Fixture validation, outside Unity.
python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json              # exits 0
python3 tools/inspect_snapshot.py fixtures/room-with-door-window-v1.json   # exits 0
python3 tools/inspect_snapshot.py --quiet fixtures/malformed-room-v1.json  # exits 1

# Wire format.
python3 tools/send_fixture.py --dry-run
python3 tools/send_fixture.py --host <viewer-ip>
```

## Test results

**Actually executed on Unity `6000.3.24f1`, EditMode. Unity exit code `0`.**

```text
total=156  passed=156  failed=0  skipped=0  result=Passed
```

143 from F0-F3 plus 13 added here.

**Test-driven, verified red before green.** The four tests covering the
interior-angle defect were written first and run against the unfixed code:

```text
total=156  passed=152  failed=4  result=Failed   (Unity exit code 2)

InteriorAngle_ReportsReflexAngleBeyond180   expected > 180, was 143.857086
InteriorAngle_SumsTo360ForAQuad             expected 360 +/- 0.1, was 287.714172
Room_RejectsReflexInteriorAngle             expected False, was True
Room_RejectsReflexInteriorAngleRegardlessOfWinding  expected False, was True
```

The remaining nine added tests passed on first run, as expected — they lock
behavior that was already correct but untested.

### Also executed outside Unity

- `inspect_snapshot.py` on all three fixtures: valid room **VALID** (exit 0),
  door+window **VALID** (exit 0), malformed **INVALID with exactly one problem**,
  `room height 5.20 m is outside 2.0-4.0 m` (exit 1). Unchanged by the fix.
- `inspect_snapshot.py` on the four constructed divergence cases: each now agrees
  with the C# validator.
- `send_fixture.py --dry-run` on all three fixtures, sizes as tabulated in
  protocol-v1.md section 7.
- `send_fixture.py` over **real TCP to a listener on 127.0.0.1:47831**: the
  listener received exactly 3 newline-delimited messages (177 / 1214 / 142
  bytes, 1533 total), parsed each as protocol v1, and asserted the trailing
  terminator was present, that documentation-only `_` keys had been stripped, and
  that the payload contained no `walls` key. Both processes exited 0.

## Known failures

None.

## Known issues

- **`tools/inspect_snapshot.py` is a standing parity risk.** It re-implements the
  shared validation rules in Python by design. This review found four
  divergences. Any change to a shared validation limit or rule must be mirrored
  in that script in the same commit. There is no automated check enforcing this.
- `RoomValidator.ValidateRoom` validates neither openings nor furniture. A
  consumer must additionally call `OpeningValidator.Validate` per opening and
  `FurnitureValidator.Validate` per object. Stated in protocol-v1.md section 4.
- `OpeningValidator`'s overlap check skips any other opening sharing the same
  `id`, so two openings that wrongly share an id will not be checked against each
  other. Unreachable while ids are GUID-derived; not fixed, recorded here.
- No physical-device testing applies to this workstream. Nothing here has run on
  an iPhone, and no AR code exists yet.

## Files most important to read next

1. `AGENTS.md`
2. `docs/contracts/scene-schema-v1.md` and `docs/contracts/protocol-v1.md`
3. `docs/status/scanner.md` or `docs/status/viewer.md` — your workstream.
4. `docs/plans/ghostmap-implementation-plan.md` section 16 (S1-S6) or 17 (V1-V6).
5. `shared/TestProject/README.md` — how to run the shared tests.

## Next task

**The foundation gate passes. Scanner and Viewer may proceed in parallel.**

- Scanner: **S1 — Scanner Unity project + physical-device AR smoke test.**
  S1 cannot be declared done on Editor behavior alone; it requires a real iPhone.
- Viewer: **V1 — Viewer project + TCP server + fixture ingestion.** V1 can be
  built entirely against `fixtures/` and `tools/send_fixture.py`. Use
  `SnapshotRevisionPolicy` rather than reimplementing revision arbitration, and
  run `OpeningValidator` and `FurnitureValidator` alongside `RoomValidator`
  before applying a snapshot.

Neither app may duplicate a shared type, and neither may change a shared contract
without following the procedure in implementation plan section 27.
