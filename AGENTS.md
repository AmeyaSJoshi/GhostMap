# GhostMap Repository Instructions

1. Read the project spec, implementation plan, shared contract docs, and your workstream status before modifying code.
2. Do not invent alternate scene schemas or network messages inside an app.
3. `shared/com.ghostmap.shared` is the only source of truth for domain models, geometry utilities, validation, and wire contracts.
4. During scanning, the scanner owns scene state. After finalization, the viewer owns editable scene state.
5. Protocol v1 sends full scene snapshots after structural changes. Do not replace it with delta/event replay without an ADR and contract tests.
6. Do not add LiDAR, Gaussian splatting, NeRFs, cloud inference, or automatic object recognition to the MVP unless all MVP acceptance tests already pass.
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
| Decisions | `docs/decisions/ADR-*.md` |

### Start of any work session

```bash
git status
git branch --show-current
git log -5 --oneline
git fetch origin
```

Then read `AGENTS.md`, `docs/status/shared.md`, your own workstream status file,
both contract documents, and the latest relevant handoff.

### Directory ownership

| Workstream | Owns |
| --- | --- |
| Scanner | `apps/scanner/**`, `docs/status/scanner.md` |
| Viewer | `apps/viewer/**`, `docs/status/viewer.md` |
| Shared / Integration | `shared/**`, `fixtures/**`, `docs/contracts/**`, `docs/decisions/**`, `docs/status/shared.md`, `docs/status/integration.md`, `tools/**` |

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

See `docs/handoffs/README.md` for the required template.

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
