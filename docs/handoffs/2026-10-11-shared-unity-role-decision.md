# Handoff

## Branch
`claude/adoring-archimedes-kle6ph`

## Base commit
`30cb4c9` (main after PR #27)

## Head commit
See the merge of this branch into `main`.

## What changed
- `CLAUDE.md`: new section "Where Unity fits, and why". The browser viewer
  replaces only the desktop Unity Viewer; the iPhone app stays a Unity app, and
  Unity is where the owner uses the exported `.glb` files. Records why the phone
  app is Unity (ADR-0001), why that reason weakened with ADR-0013, and the
  owner's decision: stay on Unity for now, revisit after `R2`.
- `docs/status/integration.md`: the decision added to "Open decisions for the
  owner", with the instruction to raise it again after `R2`.
- `docs/onboarding.md`: the same, as open question 6.

## Contract impact
- None.

## How to test
1. Docs only. Relative links checked.

## Test results
- No code changed; no tests run. Unity suites still blocked on the CI secrets
  (`docs/ci.md`).

## Known failures
- Unity CI jobs fail at the missing-secrets check until the owner adds them.

## Files most important to read next
- `CLAUDE.md`, `docs/status/integration.md`

## Next task
- Unchanged: owner adds CI secrets and runs `R2`; an agent can start `R8`.
