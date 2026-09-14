# Handoff

## Branch
`scanner/s6-network-finalization`

## Base commit
`f087aaa` (S6 on-screen diagnostics, `docs(handoffs): record S6 diagnostics commit sha` = `640d51d`)

## Head commit
`884d90b`

## What changed
No code changes. This handoff records the physical-device verification that
closes S6, run against the build at `f087aaa` (unchanged since).

The device pass used a purpose-built local TCP listener/verifier (a
standalone Python script, not committed to the repo — equivalent in spirit
to `nc -l 47831` but with live automatic parsing/verification of every
NDJSON message) bound to the laptop's LAN IP on port 47831.

- `docs/status/scanner.md`: adds "Physical-device verification — S6, passed"
  with the full record, updates "Current state", "Last verified commit",
  the S6 "Tests run" entry, and "Next safe task" to reflect S1-S6 as
  complete.

## Contract impact
- None.

## How to test
Already run — see Test results. To repeat: any TCP listener on port 47831
that logs raw lines (`nc -l 47831` is sufficient for a manual read) will do;
the verification described here additionally parsed and cross-checked every
message automatically.

## Test results
Observed on a real iPhone against the build from `f087aaa`, connected over
Wi-Fi to a real TCP listener on the laptop:

- **Connection**: real TCP connection accepted.
- **`hello`**: received on every connect/reconnect (6 total), correct
  `sessionId`/`appVersion`/`deviceName` each time.
- **`scene.snapshot`**: received after every structural mutation (floor
  lock, all four corners, two undos, closure at **0.027 m — Excellent**,
  height at **2.58 m**, one opening, `Finish Openings`, `Finish Objects`) —
  22 snapshots across two sessions, room data always matching what was
  actually captured.
- **Heartbeat**: observed during an idle gap, consistent with the 2 s
  interval.
- **Monotonic revisions**: strictly non-decreasing within each session
  across the entire test — `[0,0,0,1..10]` then, after an in-scan Reset
  correctly started a fresh session at revision 0, `[0,1..12,12,12,12]` (the
  trailing duplicates are three post-finalize reconnects correctly resending
  the same revision, not a regression). Zero violations. This held through
  three unplanned early reconnects (likely iOS's local-network permission
  prompt) and one deliberate reconnect test.
- **Finalization**: **Finalize GhostMap** was visible, interactable, and
  worked — produced the final `scene.snapshot` (`finalized:true`,
  `scanPhase:"Finalized"`, revision 12) immediately followed by
  `scan.finalized` (`finalRevision:12`), correct order, matching revision.
  This also resolves the prior handoff's open question: a first device
  attempt on a stale build showed no Finalize button; this run, on a
  guaranteed-fresh build, showed the button working correctly — confirming
  the stale-build hypothesis rather than a code defect.
- **Reconnect / latest-snapshot resend**: the listener was stopped and
  restarted once, mid-`Finalized`. The scanner reconnected on its own and
  resent `hello` then the exact same finalized snapshot (same session, same
  revision 12, `finalized:true`) on all three connection attempts before
  settling.
- Scanner EditMode: **361/361**. Shared standalone: **156/156**.
  `xcodebuild ... Release -sdk iphoneos CODE_SIGNING_ALLOWED=NO`: **BUILD
  SUCCEEDED**.

See `docs/status/scanner.md`'s "Physical-device verification — S6, passed"
for the complete record, including what this pass did not cover (a window,
an actual phone-side Wi-Fi toggle, malformed/oversized input, a non-golden
closure band).

## Known failures
- None. The prior handoff's open question (no Finalize button on a first
  device attempt) is resolved: confirmed stale build, not a code defect.

## Files most important to read next
- `docs/status/scanner.md` — "Physical-device verification — S6, passed".

## Next task
- **None in the Scanner workstream.** S1-S6 are complete and verified on a
  physical iPhone. The next stage is Integration (`I1`-`I4`), which needs
  the Viewer workstream (`V1`-`V6`, not started) — do not begin Viewer or
  Integration work from here.
