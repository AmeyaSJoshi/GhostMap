# ADR-0004: No dense depth, no LiDAR, no learned reconstruction in the MVP

- **Status:** Accepted
- **Date:** 2026-09-12
- **Task:** F0

## Context

GhostMap's constraint is a **standard, non-Pro iPhone** with no LiDAR sensor. The
obvious approaches to "scan a room" are:

1. **Dense reconstruction from RGB** — photogrammetry or multi-view stereo.
2. **Monocular depth estimation** — a learned model producing a depth map.
3. **Gaussian splatting / NeRF** — learned radiance-field reconstruction.
4. **AR vertical-plane detection** — wait for ARKit to detect every wall.
5. **Assisted geometric capture** — the user aims at features; GhostMap
   intersects camera rays with planes it already knows.

Options 1–3 need texture-rich surfaces, controlled motion, and significant
compute. Bedrooms are the worst case for them: large blank painted walls, flat
untextured ceilings, uneven lighting. They also produce a **mesh**, and a mesh is
precisely what GhostMap has decided not to deliver — the product's value is
*editable structured data*, not surface geometry. A mesh would then need semantic
segmentation on top just to recover the walls and furniture the user wanted.

Option 4 fails in a different way. AR Foundation genuinely can detect vertical
planes, but detection of *every* wall is slow, partial, and unreliable on a
non-Pro device — especially for blank walls. Making the capture flow block on it
puts a nondeterministic dependency in the middle of a live demo.

Option 5 puts the human in the loop for the one thing humans are good at — *"that
is where two walls meet"* — and puts the machine in the loop for the one thing it
is good at: exact ray/plane arithmetic.

## Decision

The MVP uses **assisted geometric capture only**.

AR plane detection is used **exactly once**, to find and lock the floor. From
that moment on, every capture is a camera ray intersected with a plane GhostMap
already knows mathematically:

| Capture | Ray | Plane |
| --- | --- | --- |
| Room corner | center-screen camera ray | locked floor plane |
| Furniture center | center-screen camera ray | locked floor plane |
| Room height | center-screen camera ray | **derived** wall plane |
| Door / window | center-screen camera ray | **derived** wall plane |

Wall planes are generated from the captured corners, not detected:

```text
tangent = normalize(B - A)
normal  = normalize(cross(up, tangent))
```

The following are explicitly **out of MVP scope** and must not be added until the
full MVP acceptance test passes:

dense 3D scanning from RGB · automatic recovery of every visible surface ·
Gaussian splatting · NeRF reconstruction · LiDAR-like depth · fully automatic
furniture dimensions · arbitrary curved rooms · multi-floor buildings ·
automatic semantic recognition of every object · centimeter survey-grade accuracy.

Accuracy is governed by measurable gates instead of claims: closure error
≤ 0.15 m to proceed, median wall absolute error ≤ 0.12 m, height error ≤ 0.15 m.

## Consequences

**Positive**

- Works on the target hardware, in a real bedroom, in poor lighting, today.
- Deterministic and unit-testable: ray/plane intersection is pure math with no
  model weights, no training data, and no inference latency.
- Output is *already* structured and semantic — no segmentation step is needed to
  recover meaning.
- Failure modes are legible to the user ("tracking is degraded", "closure error
  is 22 cm") rather than "the reconstruction looks wrong".
- Tiny payloads, instant sync, no cloud dependency, no privacy exposure.

**Negative**

- The user must actively aim at each corner; capture is not passive.
- Only surfaces the user marks are captured. GhostMap will never discover an
  object the user did not place.
- Accuracy is bounded by AR tracking drift over the scan, which is why closure
  verification is mandatory rather than optional.
- Rooms whose corners cannot be seen or aimed at are not supported.

**Neutral**

- Learned depth remains viable later as an **enhancement layer** (Stretch 5), to
  suggest or refine values a user confirms. It must never become the source of
  truth, and it must never directly mutate the room without confirmation.

## Compliance

- `AGENTS.md` rule 6 forbids adding these techniques to the MVP.
- The implementation plan's stretch ordering governs when they may be revisited.
