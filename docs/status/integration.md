# Integration Status

## Current state
- Not started. Integration tasks `I1`–`I4` require `S3` + `V2` at minimum, and a
  full demo requires `S6` + `V6`. None of these exist yet.
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
- **Blocked.** Integration begins only after the Scanner and Viewer workstreams
  reach their gates.
- First integration task: **I1 — Real iPhone → viewer live room**, which must
  succeed three consecutive times before any polish work.

## Do not touch
- `apps/scanner/**` and `apps/viewer/**` during active workstream development,
  except on a short-lived, explicitly scoped integration branch.

---

## I1 run log

No runs yet. Record every attempt here, including failures.

| Date | Result | Closure error | Notes |
| --- | --- | --- | --- |
| — | — | — | — |

## I2 accuracy benchmark

No scans yet. Targets: median wall absolute error ≤ 0.12 m, max normal wall error
≤ 0.20 m, closure ≤ 0.15 m, height error ≤ 0.15 m.

| Scan | Wall A err | Wall B err | Wall C err | Wall D err | Height err | Door width err | Closure err |
| --- | --- | --- | --- | --- | --- | --- | --- |
| — | — | — | — | — | — | — | — |
