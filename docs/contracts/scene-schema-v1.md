# Scene Schema v1

> **PLACEHOLDER — not yet frozen.**
>
> This document is produced by **Task F1: Create shared local package and data
> schema**. Until F1 is complete, the authoritative definition of the scene
> schema is section 6 of `docs/plans/ghostmap-implementation-plan.md`.
>
> Do not implement against this file in its current state, and do not treat the
> absence of content here as freedom to invent a schema. See `AGENTS.md` rule 2.

## Will contain

- `Vec3Dto`
- `CornerModel`
- `OpeningModel`
- `SceneObjectModel`
- `RoomModel`
- `SceneSnapshot`

Plus the invariants: meters throughout, +Y up, floor at Y = 0, corners ordered and
immutable after finalization, walls **derived** from consecutive corners and never
serialized, `schemaVersion = 1`, monotonic `revision`.
