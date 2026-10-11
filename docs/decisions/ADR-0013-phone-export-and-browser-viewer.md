# ADR-0013: The phone builds the files, the share sheet sends them, a browser shows them

- **Status:** Accepted (owner decision), not built
- **Date:** 2026-10-11
- **Supersedes:** the transport half of ADR-0010 (automatic discovery over the
  local network), ADR-0009 in `GhostMapDublinHacks` (UDP quick-send discovery),
  and the post-finalization editing authority in ADR-0003
- **Related:** ADR-0006 (`.glb` export), ADR-0012 (direction)

## Context

ADR-0012 sends the finished room to the GhostMap Viewer, a Unity desktop app,
which then exports Unity-ready files. Two problems:

1. **The Viewer needs Unity to run.** Today a user must install the pinned
   Unity editor just to look at their room, or someone must build, sign and
   notarise a macOS app that nobody has built yet.
2. **Getting the room across is network work.** Discovery (UDP 47832 in the
   hackathon repo), the local-network permission, firewalls, venue Wi-Fi, a
   delivery acknowledgement that needs a protocol change, and a remembered
   computer were all still to build (roadmap `R7` before this ADR).

The owner's end goal is to use the room **in Unity**. The Viewer was a stop on
the way, not the destination.

A browser cannot replace the Viewer as a network receiver: web pages cannot
listen for incoming connections. Keeping a live link would need a cloud relay
(breaks ADR-0012's "nothing leaves the phone"), a helper app on the computer
(back to installing something), or the phone hosting the page (the user types
the phone's address, breaking `AGENTS.md` rule 14).

## Decision

**The phone builds the finished files itself. Send to Computer is the iOS share
sheet. A self-contained `room.html` shows the room in any browser.**

### 1. The phone builds the export bundle

After finalization the scanner writes one zip per room:

```text
GhostMap-<room>-<yyyyMMdd-HHmm>.zip
├── room.html           open in any browser to look around       (section 3)
├── room.glb            floor, ceiling, walls with openings cut
├── objects/<id>.glb    one per furniture object
├── scene.json          the SceneSnapshot, schema v1
└── README.txt          what each file is and how to import into Unity
```

- `room.glb` and `objects/*.glb` are glTF 2.0 binary, metres, +Y up.
- The exporters already exist as plain C# with no rendering dependency
  (`GlbExporter`, `FurnitureFactory.BuildParts`, `WallSliceGenerator`). They
  move from the Viewer into the shared package (`Runtime/Export/`) so the phone
  and any remaining desktop code use the same geometry. That is a
  shared-contract change under `AGENTS.md` rule 8.
- The bundle layout becomes a contract, documented as
  `docs/contracts/export-bundle-v1.md`.
- The phone keeps every finalized scan on device, so a room can be sent again
  later.

### 2. Send to Computer is the iOS share sheet

One button opens `UIActivityViewController` with the zip. The user picks their
Mac under AirDrop. iOS shows the transfer and its own "Sent" confirmation. The
same sheet offers Save to Files, iCloud Drive and Mail, which is the path for a
Windows computer.

- A small native iOS plugin (Objective-C/Swift, tens of lines) presents the
  sheet, because Unity cannot. It is written in this repository rather than
  pulled in as a package (`AGENTS.md` rule 10).
- No IP address, port, discovery, pairing or network permission is involved.

### 3. `room.html` is the viewer

One HTML file, self-contained so a double-click works with no server and no
internet. Browsers block a local page from reading files next to it, so the
page embeds the room and furniture geometry and `scene.json` itself.

- Built from a small three.js app in `apps/web-viewer/`, bundled into one
  template file that the scanner ships and fills in at export time.
- Features: orbit, pan, zoom, frame room, dollhouse (hide ceiling), click an
  object to see its name and dimensions, two-click measuring (3D and
  horizontal), an object list. **View and measure only, no editing.**
- three.js is MIT-licensed. The page makes no network requests.

### 4. Where editing happens

After finalization the bundle is the record of the room. Furniture is moved
and resized in Unity, where the user is going anyway, or the room is rescanned.
The phone may adjust objects before finalizing, as today. Editing in the
browser would mean re-implementing the shared furniture rules in JavaScript,
which `AGENTS.md` rule 3 forbids.

### 5. What happens to the Unity Viewer and live streaming

- The Unity Viewer is **frozen**: bug fixes only. It is retired once `room.html`
  passes the plan's acceptance test (roadmap `R10`).
- Protocol v1 TCP streaming stays as a **developer tool** for watching a scan
  live from the Editor. It is no longer a product path.
- The UDP discovery code arriving from `GhostMapDublinHacks` (ADR-0009) is not
  used and is removed with the Viewer.

## Compatibility

| Contract | Impact |
| --- | --- |
| Scene schema v1 | None. `scene.json` is a `SceneSnapshot` |
| Protocol v1 | None. Kept as a developer tool |
| New: export bundle v1 | New contract, `docs/contracts/export-bundle-v1.md` |
| Shared package | Gains `Runtime/Export/` (moved from the Viewer), additive |
| `AGENTS.md` rules 4 and 14 | Rewritten to match this ADR |

## Consequences

**Positive**

- Nothing to install on the computer. A double-click shows the room.
- No networking for the user to get wrong. AirDrop does pairing, transfer and
  confirmation.
- The Unity files are on the computer the moment the transfer finishes.
- Roughly a whole roadmap task (discovery, acknowledgement, remembered
  computer, Viewer liveness) disappears.
- Works for any computer with a browser; only AirDrop itself is Mac-specific.

**Negative**

- No live view of the room on the computer while scanning, except through the
  developer TCP path.
- No furniture editing on the computer outside Unity.
- The working, tested Unity Viewer (V1-V6, 475 tests) is eventually retired.
- The phone does more work at the end of a scan (writing a few `.glb` files and
  a ~1 MB HTML file), which needs measuring on device.
- A new native iOS plugin and a small web build step join the project.

## Compliance

- Any network transfer from the phone, or any server-hosted viewer, needs a new
  ADR.
- The browser viewer must not reimplement shared validation or geometry rules;
  it only displays what the phone exported.
