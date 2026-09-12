# Protocol v1

> **PLACEHOLDER — not yet frozen.**
>
> This document is produced by **Task F3: Protocol v1 + fixtures**. Until F3 is
> complete, the authoritative definition of the wire protocol is section 7 of
> `docs/plans/ghostmap-implementation-plan.md`.
>
> Do not implement against this file in its current state, and do not treat the
> absence of content here as freedom to invent messages. See `AGENTS.md` rule 2.

## Will contain

- Transport: TCP port `47831`, UTF-8, newline-delimited JSON, 262144-byte line cap.
- `WireMessageHeader` with `protocolVersion = 1`.
- Message types: `hello`, `heartbeat`, `phone.pose`, `scene.snapshot`,
  `scan.finalized`.
- `ProtocolSerializer.Serialize` / `TryDeserialize` contract.
- Reconnection behavior for both scanner and viewer.
- Revision arbitration rules (stale ignored, equal treated as duplicate).
