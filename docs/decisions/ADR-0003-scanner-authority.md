# ADR-0003: Scanner owns scene state until finalization, then the viewer does

- **Status:** Accepted
- **Date:** 2026-09-12
- **Task:** F0

## Context

Both the scanner and the viewer can plausibly mutate a room. The scanner adds
corners, height, openings and furniture during capture. The viewer drags,
resizes, rotates and deletes furniture afterwards, and saves the result.

If both may mutate at the same time, GhostMap needs conflict resolution: merge
rules, per-field versioning, or a CRDT. All of that is real work, and all of it
is work that does not improve the demo.

The two mutation periods are also naturally disjoint in the product. While the
user is walking around a room holding the phone, nobody is at the laptop dragging
furniture. Once the scan is finished, the phone's job is over.

## Decision

Authority is **exclusive and time-sliced**, switching exactly once per session at
`scan.finalized`.

| Phase | Authoritative side | Other side |
| --- | --- | --- |
| During scanning | **Scanner** | Viewer renders received snapshots and sends **no** scene edits |
| After `scan.finalized` | **Viewer** | Scanner is read-only/finished for that session |

Rules:

- While scanning, the viewer is a pure renderer of scanner snapshots. There is no
  viewer→scanner scene channel in protocol v1.
- The scanner sends the final snapshot **immediately before** the
  `scan.finalized` message, so the viewer's last received state is complete.
- After finalization, the viewer owns the editable copy, increments its **own**
  revision for each edit, and is the side that saves and loads.
- A finalized snapshot cannot be mutated by scanner-side UI.
- The viewer enables editing only when `snapshot.finalized == true`.

Concurrent two-way editing is **not** implemented in the MVP.

## Consequences

**Positive**

- No merge logic, no conflict resolution, no distributed-state bugs.
- At any instant there is exactly one writer, so "which value is correct" is
  never ambiguous.
- The viewer's edit affordances have a single, testable enabling condition.
- Reconnection stays simple: the authoritative side resends its snapshot.

**Negative**

- The user cannot correct a mis-measured corner from the laptop mid-scan; they
  must use the scanner's back transitions or redo the corners.
- Post-finalization scanner edits are impossible for that session. Re-scanning
  starts a new session with a new ID.
- The revision counter changes owner at finalization. Implementations must not
  assume a single producer for the lifetime of a session.

## Compliance

- `AGENTS.md` rule 4 states this ownership model.
- Adding a viewer→scanner scene-edit channel is a breaking protocol change and
  requires a new ADR, a protocol version increment, and contract tests.
