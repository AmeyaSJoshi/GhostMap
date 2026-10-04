# Integration Status

## Current state
- **Quick Send discovery is implemented but not device-verified.** PR #19's
  scope-only ADR was merged, then `ADR-0009` added a small UDP discovery layer
  above the existing TCP/full-snapshot path. After finalization, the Scanner
  exposes **Send to Computer**; it broadcasts a discovery request, connects to
  the first valid Viewer response, and the existing handshake resends the
  complete final snapshot. The Viewer starts its responder beside the TCP
  listener. Manual address entry remains a developer fallback.
  - Automated discovery/protocol tests pass: Shared **202/202**, Viewer
    **548/548**, Scanner **577/577**. The scanner iOS project generated and an
    unsigned `xcodebuild` completed successfully.
  - A final all-suite pass saw one transient failure in an existing scanner TCP
    reconnect timing test; its immediate isolated rerun passed all 577 tests.
    The quick-send change does not modify that TCP client or test.
  - This supports Wi-Fi and a phone hotspot. It does **not** implement
    Bluetooth/AWDL transport, pairing, authentication, or receiver
    acknowledgement; a device test is still required before describing it as
    working on a phone.
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
- **Physical quick-send run required.** With Viewer open on a Mac, finish a
  real iPhone scan, press **Send to Computer**, and verify that the final room
  appears and can be edited. Repeat on ordinary Wi-Fi and the iPhone hotspot;
  record every attempt below. Do not claim Bluetooth/AWDL support.

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
