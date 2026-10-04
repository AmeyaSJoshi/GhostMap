# Handoff

## Branch
`integration/quick-send-discovery`

## Base commit
`3427bae` (`hackathon/auto-room-scan`)

## Head commit
`484ab74` (application implementation; documentation follows in the handoff commit)

## What changed
- Cherry-picked PR #19's scope-only `ADR-0008` as `f57e51a`.
- Added the shared UDP discovery contract on port `47832`; it has one request
  and one response and carries no scene data.
- The Viewer now starts `ViewerDiscoveryResponder` with its TCP listener.
- The Scanner now has `PeerDiscoveryClient` and a post-finalization **Send to
  Computer** control. Discovery selects the first valid local Viewer and feeds
  its TCP endpoint to the existing `ScannerNetworkClient`, which resends the
  current finalized snapshot as part of its existing handshake.
- Regenerated the Scanner and Viewer scenes; manual address entry remains as a
  developer fallback.

## Contract impact
- Additive connection-setup contract only, documented in protocol-v1 Appendix
  A and `PeerDiscoveryProtocol`. Scene schema, protocol-v1 messages, snapshot
  framing, revisions, and authority rules are unchanged.

## How to test
1. Open GhostMap Viewer on the Mac.
2. On a real iPhone, complete a scan and finalize it.
3. Tap **Send to Computer**; do not enter an address.
4. Confirm the Viewer receives the final room and enables editing.
5. Repeat with the Mac connected to the iPhone hotspot.

## Test results
- Shared EditMode: **202 passed, 0 failed**.
- Viewer EditMode: **548 passed, 0 failed**.
- Scanner EditMode: **577 passed, 0 failed**.
- One prior full-suite scanner run had a transient failure in the pre-existing
  `ReconnectAfterFinalizationResendsTheFinalizedSnapshot` timing test (expected
  `Retrying`, observed `Connected` immediately after its forced socket close).
  An immediate isolated rerun passed all 577 tests; this change does not touch
  `ScannerNetworkClient` or that test, but the result is recorded rather than
  hidden.
- Scanner iOS project generation: passed.
- Unsigned iOS Xcode Release build: `BUILD SUCCEEDED`.
- No physical-device run yet.

## Known failures
- No automated failures.
- The existing reconnect timing test described above is a known intermittent
  test-harness concern; it is not a confirmed quick-send regression.
- UDP broadcast can be blocked by a network. It is supported on normal Wi-Fi
  and a hotspot only until a real-device test confirms both.
- This is not Bluetooth/AWDL peer-to-peer transport; `ADR-0008` Tier 2 remains
  unimplemented. There is no pairing, authentication, or receive
  acknowledgement, so the phone must not claim confirmed delivery merely from
  connecting.

## Files most important to read next
- `docs/decisions/ADR-0009-local-quick-send-discovery.md`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/Networking/PeerDiscoveryClient.cs`
- `apps/viewer/Assets/GhostMap/Viewer/Runtime/Networking/ViewerDiscoveryResponder.cs`
- `apps/scanner/Assets/GhostMap/Scanner/Runtime/UI/ScannerHudController.cs`

## Next task
- Perform and record the real iPhone-to-Mac quick-send test on Wi-Fi and a
  phone hotspot. If broadcast discovery fails in either environment, collect
  the on-screen status and evaluate Bonjour/native Network framework work as a
  separate decision.
