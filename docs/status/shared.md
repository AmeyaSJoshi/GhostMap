# Shared Status

Workstream: Shared / Integration. Owns `shared/**`, `fixtures/**`, `tools/**`,
`docs/contracts/**`, `docs/decisions/**`, this file and `integration.md`.
Full history (F0-F4 detail and the F4 review findings):
`git show f5a7d30:docs/status/shared.md`.

## Current state

**F0-F4 complete. Scene schema v1 and protocol v1 are frozen. Task `R6` added
`Runtime/Export/` and completed the export bundle v1 contract.** Task `R1`
imported three additive pieces from `GhostMapDublinHacks`: `WallFitting`
(ADR-0005), `RoomGeometry.ContainsPointXZ` (ADR-0006) and
`PeerDiscoveryProtocol` (UDP 47832, ADR-0009; superseded by ADR-0013 and removed
in `R10`).

| Area | Contents |
| --- | --- |
| `Runtime/Domain` | `Vec3Dto`, `CornerModel`, `OpeningModel`, `SceneObjectModel`, `RoomModel`, `SceneSnapshot`, `ValidationResult`, `WallDefinition` |
| `Runtime/Geometry` | `GhostCoordinateFrame`, `RayPlaneMath`, `RoomGeometry` (incl. `ContainsPointXZ`), `WallGeometry`, `WallFitting`, `MeasurementMath` |
| `Runtime/Validation` | `RoomValidator`, `OpeningValidator`, `FurnitureValidator`, `ClosureQuality` |
| `Runtime/Protocol` | `ProtocolConstants`, wire messages, `ProtocolSerializer`, `SnapshotRevisionPolicy`, `PeerDiscoveryProtocol` (dormant) |
| `Runtime/Export` (task `R6`) | `GlbWriter` (`TryExportObject`, `TryExportRoom`), `RoomPalette`, `FurnitureParts` (part table and colours), `WallSliceGenerator`, `WallLayout`, `FootprintMesh`. Moved from the Viewer so the phone exports the same geometry the Viewer renders; contract `docs/contracts/export-bundle-v1.md` |
| `fixtures/` | Three canonical snapshots (valid, door + window, intentionally malformed) |
| `tools/` | `inspect_snapshot.py`, `send_fixture.py`, `run_unity_tests.sh` |
| `shared/TestProject` | Host project whose only job is running the shared EditMode suite |

The tag `shared-v1-ready` marks the frozen foundation baseline.

## Last verified commit

- Shared sources: `0b7a106` from `GhostMapDublinHacks`, merged in task `R1`.
  The frozen contract code was last changed in `47a6a0a` (F4). EditMode suite
  passes at `69af8eb`.
- `Runtime/Export/`: branch `integration/r6-export-bundle` (task `R6`), suite
  292/292 on the owner's Mac, 2026-10-11.
- `SceneObjectModel.label` (task `R5`, additive schema v1 change): branch
  `shared/r5-detector-label`, suite 303/303 on the owner's Mac, 2026-10-11.

## Tests run

```bash
./tools/run_unity_tests.sh shared
python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json
```

Last recorded Unity result, in `GhostMapDublinHacks`: **202 tests, 202 passed**
(156 foundation + 32 `WallFittingTests` + 9 `RoomContainmentTests` + 5 discovery
tests), Unity 6000.3.24f1, Test Framework 1.6.0. **Confirmed in this repository
on 2026-10-11 at `69af8eb`: 202/202**, 0 failed (owner's Mac). **After `R6`: 292 tests, 292
passed** (+69 moved from the Viewer, +21 new export tests). **After `R5`: 303** (+11 label
tests: round trip, absent in older JSON, 64-character limit, control
characters). On 2026-10-10 `inspect_snapshot.py` reported the two valid
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

- `GlbWriter` writes accessor `min`/`max` with round-trip precision. Six
  decimals once broke the glTF rule that they equal the data exactly (found by
  the Khronos validator on a rotated wall; regression test
  `DeclaredBounds_EqualTheDataExactly`).

- `tools/inspect_snapshot.py` re-implements the validation rules in Python.
  **Any change to a shared limit or rule must be mirrored there in the same
  commit.** The F4 review found four divergences.
- `RoomValidator.ValidateRoom` does not check openings or furniture. Callers
  must also run `OpeningValidator.Validate` per opening and
  `FurnitureValidator.Validate` per object (the Viewer does this in
  `SceneSnapshotValidator`).
- `shared/TestProject/` is not in the plan's original layout. It exists because
  a Unity package cannot run its own tests; see its README.
- `WallFitting`'s gates (8 samples, 0.40 m span, 5° between walls) are
  capture-time guards, not serialized rules, so `inspect_snapshot.py`
  deliberately does not mirror them.
- The fixtures are hand-written and unusually tidy (integer corners, catalog
  dimensions, 0/90/180 degree yaw). A snapshot captured off the wire during
  `R2` would make a more realistic fixture.

## Next safe task

- `R5` is built: optional `SceneObjectModel.label` (at most 64 characters, no
  control characters), contract doc section 5 and 9, `inspect_snapshot.py`
  checks and prints it, `room-with-door-window-v1.json`'s desk carries
  `"label": "dining table"`.
- `R6` is built (`Runtime/Export/`, contract complete). Next shared work is
  removing `PeerDiscoveryProtocol` in `R10`.

Roadmap progress is tracked in `docs/status/integration.md`.

## Do not touch

`apps/scanner/**` (Scanner) and `apps/viewer/**` (Viewer), except on a
short-lived, explicitly scoped integration branch.
