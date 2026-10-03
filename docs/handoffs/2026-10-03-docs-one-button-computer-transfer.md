# Handoff

## Branch
`docs/one-button-computer-transfer`

## Base commit
`f5a7d30` (main: Scanner S1-S6, Viewer V1-V6, status docs refresh)

## What changed
Documentation / architecture decision only. Added a new product requirement
before Integration I1 begins.

### Why
The Scanner's S6 screen and the old I1 plan assume the user types a laptop IP
and port and connects before scanning. That proves the transport but is not an
acceptable product. The product goal is: "Press one button and the GhostMap
appears on your computer."

### Exact product requirement (`ADR-0005`)
```text
SCAN -> FINALIZE -> [SEND TO COMPUTER] -> automatic find/connect
     -> complete scene transferred -> computer reconstructs room -> phone shows success
```
- A normal user never types an IP address or port.
- Discovery/pairing is connection setup layered **above** the existing TCP +
  full-`SceneSnapshot` transport; it does not replace it.
- First use may need a tiny pairing step (pick a discovered computer and/or QR);
  the computer is then remembered.
- Manual IP is a developer/debug fallback only. No cloud, account or internet.
- Receiver is a "computer", not a Mac; Windows intended but not claimed until
  built and tested.
- Live streaming retained but not required for the standard workflow.

### Files changed
- `docs/decisions/ADR-0005-one-button-computer-transfer.md` — **new**.
- `AGENTS.md` — new rule 14 (mirrored into the plan's embedded AGENTS copy).
- `docs/specs/ghostmap-project-spec.md` — Primary User Workflow section, FR-19
  (Send to Computer), FR-12 reframed as optional live preview, computer/Windows
  wording, assumptions.
- `docs/architecture/overview.md` — section 6.1 layering; topology says COMPUTER.
- `docs/plans/ghostmap-implementation-plan.md` — I1 split into I1A (baseline
  transport) and I1B (one-button transfer); MVP promise items 9-10; S6 screen
  marked developer/debug; demo-day IP note.
- `docs/status/integration.md`, `README.md`, this handoff and the handoffs index.

### Stale contradictions corrected
- Spec still described a **WebSocket** transport and per-mutation event messages
  (`CORNER_ADDED`, `OBJECT_UPDATED`, ...) in the component diagram, sequence
  diagram, deployment diagram, build steps and section 14. These contradicted the
  frozen TCP / full-snapshot protocol (ADR-0002) and were replaced.
- Spec/plan wording that made real-time streaming and manual-IP connection the
  normal workflow was reframed.

## Intentionally undecided
Discovery mechanism and its platform cost; pairing/trust and listener
authentication; delivery confirmation; send-after-finalize behaviour; scanner
side persistence of a finalized scan; multiple computers. All listed in
ADR-0005 and deferred to the start of I1B.

## Impact on I1
I1 is now I1A (baseline transport, manual IP allowed, diagnostic) then I1B
(one-button transfer). I1 is not complete until I1B passes three consecutive
times. I1B must start with a design step; nothing here promises a technology.

## Flagged for I1B (not a documented contradiction, so no contract change made)
Protocol v1 is Scanner -> Viewer only. "The phone shows success" needs the phone
to learn the computer actually received the room, which would require an additive
viewer->scanner acknowledgment, i.e. a shared-contract change via `AGENTS.md`
rule 8. Not made here.

## Contract impact
**None.** No production code, `shared/**`, `fixtures/**`, `docs/contracts/**`,
`apps/scanner/**` or `apps/viewer/**` changes. Protocol v1 and the scene schema
are unchanged. No dependencies added.

## How to test
Not applicable (docs only).

## Next task
Start **I1A** (baseline) and then the **I1B** design step. Not started.
