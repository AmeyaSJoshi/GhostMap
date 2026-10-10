# Shared Status

Workstream: Shared / Integration. Owns `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**`, `docs/decisions/**`, this file and `integration.md`.
Full history (F0-F4 detail and the F4 review findings):
`git show f5a7d30:docs/status/shared.md`.

## Current state

**F0-F4 complete. Scene schema v1 and protocol v1 are frozen.** Neither app
workstream has needed a contract change.

| Area | Contents |
| --- | --- |
| `Runtime/Domain` | `Vec3Dto`, `CornerModel`, `OpeningModel`, `SceneObjectModel`, `RoomModel`, `SceneSnapshot`, `ValidationResult`, `WallDefinition` |
| `Runtime/Geometry` | `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry`, `WallGeometry`, `MeasurementMath` |
| `Runtime/Validation` | `RoomValidator`, `OpeningValidator`, `FurnitureValidator`, `ClosureQuality` |
| `Runtime/Protocol` | `ProtocolConstants`, wire messages, `ProtocolSerializer`, `SnapshotRevisionPolicy` |
| `fixtures/` | Three canonical snapshots (valid, door + window, intentionally malformed) |
| `tools/` | `inspect_snapshot.py`, `send_fixture.py`, `run_unity_tests.sh` |
| `shared/TestProject` | Host project whose only job is running the shared EditMode suite |

The tag `shared-v1-ready` marks the frozen foundation baseline.

## Last verified commit

- Shared sources: `47a6a0a` (F4 review fix: true interior angles, validator
  parity). Nothing under `shared/**` has changed since.

## Tests run

```bash
./tools/run_unity_tests.sh shared
python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json
```

Last recorded Unity result: **156 tests, 156 passed** (Unity 6000.3.24f1, Test
Framework 1.6.0). On 2026-10-10 `inspect_snapshot.py` reported the two valid
fixtures valid and the malformed one invalid on the height rule alone, and
`send_fixture.py --dry-run` produced the documented 176 / 1059 / 141-byte
messages.

## Interfaces consumed

None. The package depends only on `UnityEngine` (`Vector3`, `Plane`, `Ray`,
`JsonUtility`).

## Interfaces published

See `docs/contracts/scene-schema-v1.md`, `docs/contracts/protocol-v1.md` and
the architecture overview's code map.

## Known issues

- `tools/inspect_snapshot.py` re-implements the validation rules in Python.
  **Any change to a shared limit or rule must be mirrored there in the same
  commit.** The F4 review found four divergences.
- `RoomValidator.ValidateRoom` does not check openings or furniture. Callers
  must also run `OpeningValidator.Validate` per opening and
  `FurnitureValidator.Validate` per object (the Viewer does this in
  `SceneSnapshotValidator`).
- `shared/TestProject/` is not in the plan's original layout. It exists because
  a Unity package cannot run its own tests; see its README.
- The fixtures are hand-written and unusually tidy (integer corners, catalog
  dimensions, 0/90/180 degree yaw). A snapshot captured off the wire during
  `R2` would make a more realistic fixture.

## Next safe task

`R5`: add the optional `label` field to `SceneObjectModel` (additive schema v1
change, ADR-0011) with contract doc, tests, `inspect_snapshot.py` and fixture
updates. `R1` also lands shared work from `GhostMapDublinHacks`: `WallFitting`,
`RoomGeometry.ContainsPointXZ` and the discovery contract (`PeerDiscoveryProtocol`,
UDP 47832). Roadmap progress is tracked in `docs/status/integration.md`.

## Do not touch

`apps/scanner/**` (Scanner) and `apps/viewer/**` (Viewer), except on a
short-lived, explicitly scoped integration branch.
