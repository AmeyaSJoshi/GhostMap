# Architecture Decision Records

Each ADR records one decision, why it was made, and what it costs. An accepted
ADR is changed only by a new ADR that supersedes it.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](ADR-0001-two-unity-projects.md) | Two Unity projects (scanner, viewer) sharing one local package | Accepted |
| [0002](ADR-0002-snapshot-protocol.md) | Send the full `SceneSnapshot` after every change, never deltas | Accepted |
| [0003](ADR-0003-scanner-authority.md) | Scanner owns the scene until finalization, then the viewer does | Accepted |
| [0004](ADR-0004-no-dense-depth-in-mvp.md) | Assisted geometric capture only: no LiDAR, dense depth or learned reconstruction | Accepted |
| 0005 | Sweep wall capture | Reserved; on branch `integration/sweep-and-furniture`, not yet merged |
| 0006 | Furniture detection and glTF asset export | Reserved; same branch, not yet merged |
| [0007](ADR-0007-one-button-computer-transfer.md) | One-button Send to Computer; discovery sits above the TCP transport | Accepted, not built |

The next ADR is **0008**.

## Writing a new ADR

Follow plan section 27: name it `ADR-XXXX-<change>.md` with the next free
number, and cover Context, Decision, Compatibility (breaking or additive),
Scanner impact, Viewer impact and Tests. A schema or protocol change also needs
the contract doc, tests and status updates required by `AGENTS.md` rule 8.
