# Handoff

## Branch
`integration/quick-send-discovery`

## Base commit
`f57e51a` (PR #19 scope ADR merged)

## Head commit
`268e19c` plus `43af6dd` Unity asset metadata

## What changed
- Added `PeerDiscoveryProtocol` to the shared package: UDP port `47832`, one
  discovery request token, a port/name response, and safe parser/formatter
  helpers.
- Documented the additive exchange in protocol-v1's Viewer discovery section.
- Added parser/round-trip tests.

## Contract impact
- Additive, connection setup only. No `SceneSnapshot`, protocol-v1 TCP message,
  framing, port 47831 behavior, schema version, or revision policy changed.

## How to test
1. Run `./tools/run_unity_tests.sh shared`.

## Test results
- Shared EditMode: **202 passed, 0 failed, 0 skipped**.

## Known failures
- None in automated tests. Real iPhone-to-Mac discovery remains unverified.

## Files most important to read next
- `shared/com.ghostmap.shared/Runtime/Protocol/PeerDiscoveryProtocol.cs`
- `docs/contracts/protocol-v1.md`

## Next task
- Complete the integration physical-device quick-send test documented in the
  integration handoff.
