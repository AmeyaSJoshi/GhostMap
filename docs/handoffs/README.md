# Handoffs

A handoff file lets another worker continue this project **with no chat history**.

## Rules

1. Create a **new file** for every meaningful branch handoff. **Never overwrite an
   existing handoff.** The directory is an append-only log.
2. Filename format:

   ```text
   docs/handoffs/YYYY-MM-DD-<workstream>-<short-description>.md
   ```

   Examples:

   ```text
   2026-09-12-foundation-f0-repo-scaffold.md
   2026-09-14-scanner-corner-capture.md
   2026-09-15-viewer-wall-openings.md
   ```

3. `<workstream>` is one of `foundation`, `shared`, `scanner`, `viewer`,
   `integration`.
4. Record **actual** results. Never record a test as passing unless it was
   executed and observed to pass. Never record physical-device behavior as
   verified unless it ran on a real iPhone.
5. A handoff is required at the end of every meaningful task, alongside the
   status-file update and the commit.

## Template

Copy this verbatim and fill it in.

```markdown
# Handoff

## Branch
`scanner/corner-capture`

## Base commit
`<sha>`

## Head commit
`<sha>`

## What changed
- ...

## Contract impact
- None
or
- Exact contract change and version.

## How to test
1. ...
2. ...

## Test results
- ...

## Known failures
- ...

## Files most important to read next
- ...

## Next task
- ...
```

## Index

| Date | Workstream | File |
| --- | --- | --- |
| 2026-09-12 | foundation | [F0 — repo scaffold](2026-09-12-foundation-f0-repo-scaffold.md) |
| 2026-09-12 | foundation | [F1 — scene schema v1](2026-09-12-foundation-f1-scene-schema.md) |
| 2026-09-12 | foundation | [F2 — geometry and validation](2026-09-12-foundation-f2-geometry-validation.md) |
| 2026-09-12 | foundation | [F3 — protocol v1 and fixtures](2026-09-12-foundation-f3-protocol-fixtures.md) |
| 2026-09-12 | foundation | [F4 — foundation review](2026-09-12-foundation-f4-foundation-review.md) |
| 2026-09-12 | scanner | [S1 — ARKit loader root cause](2026-09-12-scanner-s1-arkit-loader-root-cause.md) |
| 2026-09-12 | scanner | [S1 — camera pose not driven by tracking](2026-09-12-scanner-s1-camera-pose-not-driven.md) |
