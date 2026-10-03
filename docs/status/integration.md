# Integration Status

## Current state
- **Project state:** Scanner `S1`-`S6` are complete (verified on a physical
  iPhone) and Viewer `V1`-`V6` are complete. The project is entering
  integration.
- **Primary transfer requirement (`ADR-0005`, authoritative):** the user-facing
  flow is one-button **Send to Computer** after finalization, with automatic
  local discovery/pairing. A normal user must never type an IP address or port.
  `docs/decisions/ADR-0005-one-button-computer-transfer.md` is authoritative for
  this requirement; if any other document disagrees, ADR-0005 wins.
- **Manual IP connectivity** (the Scanner's S6 IP/port screen) remains useful as
  a **diagnostic baseline only** — that is what `I1A` is — not as the product
  workflow.
- Not started, but **unblocked**. Integration tasks `I1`–`I4` require `S3` + `V2`
  at minimum, and a full demo requires `S6` + `V6`. All of these are now
  complete: Scanner `S1`–`S6` (verified on a physical iPhone) and Viewer
  `V1`–`V6` (V6 merged to `main` in PR #12, merge commit `8f2b3f6`).
- No end-to-end iPhone→viewer run has been attempted.
- No accuracy benchmark has been recorded.

## Last verified commit
- None.

## Tests run
- None.

## Interfaces consumed
- None yet.

## Known issues
- Unity `6000.3.24f1` **is** installed on the current development machine and
  runs the shared EditMode suite; this line previously said otherwise.
- No physical iPhone has been connected to this project.

## Next safe task
- **Unblocked.** The Scanner and Viewer workstreams have both reached their
  gates (`S6` and `V6` complete).
- First integration task: **I1 — Real iPhone → computer transfer**, split into:
  - **I1A** — baseline end-to-end transport verification (manual IP allowed;
    diagnostic only).
  - **I1B** — one-button Send to Computer (discovery/pairing design and
    implementation; no manual IP for a normal user).
  Each must succeed three consecutive times before any polish work. I1 is not
  complete until I1B passes.
- Open design questions to settle at the start of I1B (not before I1A) are listed
  in ADR-0005: discovery mechanism, pairing/trust, delivery confirmation (protocol
  v1 has no viewer→scanner message), send-after-finalize behaviour, scanner-side
  persistence of a finalized scan, and multiple computers.

## Do not touch
- `apps/scanner/**` and `apps/viewer/**` during active workstream development,
  except on a short-lived, explicitly scoped integration branch.

---

## I1 run log

No runs yet. Record every attempt here, including failures. Track I1A (baseline transport) and I1B (one-button transfer) separately.

| Date | Result | Closure error | Notes |
| --- | --- | --- | --- |
| — | — | — | — |

## I2 accuracy benchmark

No scans yet. Targets: median wall absolute error ≤ 0.12 m, max normal wall error
≤ 0.20 m, closure ≤ 0.15 m, height error ≤ 0.15 m.

| Scan | Wall A err | Wall B err | Wall C err | Wall D err | Height err | Door width err | Closure err |
| --- | --- | --- | --- | --- | --- | --- | --- |
| — | — | — | — | — | — | — | — |
