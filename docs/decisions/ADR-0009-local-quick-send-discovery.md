# ADR-0009: Local broadcast discovery for quick Send to Computer

- **Status:** Accepted and implemented; awaiting physical iPhone-to-Mac verification; **superseded by ADR-0013** (share-sheet transfer); dormant, removed in roadmap task R10
- **Date:** 2026-10-03
- **Task:** Integration quick-send discovery

## Context

PR #19 (`ADR-0008`) correctly identified the on-stage problem: entering a
computer address on a phone is fragile. It was a scoping-only change and did
not provide executable peer-to-peer transport. The existing TCP snapshot path
is already reliable and must remain unchanged.

## Decision

For the immediate product path, the Viewer listens for a small UDP discovery
request on the local network. After finalization, the Scanner's **Send to
Computer** button broadcasts that request, accepts the first valid Viewer
response, then connects with the existing TCP client. The existing connection
handshake resends the complete finalized snapshot.

This is connection setup only. It carries no room data and does not change the
schema, protocol-v1 framing, revision arbitration, or ownership rules.

## Consequences

- It removes IP/port entry from the normal finalization path.
- It works on an ordinary shared Wi-Fi network or a phone hotspot, subject to
  the network allowing UDP broadcast.
- The existing manual address form remains a developer fallback.
- It is **not** true no-infrastructure peer-to-peer transport: it does not use
  Bluetooth or AWDL, has no pairing/authentication, and selects the first
  well-formed response. These are deliberate limits until physical testing
  shows the simple path is insufficient.
- Network-framework native plugins (the Tier 2 option in ADR-0008) remain a
  separately scoped, unimplemented option.

## Verification required

Run a final scan on a real iPhone with the Viewer open on a real Mac, first on
normal Wi-Fi and then with the Mac joined to the phone's hotspot. Confirm the
phone finds the Viewer without manual entry, the Viewer receives the finalized
snapshot, and the generated room is editable.
