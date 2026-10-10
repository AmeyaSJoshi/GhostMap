# ADR-0007: One-button, automatic computer transfer is the primary UX

> Written as ADR-0005 on branch `docs/one-button-computer-transfer` (`790baa0`)
> and renumbered on merge, because ADR-0005 and ADR-0006 are reserved for sweep
> wall capture and furniture detection.

- **Status:** Accepted
- **Date:** 2026-10-03
- **Task:** Pre-I1 architecture alignment (docs only)

## Context

Scanner `S1`-`S6` and Viewer `V1`-`V6` are complete. The Scanner's current
connection screen (Task S6) asks for a laptop IP address and a port, and the
Integration plan (Task I1) was written around "note the laptop LAN IP, type it
into the phone, connect, then scan while every mutation streams live".

That flow is a valid way to prove the transport works. It is not an acceptable
product. A normal user must not need to know what an IP address, a port, or TCP
is in order to get a finished GhostMap onto their computer. The product goal is:

> Press one button and the GhostMap appears on your computer.

The existing architecture (full `SceneSnapshot` messages over TCP, revision and
session handling, validation, Viewer ownership after finalization) is sound and
is not being replaced. The new requirement sits **above** that transport.

## Decision

**GhostMap's primary completed-scan transfer UX is one-button, automatic, local
computer transfer.**

```text
SCAN ROOM
    -> FINALIZE
    -> [ SEND TO COMPUTER ]
    -> GhostMap automatically finds / connects to the user's computer
    -> the complete GhostMap scene is transferred
    -> the computer opens / reconstructs the room
    -> the phone shows success
```

### User requirement

A normal user must **never** have to type an IP address or a port, or otherwise
understand TCP, network interfaces, or local-network debugging information.

### Layering

Discovery and pairing are **connection setup**, layered above the existing
transport. They are not a new scene protocol.

```text
Discovery / Pairing / Send UX
              |
        TCP connection
              |
protocol-v1 full SceneSnapshot messages
              |
Viewer scene store / editable scene
              |
        rendered room
```

`SceneSnapshot`, full-snapshot synchronization (ADR-0002), the TCP transport,
revision/session arbitration, validation, and Viewer ownership after
finalization (ADR-0003) are all unchanged by this decision.

### Pairing and discovery (intent, not implementation lock-in)

The preferred eventual experience is:

1. The GhostMap Viewer advertises itself on the local network.
2. The iPhone automatically discovers nearby GhostMap computers.
3. First use may require one very small pairing step: choose a discovered
   computer, and/or scan a pairing QR code if automatic discovery fails.
4. GhostMap remembers the paired computer.
5. Later sessions need only: **Finalize -> Send to Computer**.
6. If the remembered computer cannot be found, the phone shows a simple retry /
   choose-another-computer flow, in user language.
7. Manual IP entry is hidden behind a developer/debug option.

Automatic service discovery is the current preferred implementation direction,
but **this ADR does not select a library, API, or technology.** That is decided
and tested during Integration `I1B` design. Nothing here promises that a
particular mechanism works on a particular platform until it has been verified.

### Fallbacks

- Manual IP/port entry remains available as a **developer/debug fallback only**.
  It must not be the primary workflow and must not be required for standard use.
- A QR pairing step may be used as a simple fallback if automatic discovery
  proves unreliable.

### Local and offline

Transfer is local and direct whenever possible. No cloud service, server
account, or internet connection may be introduced merely to move a GhostMap from
the phone to the computer.

### Platform

The receiver is conceptually a **computer**, not specifically a Mac.

- macOS is the current development and test environment.
- The transfer architecture must not intentionally depend on the receiver being
  a Mac, and the desktop Viewer should remain architecturally capable of Windows
  support.
- Windows support is **not** claimed until a real Windows build and integration
  test have been performed. No Windows work is part of this decision.

### Live streaming

The existing live snapshot streaming capability is **retained**, not removed.
However, the primary UX is no longer "connect manually before scanning and watch
every mutation stream". The required workflow is: scan locally, finalize, send
the complete current scene. A user must not need live streaming to successfully
transfer and use a GhostMap. Live preview may later become an optional feature.

