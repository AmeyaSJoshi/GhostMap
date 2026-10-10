# Handoff

## Branch
`claude/adoring-archimedes-kle6ph`

## Base commit
`58e0a4a` (merge of PR #20, repository cleanup)

## Head commit
The commit that adds this file.

## What changed

The owner's product goal: walk around a room and get its dimensions, have the
app identify the things in it, and send a file to the computer with one button
for use in Unity. The seven unmerged branches were triaged against it.

| Branch | Verdict |
| --- | --- |
| `integration/sweep-and-furniture` (sweep capture ADR-0005, furniture detection and `.glb` export ADR-0006) | **Keep.** Serves "dimensions" and "identify things". Merge pending, see below |
| `scanner/sweep-wall-capture`, `integration/furniture-detect-and-export` | Delete. Fully contained in the branch above |
| `docs/one-button-computer-transfer` | **Keep. Merged here** as ADR-0007 |
| `feature/peer-to-peer-transport` | Delete. Its useful content (hotspot fallback, Bonjour discovery, the plist keys, not MultipeerConnectivity) is folded into ADR-0007 as candidate mechanisms |
| `feature/nl-asset-editing`, `feature/drag-drop-furniture` | Delete. Viewer-side editing is not part of the goal; drag-and-drop was never implemented |

Merged in this commit (docs only, the owner's own branch):

- `ADR-0007-one-button-computer-transfer.md`, renumbered from ADR-0005 to free
  0005/0006 for the sweep and detection ADRs, plus a "Candidate mechanisms"
  section from the peer-to-peer scoping.
- `AGENTS.md` rule 14 (one-button transfer is the primary UX).
- Plan: MVP promise items 9-10, section 18 `I1` split into `I1A` (typed-IP
  baseline) and `I1B` (Send to Computer), demo checklist note.
- Spec: primary workflow section, FR-12 live preview now optional, new FR-19
  Send to Computer (designed, not built), old FR-19 renumbered FR-20.
- Architecture overview section 6.1, README, integration status, ADR index.
- The original one-button handoff, with a renumbering note.

**Not merged: `integration/sweep-and-furniture`.** The code merged without
conflicts and the docs conflicts were resolved, but committing the merge was
refused by this session's permission policy (it integrates another
contributor's unreviewed code). The resolved status pages, handoff index and
test runner are saved outside the repo; redoing the merge reproduces them in a
few minutes. ADR-0005 and ADR-0006 are reserved in the ADR index.

## Contract impact
None. ADR-0007 notes that a Bonjour service-type string would be an additive
shared-contract change when `I1B` is built.

## How to test
Docs only. Every relative link resolves.

## Test results
No code changed.

## Known failures
None.

## Files most important to read next
- `docs/decisions/ADR-0007-one-button-computer-transfer.md`
- plan section 18

## Next task
1. Merge `integration/sweep-and-furniture` once the owner approves it.
2. Delete the five branches marked Delete above, plus the two merged ones.
3. Device session: sweep, detection, then `I1A`.
