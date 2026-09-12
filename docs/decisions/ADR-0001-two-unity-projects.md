# ADR-0001: Two Unity projects with one shared local package

- **Status:** Accepted
- **Date:** 2026-09-12
- **Task:** F0

## Context

GhostMap needs an iPhone AR capture app and a desktop reconstruction/editing app.
Three structures were possible:

1. **One Unity project, platform-switched.** Build target is toggled between iOS
   and desktop.
2. **One Unity project containing both apps as scenes**, with runtime branching.
3. **Two Unity projects in one repository**, sharing a local package.

Option 1 forces a full platform reimport each time a developer switches target,
which costs minutes per switch and makes two people working simultaneously
painful. It also means AR Foundation and the ARKit XR Plugin are present in the
desktop build, where they are dead weight and a source of platform-specific
import errors.

Option 2 keeps the same problems and adds runtime branching complexity plus a
single shared `ProjectSettings` that both workstreams must edit.

The project is also being built by two parallel workstreams under time pressure.
Unity `.unity`, `.prefab` and `.asset` files merge badly. Any structure where
both workstreams touch the same scenes and the same `ProjectSettings` will
produce merge conflicts in exactly the files that are hardest to resolve.

## Decision

Use **two separate Unity projects in one repository**:

```text
apps/scanner    iOS, AR Foundation 6.3.x + Apple ARKit XR Plugin 6.3.x
apps/viewer     desktop, no AR dependencies
```

Both reference one local Unity package, `shared/com.ghostmap.shared`, from their
own `Packages/manifest.json`:

```json
"com.ghostmap.shared": "file:../../../shared/com.ghostmap.shared"
```

The path is relative to each project's `Packages` folder.

The shared package is the **only** source of truth for domain models, geometry
utilities, validation, and wire contracts. Duplicating shared models inside
either app is prohibited.

## Consequences

**Positive**

- No platform switching. Each developer opens one project and stays in it.
- Scanner and viewer scenes/prefabs/`ProjectSettings` are fully separate, so the
  dangerous Unity merge conflicts become rare by construction.
- The desktop viewer carries no AR dependencies at all.
- Directory ownership is unambiguous, which is what makes the parallel
  workstream contract in the implementation plan enforceable.
- The shared contract is versioned in exactly one place and consumed by
  reference, so the two apps cannot silently drift apart.

**Negative**

- Two `Packages/manifest.json` and two `ProjectSettings` trees to maintain.
- The relative `file:` package path is brittle if anyone moves directories.
  Repository layout is therefore fixed by the implementation plan.
- A shared package change requires both projects to reimport.
- Editing the shared package from inside a consuming project is read-only in the
  Unity Package Manager UI; shared code is edited on disk in `shared/`.

**Neutral**

- Two projects means two EditMode test suites plus the shared package's own
  suite. The shared suite is the one that guards the contract.

## Compliance

- Neither app may declare its own copy of a shared type.
- A shared contract change follows the procedure in the implementation plan:
  stop feature work, document, branch, test, merge shared first, then rebase both
  app branches.
