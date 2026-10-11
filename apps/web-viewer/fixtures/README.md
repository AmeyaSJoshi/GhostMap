# Web viewer test fixtures

`objects/*.glb` are the three objects of `fixtures/valid-room-v1.json` (`bed-1`,
`desk-1`, `chair-1`; `room-with-door-window-v1.json` has the same objects),
written by the **real C# exporter**, `GlbExporter.TryExportObject` in
`apps/viewer/Assets/GhostMap/Viewer/Runtime/Export/GlbExporter.cs`, at commit
`69af8eb`. They let the tests check placement against what the phone will
actually export, not against hand-made boxes.

To regenerate after the exporter changes (until `R6` moves it into the shared
package): add a throwaway EditMode test to `apps/viewer` that reads the fixture
with `JsonUtility.FromJson<SceneSnapshot>`, calls
`GlbExporter.TryExportObject` for each object, and writes the bytes here as
`<id>.glb`. Run it with
`-runTests -testPlatform EditMode -testFilter <its full name>`, then delete the
test and its `.meta`.

There is no real `room.glb` until `R6`; tests and the demo use
`scripts/standin-glb.mjs` instead.
