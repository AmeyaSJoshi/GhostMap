# Handoff

## Branch
`claude/adoring-archimedes-kle6ph`

## Base commit
`e24d475` (main after PR #24, `R1` merged)

## Head commit
See the merge of this branch into `main`.

## What changed
- **CI.** `.github/workflows/tests.yml`: a `tools` job (Python fixture checks,
  `send_fixture.py --dry-run`, `bash -n` on the runner) and a `unity` matrix
  (shared, viewer, scanner EditMode) using GameCI `unity-test-runner@v4.3.2`
  and Unity `6000.3.24f1`. `docs/ci.md` explains the three secrets the owner
  must add and what to do if licence activation fails.
- **Onboarding.** `docs/onboarding.md`: the whole project for a newcomer with no
  chat history: state, environment limits, repo tour, the scan flow traced
  through the code, invariants, gotchas from past sessions, testing, process,
  history, open questions, glossary.
- **Agent entry point.** `CLAUDE.md` at the root points to `AGENTS.md`, the
  onboarding guide, the integration tracker and the task briefs, and states
  what an agent container can and cannot do.
- **Task briefs.** `docs/tasks/README.md` plus `R2`-`R10`, each naming the files
  to touch, the existing code to reuse (checked against the source), tests,
  owner-only steps, and "done when".
- **Corrections found while writing the briefs:** per-object `.glb` files have
  no node transform (the `GlbExporter.TryExportObject` comment says yaw is on
  the node; `Build` writes only `mesh` and `name`). `R6` and `R8` say how to
  handle it.
- Doc maps updated: `README.md`, `docs/README.md`, the `AGENTS.md` quick
  reference, `docs/status/integration.md`.

## Contract impact
- None. `R8` proposes the `room.html` data placeholder for
  `docs/contracts/export-bundle-v1.md`, to be written by `R6`/`R8`.

## How to test
1. `python3 tools/inspect_snapshot.py --quiet fixtures/valid-room-v1.json`
   (and the other two fixtures; the malformed one must fail).
2. Owner: add the secrets per `docs/ci.md`, then **Actions → tests → Run
   workflow**.

## Test results
- Unity suites: **not run.** The agent container has no Unity, its network
  policy blocks Unity's hosts, and Unity needs the owner's licence. This is why
  CI was added. Recorded in `docs/status/integration.md`.
- Python tools job checks: passed locally in the container and in GitHub
  Actions run 1. The three Unity jobs in that run stopped at the missing-secrets
  check, as designed. Run 1 also showed the artifact upload erroring when the
  test step was skipped; fixed by skipping the upload in that case.

## Known failures
- The `unity` CI jobs fail with "Unity licence secrets are missing" until the
  owner adds `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`.

## Files most important to read next
- `CLAUDE.md`, `AGENTS.md`, `docs/onboarding.md`
- `docs/status/integration.md` ("Next safe task")
- `docs/tasks/README.md`

## Next task
- Owner: add the CI secrets and run the workflow; record 202 / 548 / 577.
- Owner: `R2` on the iPhone (`docs/tasks/R2-first-device-session.md`).
- Agent, in parallel: `R8` (no Unity or iPhone needed), then code for `R6`/`R4`.
