# R6: The phone builds the export bundle

**Decision:** ADR-0013 sections 1 and 4. **Workstreams:** Shared (moves the
exporters, new contract), Scanner (builds and stores the bundle).
**Who:** an agent; CI runs the tests; the owner opens the files on the Mac.

## Goal

On **Finalize GhostMap**, the phone writes
`GhostMap-<room>-<yyyyMMdd-HHmm>.zip` containing `room.html`, `room.glb`,
`objects/<id>.glb`, `scene.json` and `README.txt`, and keeps it so the scan can
be sent again.

## What exists today (reuse it)

| Piece | Where | Unity dependency |
| --- | --- | --- |
| Per-object `.glb` writer | `apps/viewer/.../Runtime/Export/GlbExporter.cs` (`TryExportObject(SceneObjectModel, out byte[], out string)`) | Unity value types only (`Vector3`, `Color`, `Mathf`); uses `FurnitureFactory.BuildParts` and `ColorFor` |
| Atomic file writing, per-object results | `apps/viewer/.../Runtime/Export/FurnitureAssetExporter.cs` | `Application.persistentDataPath` for the default directory |
| Furniture part table | `apps/viewer/.../Runtime/Rendering/FurnitureFactory.cs`: `FurniturePartSpec`, `BuildParts`, `ColorFor` are pure; `Create`/`Dispose` build `GameObject`s | Mixed: split it |
| Wall segmentation | `apps/viewer/.../Runtime/Rendering/WallSliceGenerator.cs` (`BuildSlices`) | Pure |
| Wall placement | `apps/viewer/.../Runtime/Rendering/WallRenderer.cs` (`BuildWalls` → `WallRenderSpec`/`WallSegmentSpec`, `WallThicknessM` 0.10) | Pure math with `Vector3`/`Quaternion` |
| Floor/ceiling | `apps/viewer/.../Runtime/Rendering/FloorCeilingRenderer.cs` | Returns `UnityEngine.Mesh`; needs a vertex-list variant |
| Tests | `GlbExporterTests.cs`, `FurnitureFactoryTests.cs`, `WallSliceGeneratorTests.cs`, `WallRendererTests.cs`, `WallRendererOpeningsTests.cs`, `FloorCeilingRendererTests.cs` | Move the pure parts' tests with the code |

`GlbExporter` already handles Unity's left-handed axes by negating Z. Each
object is written in its own local space, centred on the origin and sitting on
`y = 0`. Its node carries **no transform**: the doc comment on `TryExportObject`
says yaw goes on the node, but `Build` writes only `mesh` and `name`. Fix the
comment when moving the code; placing objects in the room is the job of
whoever reads `scene.json` (Unity user, `room.html`). Node names are
`<type>_<shortened id>`.

## Steps

### 1. Move the pure geometry into the shared package (Shared, rule 8)

New namespace `GhostMap.Shared.Export` in `shared/com.ghostmap.shared/Runtime/Export/`:

- `FurnitureParts` (from `FurnitureFactory`: `FurniturePartSpec`, `BuildParts`,
  `ColorFor`). The Viewer's `FurnitureFactory.Create` calls it.
- `WallSlices` (from `WallSliceGenerator`) and `WallLayout` (the pure part of
  `WallRenderer.BuildWalls`). The Viewer's renderers call them.
- `FootprintMesh`: floor and ceiling triangles as vertex/index lists (fan from
  corner 0 with the winding rule now in `FloorCeilingRenderer`).
- `GlbWriter` (from `GlbExporter`): keep `TryExportObject`; add
  `TryExportRoom(RoomModel, out byte[], out string)` that writes floor, ceiling
  and every wall segment as boxes, in Ghost-space positions (Z negated).
- Move their tests to `shared/com.ghostmap.shared/Tests/Editor/`. Keep the
  Viewer compiling (it is frozen, but must not break before `R10`).

### 2. Contract

Write `docs/contracts/export-bundle-v1.md`: file names, zip layout, units
(metres), axes (glTF +Y up, right-handed; Unity import via glTFast restores
Unity axes), what `room.html` embeds, the README text, and that `scene.json` is
a `SceneSnapshot` (schema v1). Add a test that a built bundle matches it.

### 3. Bundle builder (Scanner)

`apps/scanner/.../Runtime/Export/ExportBundleBuilder.cs`, plain C#:

```csharp
public static bool TryBuild(SceneSnapshot finalized, string roomTemplateHtml,
                            DateTime now, out byte[] zip, out string fileName, out string error);
```

- Refuse an unfinalized snapshot.
- `System.IO.Compression.ZipArchive` into a `MemoryStream`. If the compiler cannot
  find `ZipArchive`, add a `csc.rsp` with `-r:System.IO.Compression.dll` next to
  the scanner's runtime `.asmdef`; confirm in CI.
- `room.html` = the `R8` template with placeholders replaced by base64 GLBs and
  the scene JSON (until `R8` ships, use a minimal placeholder template so the
  bundle shape is testable).
- `README.txt`: what each file is; "Unity: add the glTFast package
  (com.unity.cloud.gltfast), then drag room.glb and objects/ into Assets".

### 4. Saved scans (Scanner)

`SavedScanStore` (plain C#, directory injected; real path
`Application.persistentDataPath/scans/`): `Save(zipBytes, fileName)`, `List()`
newest first, `Delete(fileName)`. Write atomically (temp file + move).

### 5. Wire into finalize

After `ScanWorkflowController.TryFinalize` succeeds (today
`ScannerHudController.OnFinalizePressed`), build and save the bundle, and show
"Saved" or the error. Measure and log the time (`GhostMap export: <ms>`).

## Tests

- Shared: moved tests stay green; `TryExportRoom` for the fixture room has
  floor, ceiling and the expected wall segment count (door cut out); bounds in
  metres; Z negated.
- Contract test: the bundle for `valid-room-v1.json` contains exactly the
  documented entries; `scene.json` parses back to an equal snapshot.
- Scanner: unfinalized refused; file name format; saved-scan list order;
  atomic write leaves no partial file on failure.

## Done when

CI is green; the owner unzips a bundle from the phone on the Mac, opens
`room.glb` in Blender and with `npx gltf-validator`, and imports it into a fresh
Unity project with glTFast at the right scale. Status pages, the contract doc
and a handoff are updated.
