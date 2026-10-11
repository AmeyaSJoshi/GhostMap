# Handoff

## Branch
`claude/adoring-archimedes-kle6ph`

## Base commit
`f5f144e` (main after PR #28)

## Head commit
See the merge of this branch into `main`.

## What changed
Put each piece of information where its readers look, so non-Claude agents and
people see it too:

- `AGENTS.md` (rules for every agent and contributor): rule 13 now covers every
  task, however small, and requires the new **End-of-task documentation
  checklist** section (moved from `CLAUDE.md`). New **rule 15**: the phone app
  stays Unity; switching to native Swift is the owner's call after `R2`, with a
  new ADR. Quick reference lists `CLAUDE.md`.
- `docs/onboarding.md`: holds the full "Where Unity fits, and why" explanation
  (moved from `CLAUDE.md`) at the end of section 1; the Stack bullet, open
  question 6 and section 9 "Process" point to it and to rule 13.
- `README.md` (for people): short "Where Unity fits" section.
- `CLAUDE.md`: now only the Claude-specific start steps and cloud-session
  facts, plus pointers to rules 13 and 15.
- `docs/status/integration.md`: the Unity decision now points to onboarding and
  rule 15.

Rule of thumb now: rules in `AGENTS.md`, explanation in `docs/onboarding.md`,
product summary in `README.md`, Claude-only setup in `CLAUDE.md`, live state in
`docs/status/`.

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
- `AGENTS.md`, `CLAUDE.md`, `docs/onboarding.md`

## Next task
- Unchanged: owner adds CI secrets and runs `R2`; an agent can start `R8`.
