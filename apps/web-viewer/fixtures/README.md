# Web viewer test fixtures

`objects/*.glb` are the three objects of `fixtures/valid-room-v1.json` (`bed-1`,
`desk-1`, `chair-1`; `room-with-door-window-v1.json` has the same objects),
written by the **real C# exporter**, `GlbWriter.TryExportObject` in
`shared/com.ghostmap.shared/Runtime/Export/GlbWriter.cs` (task `R6`). They let
the tests check placement against what the phone actually exports, not
against hand-made boxes. All three pass the Khronos glTF validator with no
errors, warnings or hints.

To regenerate after the writer changes: add a throwaway EditMode test to
`apps/scanner` (its test assembly references the shared package) that reads
the fixture with `JsonUtility.FromJson<SceneSnapshot>`, calls
`GlbWriter.TryExportObject` for each object, and writes the bytes here as
`<id>.glb`. Run it with
`-runTests -testPlatform EditMode -testFilter <its full name>`, then delete the
test and its `.meta`.

The real `room.glb` comes from `GlbWriter.TryExportRoom`; the Node tests and
the demo use `scripts/standin-glb.mjs` instead so they need no Unity.
