# Architecture Decision Records

Each ADR records one decision, why it was made, and what it costs. An accepted
ADR is changed only by a new ADR that supersedes it.

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](ADR-0001-two-unity-projects.md) | Two Unity projects (scanner, viewer) sharing one local package | Accepted |
| [0002](ADR-0002-snapshot-protocol.md) | Send the full `SceneSnapshot` after every change, never deltas | Accepted |
| [0003](ADR-0003-scanner-authority.md) | Scanner owns the scene until finalization, then the viewer does | Accepted; post-finalization editing superseded by 0013 |
| [0004](ADR-0004-no-dense-depth-in-mvp.md) | Assisted geometric capture; no LiDAR, dense depth or learned reconstruction | Accepted, partly superseded by 0012 |
| 0005 | Sweep wall capture | In `GhostMapDublinHacks`; arrives with task R1 |
| 0006 | Furniture surface detection and per-object `.glb` export | In `GhostMapDublinHacks`; arrives with task R1 |
| 0007 | Furniture type from ARKit's plane label | In `GhostMapDublinHacks`; **superseded by 0011** |
| 0008 | Peer-to-peer transport (scoping) | In `GhostMapDublinHacks`; proposal only |
| 0009 | Local quick-send discovery (UDP, port 47832) | In `GhostMapDublinHacks`; **superseded by 0013**, removed with the Unity Viewer |
| [0010](ADR-0010-one-button-computer-transfer.md) | One-button Send to Computer, no IP typing | Accepted; transport superseded by 0013 |
| [0011](ADR-0011-on-device-yolo-furniture-identification.md) | Identify furniture on the phone with YOLO-n | Accepted, not built |
| [0012](ADR-0012-stand-in-place-on-device-capture.md) | Stand in one spot, capture everything on the phone, deliver Unity-ready files | Accepted; delivery superseded by 0013 |
| [0013](ADR-0013-phone-export-and-browser-viewer.md) | Phone builds the export bundle, Send to Computer is the iOS share sheet, `room.html` is the viewer | Accepted, not built |

The next ADR is **0014**.

## Writing a new ADR

Follow plan section 27: name it `ADR-XXXX-<change>.md` with the next free
number, and cover Context, Decision, Compatibility (breaking or additive),
Scanner impact, Viewer impact and Tests. A schema or protocol change also needs
the contract doc, tests and status updates required by `AGENTS.md` rule 8.
