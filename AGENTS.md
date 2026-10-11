# GhostMap Repository Instructions

1. Read the project spec, implementation plan, shared contract docs, and your workstream status before modifying code.
2. Do not invent alternate scene schemas or network messages inside an app.
3. `shared/com.ghostmap.shared` is the only source of truth for domain models, geometry utilities, validation, and wire contracts.
4. During scanning, the scanner owns scene state. After finalization, the scene is read-only and the phone's export bundle is the record; furniture is edited in Unity or by rescanning (`docs/decisions/ADR-0013-phone-export-and-browser-viewer.md`).
5. Protocol v1 sends full scene snapshots after structural changes. Do not replace it with delta/event replay without an ADR and contract tests.
6. All capture and recognition run on the phone. Do not add LiDAR, dense depth reconstruction, Gaussian splatting, NeRFs, generative 3D models, or any cloud or off-device inference. The only learned model allowed is the on-device YOLO-n furniture detector (`docs/decisions/ADR-0011-on-device-yolo-furniture-identification.md`); it may name objects but must never set geometry. Anything beyond that needs a new ADR (see `docs/decisions/ADR-0012-stand-in-place-on-device-capture.md`).
7. Do not edit another workstream's directory unless the task explicitly requires integration.
8. Shared contract changes require:
   - contract documentation update,
   - tests,
   - dedicated commit,
   - status/handoff update.
9. Never modify generated Unity folders (`Library`, `Temp`, `Logs`, `obj`, build output).
10. Do not upgrade Unity or package versions without a dedicated dependency-change PR.
11. Every bug fix must include a regression test when the bug is testable without a physical device.
12. Real-device behavior must never be declared fixed until verified on a real iPhone.
13. At the end of every meaningful task:
   - run tests,
   - update the workstream status,
   - create a handoff file,
   - commit.
14. GhostMap's primary transfer UX is one-button **Send to Computer** after
    finalization: the phone builds the export bundle (`room.html`, `room.glb`,
    one `.glb` per object, `scene.json`) and opens the iOS share sheet. Normal
    users never type IP addresses or ports. The TCP snapshot stream is a
    developer tool only. See `docs/decisions/ADR-0013-phone-export-and-browser-viewer.md`.

---

## Quick reference

These pointers are navigational only. They do not add, relax, or reinterpret any
rule above. Where this section and the numbered rules appear to differ, the
numbered rules win.

### Canonical documents

| Document | Path |
| --- | --- |
| Project spec | `docs/specs/ghostmap-project-spec.md` |
| Implementation plan | `docs/plans/ghostmap-implementation-plan.md` |
| Architecture overview | `docs/architecture/overview.md` |
| Scene schema v1 | `docs/contracts/scene-schema-v1.md` |
| Protocol v1 | `docs/contracts/protocol-v1.md` |
| Decisions | `docs/decisions/README.md` (index of `ADR-*.md`) |
| Workstream status | `docs/status/*.md` |
| Documentation map | `docs/README.md` |

### Start of any work session

```bash
git status
git branch --show-current
git log -5 --oneline
git fetch origin
```

Then read `AGENTS.md`, `docs/status/shared.md`, your own workstream status file,
both contract documents, and the latest relevant handoff.

### Running tests

```bash
./tools/run_unity_tests.sh            # shared, viewer and scanner EditMode suites
./tools/run_unity_tests.sh viewer     # one suite
```

See `tools/README.md` for the Python tools.

### Directory ownership

| Workstream | Owns |
| --- | --- |
| Scanner | `apps/scanner/**`, `docs/status/scanner.md` |
| Viewer | `apps/web-viewer/**`, `apps/viewer/**` (frozen, retiring), `docs/status/viewer.md` |
| Shared / Integration | `shared/**`, `fixtures/**`, `docs/contracts/**`, `docs/decisions/**`, `docs/research/**`, `docs/status/shared.md`, `docs/status/integration.md`, `tools/**` |

### Branch naming

```text
foundation/<task>
shared/<task>
scanner/<task>
viewer/<task>
integration/<task>
fix/<scope>-<description>
```

### Commit convention

```text
feat(scanner): ...
feat(viewer): ...
feat(shared): ...
fix(scanner): ...
fix(viewer): ...
test(shared): ...
docs(architecture): ...
chore(repo): ...
```

### Handoff files

Create a new file per handoff, never overwrite an existing one:

```text
docs/handoffs/YYYY-MM-DD-<workstream>-<short-description>.md
```

See `docs/handoffs/README.md` for the required template. Handoffs from before
2026-10-10 live in git history, not in the working tree.

### Definition of done

A task is done only when:

1. relevant automated tests pass;
2. no unrelated files changed;
3. the app opens without Console exceptions;
4. the status file is updated;
5. a handoff file is created;
6. a commit is created;
7. a physical test is performed if the task touches AR/device/network behavior;
8. `docs/contracts/**` is updated if an interface changed.
