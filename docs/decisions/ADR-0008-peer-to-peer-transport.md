# ADR-0008: Peer-to-peer transport between scanner and viewer

- **Status:** Proposed — scoping only. **Nothing here is decided or built.**
- **Date:** 2026-10-03
- **Task:** Phone-to-Mac sending without infrastructure Wi-Fi (branch `feature/peer-to-peer-transport`)

## Context

The scanner reaches the viewer over raw TCP on port 47831 (`ADR-0002`). That
works, and it has a demo-day problem: it needs both devices on a network that
allows peer-to-peer traffic, and conference Wi-Fi frequently does not. The
request was "send it via Bluetooth or something", with the demo running on a
Mac.

The Mac detail matters. It removes the blocker that would have killed this on
Windows or Linux: the useful peer-to-peer APIs are Apple-only, and iPhone-to-Mac
is Apple-to-Apple.

### What the transport actually has to carry

Very little. A `SceneSnapshot` for a finished room is a few kilobytes of JSON,
sent after each structural change, newline-delimited. Protocol v1 caps a line at
262144 bytes. This is not a bandwidth problem, and it never will be.

### The protocol layer is already transport-agnostic

The load-bearing discovery while scoping this:

```csharp
public LineReader(Stream stream)
```

`LineReader` takes a plain `System.IO.Stream`. Everything above it —
`ProtocolSerializer`, every message type, `SnapshotRevisionPolicy`, the whole
snapshot model — is written against bytes, not against sockets. **Any transport
that can produce a `Stream` drops in underneath the existing protocol with no
change to protocol v1 at all.**

So this is a transport swap, not a protocol change. `AGENTS.md` rule 5 is not
engaged: full snapshots still go over the wire, in the same format, with the
same revision rules.

### But there is no transport seam yet

`ViewerTcpServer` and `ScannerNetworkClient` are both concrete classes that own
their sockets directly. There is no `ITransport`, no injection point. Extracting
that seam is step one of any option below, and it is worth doing on its own
merits: it is what makes the transport testable without a socket at all.

### The real friction may not be the transport

`ScannerHudController` has a `hostInput` field. **Today a person types the Mac's
IP address into a phone**, on stage, under lights. That is the most fragile step
in the demo and it is a discovery problem, not a transport problem. Worth
separating, because the two have very different costs.

## The options

### Tier 0 — iPhone personal hotspot. No code.

Mac joins the phone's hotspot; the existing TCP works unchanged. Costs nothing,
available today, and removes the conference-Wi-Fi risk entirely. Still needs the
IP typed in.

### Tier 1 — Bonjour discovery, still TCP.

The viewer advertises `_ghostmap._tcp`; the scanner browses and offers a list of
names instead of an empty text field. The transport does not change at all.

Removes the typing, works over ordinary Wi-Fi or the Tier 0 hotspot, and is a
much smaller job than Tier 2 because the byte pipe stays exactly as it is. On
iOS 14+ it needs `NSLocalNetworkUsageDescription` and `NSBonjourServices` in the
plist, and discovery fails **silently** without them — the classic way this is
lost a day to.

### Tier 2 — true peer-to-peer, no network at all.

Apple's **Network framework** with `NWListener`/`NWBrowser` and the peer-to-peer
option, which carries the connection over AWDL or Bluetooth with no infrastructure
network present. It yields a byte-stream connection, which is precisely what
`LineReader` wants.

**Not MultipeerConnectivity.** That was the obvious candidate and it is the
wrong one: Xcode 27 (in beta now) formally deprecates it, Apple's own TN3213
documents moving off it to the Network framework, and there are reports of it
connecting unreliably on iOS 26. Building a new transport on a framework Apple
is retiring would be a poor trade for a project that pins its dependencies this
carefully (`AGENTS.md` rule 10).

Cost, honestly: Network framework is a C/Swift API, and Unity exposes none of
it. This needs **two native plugins** — a static library on the iOS side, a
`.bundle` on the macOS side — plus the plist permissions above. That is the
largest piece of new surface area anyone has proposed for this project, and
none of it can be built, run or verified on the Linux machine this was scoped
on.

## Recommendation

**Tier 0 for the hackathon, Tier 1 next, Tier 2 only if it earns its place.**

Tier 0 costs nothing and removes the actual demo risk. Tier 1 removes the
actual on-stage friction — typing an IP — for a fraction of Tier 2's cost, and
is useful regardless of what the transport underneath becomes. Tier 2 is a real
feature with a real payoff ("no network at all"), and it is a post-hackathon
project, not a this-week one.

Whatever is chosen, **extract the transport seam first**. It is small, it is
testable off-device, and every tier above Tier 0 needs it.

## Open questions

1. Is the goal demo robustness, or a product capability? Tier 0 answers the
   first completely and the second not at all.
2. Does the viewer stay cross-platform? Tier 2 is macOS-only by construction,
   so Windows and Linux viewers would keep the TCP path and the project would
   carry two transports forever.
3. `ProtocolConstants` would gain a service type string for Tiers 1 and 2. That
   is a shared contract change under rule 8 — doc, tests, dedicated commit.
4. Rule 12 applies in full: none of this is working until it runs between a real
   iPhone and a real Mac.

## Decision

None yet. This ADR is a scoping artifact and must not be treated as accepted.
