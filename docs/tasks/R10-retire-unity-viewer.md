# R10: Retire the Unity Viewer

**Decision:** ADR-0013 section 5. **Workstreams:** Viewer (deletes
`apps/viewer/`), Shared (removes the discovery contract), Scanner (removes the
discovery client), Integration (runner, CI, docs).
**Depends on:** the plan's section 23 acceptance test passing with `room.html`.
**Do not start early.** Until then the Viewer is frozen (bug fixes only) and
must keep compiling, because `R6` moves its exporters out but leaves it using
them.

**Who:** an agent; CI confirms the remaining suites.

## Before deleting, confirm

1. `docs/status/integration.md` records a passing section 23 acceptance test,
   with date and build.
2. `R6` has moved `GlbExporter`, `FurnitureFactory.BuildParts`/`ColorFor`,
   `WallSliceGenerator`, the pure part of `WallRenderer` and floor/ceiling
   triangulation into `shared/com.ghostmap.shared/Runtime/Export/`, with tests.
   `grep -rn "GhostMap.Viewer" shared apps/scanner` returns nothing.
3. Nothing else in the Viewer is wanted. Its features (editing, inspector,
   saved snapshots, live TCP view, UDP discovery responder) are all superseded
   by ADR-0013. If the owner still wants any of them, stop and ask.

## Decide the developer TCP stream (needs an ADR)

After the Viewer is gone, **nothing receives** the scanner's protocol v1
stream: the only receiver is the Viewer, and `tools/send_fixture.py` is a
sender (it plays the scanner's role). Two options:

| Option | Cost | Recommendation |
| --- | --- | --- |
| Remove the stream: `ScannerNetworkClient`, `ScannerSnapshotPublisher`, `ISnapshotSink`, the HUD's connect UI, `send_fixture.py`, the protocol tests, and the local-network usage string | Smaller app, no network permission prompt, fewer tests | **Recommended** unless the owner uses live streaming |
| Keep it, with a tiny Python receiver in `tools/` that prints snapshots (reuse `inspect_snapshot.py` checks) | A dev tool nobody may use, and protocol v1 stays a maintained contract | Only if the owner asks |

Either way, write `docs/decisions/ADR-0014-...md` (next free number; check
`docs/decisions/README.md`). Removing protocol v1 also changes `AGENTS.md` rule
5 and the "Protocol v1" row in the quick reference; the ADR must say so, and
`docs/contracts/protocol-v1.md` is marked superseded rather than deleted.

## Remove

| What | Where | Note |
| --- | --- | --- |
| The Unity Viewer project | `apps/viewer/` (whole folder, including its tests) | ~475 tests go with it |
| UDP discovery contract | `shared/com.ghostmap.shared/Runtime/Protocol/PeerDiscoveryProtocol.cs`, `Tests/Editor/PeerDiscoveryProtocolTests.cs` | Shared change, rule 8: dedicated commit, `protocol-v1.md` updated, `docs/status/shared.md` updated |
| UDP discovery client | `apps/scanner/.../Runtime/Networking/PeerDiscoveryClient.cs`, `Tests/EditMode/PeerDiscoveryClientTests.cs`, its use in `ScannerHudController` (`discovery` field, Send to Computer state) | `R7` may already have removed the HUD use when it replaced `OnSendToComputerPressed` |
| TCP stream (if the ADR removes it) | See the table above | Also remove `NSLocalNetworkUsageDescription` from `Editor/ScannerIosPostBuild.cs`; with both discovery and TCP gone, nothing needs it |

Delete `.meta` files with their assets. Never touch generated folders
(`Library`, `Temp`, `Logs`, `obj`).

## Update

| File | Change |
| --- | --- |
| `tools/run_unity_tests.sh` | Drop the `viewer` suite (default list, `viewer)` case, the scene-regeneration loop for `Build Viewer Scene`) |
| `.github/workflows/tests.yml` | Drop the `viewer` matrix entry and update the expected counts comment |
| `docs/ci.md` | Job table and expected counts |
| `AGENTS.md` | Quick reference: ownership row for Viewer becomes `apps/web-viewer/**` only; rule 5 if protocol v1 goes |
| `README.md`, `docs/README.md`, `docs/architecture/overview.md`, `docs/specs/ghostmap-project-spec.md` | Remove the Unity Viewer from the structure and "what is where" tables |
| `docs/onboarding.md`, `CLAUDE.md` | Repo tour, suite counts, environment notes |
| `docs/plans/ghostmap-implementation-plan.md` | Mark `R10` done; leave the legacy Viewer sections with their "Legacy (ADR-0013)" banner |
| `docs/decisions/ADR-0001-two-unity-projects.md` | Status: superseded by ADR-0013 (one Unity project + web viewer) |
| `docs/status/viewer.md` | Now covers `apps/web-viewer/` only |
| `fixtures/README.md`, `tools/README.md`, `shared/TestProject/README.md` | Remove Viewer instructions |

`grep -rn "apps/viewer\|Unity Viewer\|PeerDiscovery\|47832" .` should then
return only ADRs, the plan's legacy sections, and handoffs.

## Done when

CI is green with the shared and scanner suites (record the new counts in
`docs/status/integration.md`); the owner has rebuilt the scanner to the iPhone
and run one scan and one Send to Computer; the ADR, status pages and a handoff
are written.
