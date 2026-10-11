# Handoff

## Branch
`integration/r1-test-confirmation`

## Base commit
`69af8eb` (main after PR #29)

## Head commit
The commit that adds this file. Documentation only.

## What changed
- Ran the three Unity EditMode suites in this repository for the first time
  since `R1` merged `GhostMapDublinHacks`. All pass with the expected counts.
- Recorded the result everywhere that still said the run was pending:
  `docs/status/integration.md` (current state, R1 row, last verified commit,
  tests run, open decision 2, next safe task), `docs/status/scanner.md`,
  `viewer.md`, `shared.md` (last verified commit, tests run),
  `docs/onboarding.md` section 2, `README.md` status table, plan "Where the
  project is" and Task R1, `docs/tasks/README.md` R1 row.
- "Next safe task" now puts `R2` (owner) and `R8` (agent) first; the CI secrets
  move to "when convenient", since local runs on the Mac cover the gap.

## Contract impact
- None.

## How to test
1. On a Mac with Unity 6000.3.24f1: `./tools/run_unity_tests.sh`.
2. `python3 tools/inspect_snapshot.py --quiet fixtures/<each fixture>`.

## Test results
- Owner's Mac, Unity 6000.3.24f1, `./tools/run_unity_tests.sh` at `69af8eb`:
  - shared: 202 tests, 202 passed, 0 failed, 0 skipped
  - viewer: 548 tests, 548 passed, 0 failed, 0 skipped
  - scanner: 577 tests, 577 passed, 0 failed, 0 skipped
- Python checks: `valid-room-v1.json` and `room-with-door-window-v1.json`
  valid; `malformed-room-v1.json` invalid on the height rule only;
  `send_fixture.py --dry-run` and `bash -n tools/run_unity_tests.sh` pass.
- The test runs changed no tracked files. The iOS build was not repeated.

## Known failures
- None in the automated suites.
- CI's Unity jobs still stop at the missing licence secrets (`docs/ci.md`).

## Files most important to read next
- `docs/status/integration.md`
- `docs/tasks/R2-first-device-session.md`, `docs/tasks/R8-browser-viewer.md`

## Next task
- Owner: `R2` on the iPhone.
- Agent: `R8`, the browser viewer.
