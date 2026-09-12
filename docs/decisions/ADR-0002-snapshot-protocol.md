# ADR-0002: Full-snapshot synchronization instead of event replay

- **Status:** Accepted
- **Date:** 2026-09-12
- **Task:** F0

## Context

The scanner must keep a laptop viewer in sync while a room is captured. Two
designs were considered:

1. **Event/delta replay.** The scanner emits small mutation events
   (`CORNER_ADDED`, `OBJECT_UPDATED`, …) and the viewer reconstructs the room by
   applying them in order.
2. **Full snapshot.** After every structural mutation the scanner sends the
   entire current `SceneSnapshot`.

Event replay is the smaller payload, but it makes the viewer's correctness depend
on having received **every** event in **exact** order. Over a hackathon Wi-Fi
network with an iPhone that may background, relocalize, or drop TCP, that
assumption fails. Recovering requires a resync mechanism — which is a snapshot
anyway, plus the complexity of deciding when to send it.

Event replay also makes divergence silent: if one event is lost, the viewer keeps
rendering a room that is subtly wrong with no way to detect it.

A GhostMap MVP scene is very small: four corners, a handful of openings, and a
few furniture objects. The target is **under 100 KB**, and in practice a typical
scene is a few kilobytes. Snapshots are sent only on mutation — never per frame —
so bandwidth is not a constraint.

## Decision

**Protocol v1 sends the entire current `SceneSnapshot` after every structural
mutation.** There is no delta or event replay.

Transport:

```text
TCP, port 47831
UTF-8, one JSON object per line, terminated with \n
maximum accepted line length 262144 bytes
```

A snapshot is sent immediately after: floor lock, corner add/remove, room height
update, opening add/remove/edit, furniture add/remove/edit, and scan
finalization.

`SceneSnapshot.revision` is monotonic within a `sessionId`. The viewer:

- accepts the first valid snapshot of a **new** session;
- accepts a snapshot for the **current** session only if `revision > current`;
- treats an **equal** revision as a harmless duplicate and ignores it;
- ignores any **lower** revision as stale;
- validates the room before applying it;
- never clears the displayed scene because the connection dropped.

`phone.pose` is explicitly **debug/display only**, capped at 5 Hz. The room is
never reconstructed from pose messages.

## Consequences

**Positive**

- **Idempotent.** Receiving the same snapshot twice is harmless.
- **Trivial reconnection.** After reconnecting, the scanner simply sends its
  current snapshot; no replay log, no resync negotiation.
- **No silent divergence.** The viewer's state is always exactly some snapshot
  the scanner produced.
- **Debuggable.** One JSON object fully describes the scene, so a failure can be
  diagnosed by reading a single line of the wire log.
- **Onboarding.** A new worker understands the entire protocol state from one
  object.

**Negative**

- Larger payload per mutation than a delta would be. Accepted: scenes are tiny
  and mutations are user-paced, not per-frame.
- The viewer rebuilds the scene on each accepted snapshot. For the MVP scene size
  a full rebuild under 100 ms is acceptable; partial rebuild is an optimization,
  not a requirement.
- Snapshot generation must never happen per frame, or the scanner will produce
  avoidable garbage.

## Compliance

- Replacing this with delta/event replay requires a new ADR **and** contract
  tests, per `AGENTS.md` rule 5.
- Any change to message types, port, framing, or the revision rules is a protocol
  version change and must update `docs/contracts/protocol-v1.md`.
