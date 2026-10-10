# Archive

History that is no longer the current description of the project but is kept
for reference. Nothing here is normative. For the current state read
`docs/status/`, `docs/architecture/overview.md` and the contracts.

## Files

| File | What it is |
| --- | --- |
| [`status-history/scanner-through-2026-10-03.md`](status-history/scanner-through-2026-10-03.md) | Full Scanner status through S6: per-task design notes, every device-test record, the S1 root-cause analyses, all test-run logs |
| [`status-history/viewer-through-2026-10-03.md`](status-history/viewer-through-2026-10-03.md) | Full Viewer status through V6: per-task design notes, test breakdowns, visual verification |
| [`status-history/shared-through-2026-10-03.md`](status-history/shared-through-2026-10-03.md) | Shared status through F4, including the published-interface list |
| [`status-history/integration-through-2026-10-03.md`](status-history/integration-through-2026-10-03.md) | Integration status before the 2026-10-10 cleanup |
| [`plan-completed-tasks.md`](plan-completed-tasks.md) | Implementation plan sections 15-17 (task specs F0-F3, S1-S6, V1-V6) and 34 (self-review), verbatim |

Per-task handoffs stay in `docs/handoffs/`; they are never moved or edited.

## Removed branches

On 2026-10-10 every branch on `origin` except `main` was deleted.

**Merged branches** (all commits already on `main`): `scanner/s1` to `s6`,
`viewer/v1` to `v6`, `shared/status-docs-refresh`. Nothing was lost.

**Unmerged branches.** None of this work is on `main`, none of it was built for
iOS or run on a phone, and two branches each defined a different ADR-0005. The
tip of each is preserved as a tag, so any of it can be restored with
`git checkout -b <name> archive/<branch>`.

| Tag | Tip | Author | Contents |
| --- | --- | --- | --- |
| `archive/scanner/sweep-wall-capture` | `fe3265f` | krishangnaikar | "ADR-0005": capture walls by sweeping the floor junction and fitting lines (new `SweepWalls` phase, shared `WallFitting`); walked corners kept as fallback |
| `archive/integration/furniture-detect-and-export` | `eeb4e7f` | krishangnaikar | "ADR-0006": furniture candidates from ARKit horizontal planes (user picks the type); per-object `.glb` export from the Viewer |
| `archive/integration/sweep-and-furniture` | `7a06dd5` | krishangnaikar | Merge of the two above, plus `tools/run_unity_tests.sh` and regenerated scenes. Its handoff reports 197 / 510 / 542 passing tests on Linux |
| `archive/feature/nl-asset-editing` | `f000bef` | krishangnaikar | "ADR-0007" proposal: natural-language add/remove of furniture. Docs only |
| `archive/feature/drag-drop-furniture` | `a64070b` | krishangnaikar | "ADR-0007" accepted: palette drag-and-drop add, delete and undo instead of an LLM. Docs only; never implemented |
| `archive/feature/peer-to-peer-transport` | `2b87407` | krishangnaikar | "ADR-0008" proposal: hotspot, then Bonjour discovery, then Network-framework peer-to-peer. Docs only |
| `archive/docs/one-button-computer-transfer` | `790baa0` | AmeyaSJoshi | "ADR-0005" accepted on its branch: one-button Send to Computer with automatic discovery; I1 split into I1A/I1B; spec wording fixes. Docs only |

`tools/run_unity_tests.sh` on `main` is adapted from the sweep-and-furniture
branch's runner.

If any of these ideas come back, give them fresh ADR numbers from
`docs/decisions/README.md`.
