# Handoff

## Branch
`integration/furniture-detect-and-export`

## Base commit
`f5a7d30` (merge of PR #13). **Not** based on `scanner/sweep-wall-capture` — see
"Known failures" for the overlap.

## Head commit
`f9b254e`

## What changed

One pipeline, two halves: **detect furniture, measure it, generate a `.glb`
asset from the measurements.** Recorded as `ADR-0006`.

- `6f5729d` `feat` — `ADR-0006`, `RoomGeometry.ContainsPointXZ`,
  `FurnitureDetectionController`,
  `ObjectPlacementController.TryAdoptDetectedObject`,
  `ISpatialProvider.TryGetDetectedSurfaces`, `GlbExporter`,
  `FurnitureAssetExporter`, `FurnitureFactory.ColorFor`, 59 tests.
- `f9b254e` `feat` — `FurnitureDetectionHud`, both scene builders, the Viewer's
  **Export Assets** button and its gate, 18 further tests.

### Detection

A horizontal plane at Ghost height `y` is the **top surface of something `y`
tall**. So height, footprint and yaw all fall out of one detected plane — and
height is exactly the number the S5 manual path had to guess from a per-type
default.

Gates, each tested in isolation: alignment must be `HorizontalUp`; height
`0.20`–`1.40 m`; both extents `>= 0.30 m` and within the shared furniture
maximum; footprint centre inside the room polygon; not already accepted or
dismissed. **The user picks the type**; a detection never mutates the scene on
its own.

### Why this does not break the project's rules

- **`AGENTS.md` rule 6 is untouched.** It prohibits automatic object
  recognition and cloud inference before the MVP acceptance test. This
  recognizes nothing — it finds a horizontal rectangle and measures it. No
  model, no inference, no network.
- **`ADR-0004` is untouched.** It rejected *depending* on detection of blank
  **vertical** walls, which is slow and partial on a non-Pro device. Horizontal
  furniture surfaces are the favourable case and are the same detection the
  device-verified floor lock already relies on. Detection failure is also not
  fatal: S5 manual placement remains, unchanged and tested alongside it.
- **Plan section 25 *is* deviated from**, deliberately and at the project
  owner's direction: it says "only start after full MVP acceptance test
  passes", and the acceptance test has not been run. `ADR-0006` records the
  override and the two things that limit it.

### No new scan phase

Detection lives inside the existing `AddObjects`. The state machine, the
transition table and `scanPhase` are unchanged. Accepted candidates go to
`ObjectPlacementController.TryAdoptDetectedObject`, so there stays exactly one
object store feeding the snapshot — which also means S5's `+`/`-` adjustment
works on a detected object, and it has to, because a detected chair's measured
height is its seat.

### Export

`GlbExporter` writes one binary glTF per object, built from
`FurnitureFactory.BuildParts` — the same pure function the renderer uses — so
the asset matches the screen by construction rather than by a second part
table. Written by hand: the payload is axis-aligned boxes and the container is a
12-byte header plus two chunks, so a package dependency would have been bigger
than the writer.

Files land in `Application.persistentDataPath/ghostmap-assets/`, one per object,
written atomically. **Furniture only** — there is no whole-room `.glb`, and
`ADR-0006` says so rather than claiming one.

## Contract impact

**None. Additive only.**

| Contract | Impact |
| --- | --- |
| `SceneObjectModel` | None. A detected object is ordinary parametric furniture: same seven fields, same eight types. |
| `SceneSnapshot`, `RoomModel` | None. Still `schemaVersion` 1. |
| `ScanPhase` | **None.** |
| Protocol v1 | None. A detected object is indistinguishable on the wire from a hand-placed one. |
| `scene-schema-v1.md`, `protocol-v1.md` | No change required. |
| Saved scene JSON | None. `.glb` files sit alongside it, never instead of it. |

New shared geometry: `RoomGeometry.ContainsPointXZ`. Purely additive.

## How to test

**Steps 1 and 2 first.** Both scene builders changed, and the scenes were
authored on a machine without Unity.

1. Regenerate the scanner scene: **GhostMap → Build Scanner Scene**. Commit it.
2. Regenerate the viewer scene via `ViewerSceneBuilder`. Commit it.
3. Shared suite (`shared/TestProject`): expect 156 + 9 = **165**.
4. Scanner suite (`apps/scanner`, `-buildTarget iOS`): expect 361 + 42 new
   (30 detection + 12 workflow) = **403**, noting that count includes the
   embedded shared tests. Reconcile against the XML, not this arithmetic.
5. Viewer suite (`apps/viewer`): expect 475 + 26 = **501**.
6. `ScannerBuild.ConfigureXr`, then `ScannerBuild.BuildScanner`, then
   `xcodebuild`.
7. **Physical-device test** — required, below.

### Physical-device procedure

Detection cannot be judged off-device at all. Run a scan to `AddObjects`, then:

1. Stand so a desk or table top is in view. Within about half a second a
   **cyan marker** should appear floating at the surface's height, turning
   **yellow** when selected, and the readout should show
   `sel 1/1 G(x,z) WxD top H yaw Y`.
2. Check the measured numbers against a tape measure. The `top` figure is the
   one that matters most: it is the height the manual path could only guess.
3. Tap **Detect as:** until it reads `desk`, then **Add**. The object should
   appear in the viewer immediately, at the right place, at the right size.
4. Look at the desk again. It must **not** be offered a second time, however
   much the plane grows.
5. Try a **bed**, a **dresser**, and a **chair**. The chair is the expected
   failure: its detected height is the seat. Correct it with the S5 `+` height
   control and confirm that works on a detected object.
6. Try a **glass table** and a **dark couch** if available. Expect no detection;
   confirm the manual S5 path still places them.
7. Deliberately look through a **doorway** at furniture in the next room. It
   must not be offered (the in-room gate).
8. Finalize. In the viewer, press **Export Assets**, then open the resulting
   `.glb` files from `ghostmap-assets/` in Blender or any glTF viewer.

### What to report

- **measured vs tape-measured** width, depth and height for every object — this
  is the whole value of the feature, and the only thing that can show whether
  ARKit's observed extents are usable;
- how long after looking at a surface a candidate appeared;
- anything offered that is not furniture (windowsills, rug edges, counters);
- anything obviously furniture that was never offered;
- whether the exported `.glb` files open, and whether their dimensions measure
  correctly in the external tool.

## Test results

**None. Nothing was compiled and nothing was run.**

This machine is Linux with no Unity, no `xcodebuild` and no iPhone. The 77 new
tests were written and have **never executed**; the production code has **never
been compiled**. Expect compile errors first.

What was actually verified here:

- brace, parenthesis and bracket balance on every new and changed file;
- all nine `[SerializeField]` names in `FurnitureDetectionHud` match the names
  used in `AssignSerializedReferences` and `RequireAssigned`;
- the existing APIs the new code calls exist with the signatures used
  (`FurnitureValidator.MaxDimensionM`, `IsSupportedType`,
  `TryGetDefaultDimensions`, `SupportedTypes`,
  `GhostCoordinateFrame.WorldDirectionToGhost` /
  `GhostDirectionToWorld`, `HeightCaptureController.Walls`,
  `FurnitureFactory.BuildParts`);
- `FurnitureFactory.BuildParts` really is `public static` and pure, so the
  exporter can reuse it instead of growing a second part table;
- the desk part table's union really is the full `W x D x H` box sitting on
  `y = 0`, which is what the exported `min`/`max` bounds assertions rely on;
- that `BuildParts` **refuses** an unsupported type rather than falling back to
  generic — one export test was corrected after checking this.

## Known failures

- **`ScannerSceneTests` will fail** until step 1. It asserts the committed
  `Scanner.unity`, which has no `FurnitureDetectionHud` and no `planeManager`
  on `ArSpatialProvider` yet.
- **The Viewer's scene test will fail** until step 2, for the same reason: the
  committed `Viewer.unity` has no `ExportAssetsButton`.
- **This branch and `scanner/sweep-wall-capture` (PR #14) will conflict.** Both
  are based on `f5a7d30` and both change:
  - `ScanWorkflowController.cs` — both add a constructor parameter and a
    property. Trivial to resolve; take both.
  - `ScannerSceneBuilder.cs` — different rows and different HUDs, so the edits
    are adjacent rather than overlapping, but in the same regions.
  - `FloorLockHud.cs` — both add a controller to `BuildWorkflow`. Take both.
  - `ScanWorkflowControllerTests.cs` / `ScannerSnapshotPublisherTests.cs` —
    both change the same factory line. Take both parameters.

  The conflict surface was deliberately kept small: this branch adds **no**
  `ScanPhase` value and **no** transition-table entry, which is where the worst
  of it would otherwise have been. Merge the sweep branch first, then rebase
  this one.
- Everything else is unknown, because nothing ran.

## Known limitations

The three that matter, all recorded in `ADR-0006`:

- **ARKit reports the surface it has observed, not the object.** A plane's
  extent grows as the user looks around, and can stop short of a table's edge
  or run past it onto an adjacent surface. Measured width and depth are a lower
  bound in practice. This is why the S5 adjustment controls had to keep working
  on detected objects.
- **Height is the top surface, which is not always the object's height.** Right
  for a desk, table, dresser, bed or TV stand. **Wrong for a chair and a
  couch**, where the detected plane is the seat and the back rises above it.
- **Glass and dark surfaces may not be detected at all.** The manual path is
  the answer, and is retained for exactly this.

Also:

- A plane under a rug, or a low windowsill, can present as furniture. The
  `0.20 m` floor and the in-room gate catch most of it, not all.
- Yaw is only defined modulo 180 degrees, because the plane's axis sign is
  arbitrary, so width and depth may come out swapped relative to how a person
  would name them.
- Undoing a detected object does not un-dismiss its surface, so it is not
  re-offered until detection is cleared. Un-resolving on undo would need the
  object store to remember which surface produced which object, and the schema
  has no field for that.
- `TryEvaluate`'s in-room gate reads the footprint cached by the last
  `Refresh`, and is skipped if no refresh has run. Deliberate — keeping it
  side-effect free is what makes the other gates testable in isolation — and
  both real callers always refresh first.
- Exported assets are a point-in-time copy: editing an object after exporting
  leaves a stale `.glb` until the user exports again.
- No whole-room `.glb`.
- The detection HUD shares the S5 openings row. That costs nothing today
  because the phases are exclusive, but it does mean two HUDs now depend on
  that exclusivity holding.

## Files most important to read next

- `docs/decisions/ADR-0006-furniture-detection-and-asset-export.md` — the
  decision, the rule-6 reasoning, the section-25 override, and the full
  consequences list.
- `apps/scanner/.../Capture/FurnitureDetectionController.cs` — the gates and the
  measurement.
- `apps/viewer/.../Export/GlbExporter.cs` — the container and the winding rule.
- `apps/scanner/.../Tests/EditMode/ScanWorkflowDetectionTests.cs` — the clearest
  statement of the intended end-to-end behavior.

## Next task

1. Merge PR #14 (sweep) first, then rebase this branch onto it.
2. Regenerate both scenes and commit them.
3. Run all three suites; fix what does not compile.
4. **Device-test detection, and tape-measure everything it reports.** Per
   `AGENTS.md` rule 12 this is not working until it has run on a real iPhone,
   and unlike the export half, nothing about detection quality can be
   established off-device.
5. Open one of the exported `.glb` files in an external tool before trusting
   the writer.
6. `I1` is still not started, and plan section 25 still says stretch work
   should have waited for it.
