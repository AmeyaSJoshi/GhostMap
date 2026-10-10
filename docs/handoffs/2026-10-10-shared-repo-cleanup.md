# Handoff

## Branch
`claude/adoring-archimedes-kle6ph`

## Base commit
`f5a7d30` (merge of PR #13)

## Head commit
The commit that adds this file. Docs and tooling only.

## What changed

A repository-wide cleanup so the documents match the code and each one has a
single job. No C#, scene, package or project-settings file changed.

- **Status files** rewritten as short current-state pages with every stale
  line fixed ("there is no reset", "the Viewer has not been started", "no
  physical iPhone has been connected", a missing V6 commit sha). The full
  originals moved verbatim to `docs/archive/status-history/`.
- **Plan**: every section number kept (code comments cite them). Sections that
  duplicated `AGENTS.md`, the contracts or the architecture (5, 6, 7, 33) are
  now pointers; the completed task specs (15, 16, 17) and the self-review (34)
  moved verbatim to `docs/archive/plan-completed-tasks.md` with a summary table
  left in place; **As built** notes added where the code deliberately differs
  (3, 8.7, 10, 14). A status table sits at the top.
- **Spec** reduced to the product: removed the pre-implementation UML, CRC
  cards, class list and WebSocket event-message list that ADR-0001 to 0004
  superseded. Functional requirements now carry their real status.
- **Architecture overview**: added section 12 (code map of every runtime type)
  and 13 (threading and runtime model, known gaps against sections 6-7).
- **Contracts**: the schema doc now states the limits the validators already
  enforce (opening height and sill, furniture 0.05-5.0 m, partial-room
  validation). The protocol doc gained a non-normative implementation-status
  table. No contract changed.
- **New**: `docs/README.md` (documentation map), `docs/decisions/README.md`
  (ADR index), `docs/archive/README.md`, READMEs for `apps/scanner`,
  `apps/viewer`, `shared`, `fixtures`, `tools`, `.github/pull_request_template.md`
  (plan section 4.6), and `tools/run_unity_tests.sh` (adapted from the deleted
  `integration/sweep-and-furniture` branch; the scanner suite keeps
  `-buildTarget iOS`).
- `AGENTS.md`: numbered rules untouched; quick reference gained the docs map,
  archive and test-runner pointers.
- Removed seven leftover `.gitkeep` files from populated `Assets` folders.
- **Branches**: all 20 non-`main` branches on `origin` deleted. The 13 merged
  ones were fully contained in `main`. The 7 unmerged ones are preserved as
  `archive/<branch>` tags and described in `docs/archive/README.md`.

## Contract impact
None. Contract docs gained descriptions of existing behavior only.

## How to test
1. `python3 tools/inspect_snapshot.py fixtures/valid-room-v1.json` → valid.
2. `./tools/run_unity_tests.sh` on a machine with Unity 6000.3.24f1. Expected:
   shared 156, viewer 475, scanner 361, all passing (no code changed).

## Test results
- No Unity in the cleanup environment, so the C# suites were not run. No C#
  file changed.
- `inspect_snapshot.py`: two fixtures valid, the malformed one invalid on
  height only. `send_fixture.py --dry-run`: 176 / 1059 / 141 bytes as
  documented.
- `run_unity_tests.sh` exercised against a stand-in Unity binary: argument
  passing (iOS target on the scanner only), XML summary, failure exit codes,
  `--rebuild-scenes`, the missing-Unity message. `bash -n` clean.
- Every relative Markdown link in the live docs resolves.

## Known failures
None.

## Files most important to read next
- `docs/README.md`
- `docs/status/integration.md`
- plan section 18 (`I1`)

## Next task
- **I1 — real iPhone → Viewer live room.**
