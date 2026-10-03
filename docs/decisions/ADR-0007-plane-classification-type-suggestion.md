# ADR-0007: Pre-select the furniture type from ARKit's plane label and measured size

- **Status:** Accepted
- **Date:** 2026-10-03
- **Task:** Furniture type suggestion (branch `integration/ui-pr16-device-test`)

## Context

`ADR-0006` detects a furniture top surface and measures its width, depth,
height and yaw, but leaves the type entirely to the user, who had to cycle a
type button for every object. The owner asked for detection that "just works":
look at the furniture, tap once.

Apple RoomPlan does full furniture detection but requires LiDAR, which the
target (ordinary non-Pro iPhone; the owner's device is an iPhone 17) does not
have. Camera-image object recognition (e.g. a YOLO model) would add a new
inference component and dependency.

ARKit already classifies detected planes on A12 and later devices without
LiDAR, and AR Foundation 6.3 — already in the project — exposes it as
`ARPlane.classifications` (`Table`, `Seat`, `Floor`, …). It costs no new
dependency and no camera-image processing.

## Decision

- `DetectedSurface` and `FurnitureCandidate` carry ARKit's
  `PlaneClassifications` for the surface.
- `FurnitureTypeSuggester` (plain C#, scanner `Capture`) maps label + measured
  size to a supported type: Seat → chair / couch / bed by size; Table →
  desk / table / dresser by height and depth; unlabelled → bed, desk, table or
  dresser only when the shape is distinctive, otherwise `generic`.
- `FurnitureDetectionController` pre-selects the offered candidate's suggested
  type and re-applies it on every refresh, because ARKit often labels a plane
  seconds after first reporting it. **A type the user picks always wins** for
  that surface; it never carries over to a different surface.
- The guide says "Looks like a desk … Tap Add". The user confirms or changes
  it. Nothing is added without a tap.

### Exception to AGENTS.md rule 6

Rule 6 keeps automatic object recognition out of the MVP until the MVP
acceptance tests pass. This decision is a **deliberate, owner-approved
exception**, scoped narrowly: it uses ARKit's existing on-device plane label
plus geometry, classifies no camera images, sends nothing off device, and
never adds an object without the user's tap. Anything broader (image models,
automatic adding) still requires its own ADR.

## Consequences

- Usual flow per object: look at it, tap Add.
- ARKit's labels are coarse (no "bed"; desk vs table vs dresser is decided by
  height and depth), so suggestions can be wrong; the type button remains.
- Extents are a lower bound on the real object, so thresholds sit on the small
  side; a partly-seen bed may be suggested as something else until more of it
  is seen.
- Accuracy of ARKit's labels on the target device is **unverified** until a
  physical iPhone run.
- No schema, protocol, viewer or shared change.