## Consequences

- **Integration `I1` must validate the product transfer flow**, not merely prove
  that manually-entered TCP connectivity works. The plan now splits baseline
  transport verification (`I1A`) from the one-button transfer UX (`I1B`).
- Manual-IP connectivity stays valuable as a **diagnostic baseline**: if `I1B`
  fails, `I1A` separates "software/protocol is broken" from "discovery or pairing
  is broken".
- The Scanner's S6 connection screen (IP and port fields) is reclassified as a
  developer/debug surface. It is not removed by this ADR.
- Protocol v1 is unchanged by this ADR. Open design questions that may later
  require shared-contract work are recorded below and must be settled in `I1B`
  design through the normal shared-contract change procedure.

### Open questions deliberately left for I1B design

1. **Discovery mechanism** and its platform cost (iOS local-network permission
   and declarations, advertising from a Unity desktop app, behaviour on Windows).
2. **Pairing and trust.** How the phone knows it is talking to the intended
   computer on a shared network, and whether the listener needs any
   authentication.
3. **Delivery confirmation.** "The phone shows success" implies the phone learns
   the computer actually received and accepted the room. Protocol v1 is
   Scanner -> Viewer only; a viewer-to-scanner acknowledgment would be an additive
   shared-contract change and is **not** made or promised here. Until it is
   decided, "success" must not be claimed on the strength of a socket write
   alone.
4. **Send after finalize.** Confirm the Scanner can connect only after
   finalization and still deliver the full final snapshot (protocol v1 already
   requires a connecting scanner to send `hello` then its current snapshot, and
   the Viewer's ownership switch is driven by `snapshot.finalized`).
5. **Scanner-side persistence.** The scanner currently keeps the scene in
   memory only; whether a finalized scan must survive an app restart before it is
   sent is undecided.
6. **Multiple computers** on one network, and what "remembered computer" means
   when the remembered one is absent.

## Candidate mechanisms for I1B

Recorded from the peer-to-peer transport scoping (branch
`feature/peer-to-peer-transport`, `2b87407`, not merged). Input to the I1B
design step, not a selection.

1. **iPhone Personal Hotspot.** No code. The computer joins the phone's
   hotspot and the existing TCP transport works unchanged, which removes any
   dependence on venue Wi-Fi. The IP still has to be typed, so this is a demo
   fallback, not the product flow.
2. **Bonjour discovery over the existing TCP** (most likely first choice). The
   Viewer advertises `_ghostmap._tcp`; the scanner browses and shows computer
   names instead of an IP field. The byte stream does not change. iOS needs
   both `NSLocalNetworkUsageDescription` and `NSBonjourServices` in
   `Info.plist`; without them discovery fails **silently**. The service-type
   string would be a shared-contract addition under `AGENTS.md` rule 8.
3. **True peer-to-peer** with Apple's Network framework
   (`NWListener`/`NWBrowser`, peer-to-peer enabled, over AWDL or Bluetooth),
   for when no shared network exists. Needs a native iOS plugin and a native
   macOS plugin, and would be macOS-only. **Not** MultipeerConnectivity, which
   Apple is deprecating.

Every option except (1) starts by extracting a transport seam:
`ViewerTcpServer` and `ScannerNetworkClient` own their sockets directly today,
while `LineReader` and everything above it already work on a plain `Stream`.

## Non-goals

- Implementing discovery, pairing, or QR scanning now.
- Replacing `SceneSnapshot` or reverting to delta/event replay (ADR-0002 stands).
- Cloud sync, accounts, or any internet requirement.
- Building Windows support now.
- Phone-only viewing or editing.
- Changing the frozen scene schema or protocol v1.

## Compliance

- `AGENTS.md` rule 14 states the product rule and points here.
- Replacing the TCP/full-snapshot transport still requires its own ADR
  (`AGENTS.md` rule 5, ADR-0002).
- Any viewer-to-scanner message (including an acknowledgment) is a shared
  protocol change and follows `AGENTS.md` rule 8.
