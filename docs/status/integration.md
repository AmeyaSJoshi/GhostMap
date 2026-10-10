# Integration Status

Workstream: Shared / Integration. Tasks `I1`-`I4` are defined in plan section 18.

## Current state

**Not started, unblocked.** Scanner S1-S6 (device-verified) and Viewer V1-V6
are both complete, which is the plan's gate for full integration.

- The scanner has never talked to the Viewer. Its S6 device test used a Python
  TCP listener.
- No accuracy benchmark has been recorded.
- The MVP acceptance test (plan section 23) has not been run.

## Last verified commit

None.

## Tests run

None yet.

## Known risks going into I1-I3

These come from reading the code, not from a failed run.

- **Viewer liveness.** The Viewer has no heartbeat timeout or socket read
  timeout. A phone that leaves Wi-Fi without closing the socket leaves the
  Viewer HUD on `Connected`. Relevant to `I1` step 16 and the `I3` Wi-Fi test.
- **Finalized while disconnected.** If the scanner finalizes with no connection,
  the reconnect sends `hello` and the finalized snapshot but no
  `scan.finalized`. The Viewer does not need that message today.
- **Manual IP.** The phone needs the laptop's LAN IP typed in. Practise this and
  confirm iOS local-network permission before demo day.

## Next safe task

**I1 — real iPhone to Viewer live room.** Follow plan section 18. Record every
attempt below, failures included. It must pass three consecutive times before
any polish work.

## Do not touch

`apps/scanner/**` and `apps/viewer/**`, except on a short-lived, explicitly
scoped `integration/<task>` branch.

---

## I1 run log

| Date | Result | Closure error | Notes |
| --- | --- | --- | --- |
| — | — | — | — |

## I2 accuracy benchmark

Targets: median wall absolute error ≤ 0.12 m, max normal wall error ≤ 0.20 m,
closure ≤ 0.15 m, height error ≤ 0.15 m.

| Scan | Wall A err | Wall B err | Wall C err | Wall D err | Height err | Door width err | Closure err |
| --- | --- | --- | --- | --- | --- | --- | --- |
| — | — | — | — | — | — | — | — |
