# Documentation Map

Start with [`AGENTS.md`](../AGENTS.md) for the working rules, then the status
file for whatever you are about to touch.

| Question | Read |
| --- | --- |
| What is GhostMap for, and what must it do? | [`specs/ghostmap-project-spec.md`](specs/ghostmap-project-spec.md) |
| How is it built, and where is each class? | [`architecture/overview.md`](architecture/overview.md) |
| What exactly goes on the wire and in a saved file? | [`contracts/scene-schema-v1.md`](contracts/scene-schema-v1.md), [`contracts/protocol-v1.md`](contracts/protocol-v1.md) |
| Why was it built this way? | [`decisions/`](decisions/README.md) |
| What is the task order, and what is left? | [`plans/ghostmap-implementation-plan.md`](plans/ghostmap-implementation-plan.md) |
| What works right now, and what is broken? | [`status/`](status/): `scanner.md`, `viewer.md`, `shared.md`, `integration.md` |
| What happened in a specific task? | [`handoffs/`](handoffs/README.md) |
| What experiments informed the design? | [`research/`](research/README.md) (not production) |

## Which document wins

1. Code in `shared/com.ghostmap.shared` is the source of truth for the schema,
   protocol and validation; the contract docs describe it.
2. Accepted ADRs override the plan and the spec where they disagree.
3. `AGENTS.md`'s numbered rules govern how work is done.
4. Status files describe current reality, including where it differs from the
   plan. Handoffs are history and are never edited.
