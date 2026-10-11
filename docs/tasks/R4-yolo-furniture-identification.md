# R4: Identify furniture on the phone with YOLO-n

**Decision:** `docs/decisions/ADR-0011-on-device-yolo-furniture-identification.md`.
**Workstream:** Scanner (plus Shared only if `R5` lands in the same PR).
**Who:** an agent writes the plugin, C# pipeline and tests; the owner builds to
the iPhone and runs the device check.

## Goal

When the user points at furniture during `AddObjects`, the phone names it
("bed", "chair", ...) from the camera image, places it on the floor and sizes it
from the ARKit surface it sits on, then offers "Found: bed, desk, 2 chairs" with
one-tap **Add All**. The model names objects only; it never sets a dimension.

## What exists today (reuse it)

| Piece | Where | Notes |
| --- | --- | --- |
| Surface candidates with measured size, yaw, height | `Runtime/Capture/FurnitureDetectionController.cs` (`Refresh`, `Candidates`, `FurnitureCandidate`) | Keep. YOLO supplies the name for these |
| Type guess from ARKit plane label | `Runtime/Capture/FurnitureTypeSuggester.cs`, `FurnitureCandidate.SuggestedType`, `ApplySuggestedType()` | **Replace.** Delete the suggester and its tests (`FurnitureTypeSuggesterTests.cs`) once YOLO names candidates |
| Adopting a candidate into the scene | `ScanWorkflowController.TryAcceptDetectedFurniture`, `TryAcceptAllDetectedFurniture`; `ObjectPlacementController.TryAdoptDetectedObject` | Keep; feed it the YOLO type |
| Floor ray from a screen point | `ISpatialProvider.GetScreenRay`, `RayPlaneMath.TryIntersectHorizontalPlane`, `GhostCoordinateFrame.WorldToGhost` | Use for detections with no surface (chairs often have none) |
| Default dimensions per type | `FurnitureValidator.TryGetDefaultDimensions` | For detections with no matching surface |
| UI | `Runtime/UI/FurnitureDetectionHud.cs`, `AutoScanHud` (**Add All**), `ScanGuide` wording | Extend; keep `ScanGuide` the single owner of wording |
| Test fakes | `Tests/EditMode/FakeSpatialProvider.cs` | Add a `FakeObjectDetector` beside it |

`ARCameraManager` is already in `Scanner.unity` (unused by code).

## Design

### 1. Model

- `pip install ultralytics`, then
  `yolo export model=yolo11n.pt format=coreml nms=True imgsz=640`.
- Commit `apps/scanner/Assets/Plugins/iOS/GhostMapDetector/yolo11n.mlpackage`
  and record its SHA-256 and the Ultralytics version in this brief and in
  ADR-0011. Licence: AGPL-3.0 (see ADR-0011).
- Check the `.mlpackage` is included in the Xcode project by Unity (iOS plugin
  folder). If it is not compiled automatically, add it in `ScannerIosPostBuild`
  (inside the existing `UNITY_IOS` guard).

### 2. Native plugin (Swift or Objective-C++, iOS only)

`apps/scanner/Assets/Plugins/iOS/GhostMapDetector/GhostMapDetector.swift` (+ a
small `.mm` shim exposing C functions if Swift `@_cdecl` is awkward):

```c
// returns 0 on success; fills up to maxCount detections
int GhostMapDetector_Initialize(void);
int GhostMapDetector_Detect(void* arFramePtr, float viewportWidth, float viewportHeight,
                            GhostMapDetection* out, int maxCount);
```

- Input: the native `ARFrame*`. AR Foundation exposes it through
  `XRCameraFrame.nativePtr` (from `ARCameraManager.TryGetLatestFrame`), which
  points to a small struct whose `framePtr` field is the `ARFrame`. **Confirm
  this layout against the AR Foundation 6.3 / ARKit XR Plugin 6.3 docs before
  relying on it.** Using the frame avoids copying the camera image.
- Run Vision (`VNCoreMLRequest`) on `frame.capturedImage` with orientation
  `.right` (portrait).
- Convert each box from Vision's normalized image space to **screen
  coordinates** with `frame.displayTransform(for: .portrait, viewportSize:)`, so
  C# receives boxes in the same space as `ISpatialProvider.CenterScreenPoint`.
- Output struct per detection: class index, confidence, screen-space box
  (xMin, yMin, xMax, yMax) in pixels with Unity's bottom-left origin.
- Run at most ~5 times a second, off the main thread; drop frames rather than
  queue them.

### 3. C# seam

```csharp
namespace GhostMap.Scanner.AR
{
    public readonly struct ObjectDetection
    {
        public string ClassName { get; }      // COCO name, e.g. "dining table"
        public float Confidence { get; }
        public Rect ScreenBox { get; }        // pixels, bottom-left origin
    }

    public interface IObjectDetector
    {
        bool IsAvailable { get; }             // false in Editor/Simulator unless faked
        bool TryGetLatest(List<ObjectDetection> into, out float timestamp);
    }
}
```

- `CoreMlObjectDetector : MonoBehaviour, IObjectDetector` wraps the plugin
  (`[DllImport("__Internal")]`, compiled only under `UNITY_IOS && !UNITY_EDITOR`).
- In demo mode (`SimulatedRoom`), provide a simulated detector that reports the
  virtual room's furniture so the flow works in the Editor.

### 4. Pure C# pipeline (all EditMode-tested)

`Runtime/Capture/FurnitureIdentificationController.cs`:

1. **Floor point:** for each detection, ray through the box's bottom-centre →
   floor plane → Ghost space. Reject if the ray misses the floor or the point is
   outside the room (`RoomGeometry.ContainsPointXZ` against the corner store).
2. **Match a surface:** a `FurnitureCandidate` whose footprint contains, or is
   within 0.3 m of, the floor point → take its width, depth, height, yaw.
3. **Class to type** (ADR-0011 table): `bed`→`bed`, `chair`→`chair`,
   `couch`→`couch`, `dining table`→`table` or `desk` (desk when the matched
   surface is 0.65-0.80 m high and ≤ 0.8 m deep), `tv`→`tv`; `refrigerator`,
   `potted plant`, `toilet`, `sink`, `oven`, `microwave`, `bench`→`generic`;
   everything else ignored.
4. **Tracker:** same type within 0.5 m across frames is one object; propose it
   after 5 frames with confidence ≥ 0.5; forget it after 3 s unseen.
5. **Proposal list:** each tracked object has type, (optional) label, Ghost
   center, size, yaw, and whether size came from a surface or defaults.

Wire into `ScanWorkflowController`: replace the suggested type in
`TryAcceptDetectedFurniture`/`TryAcceptAllDetectedFurniture` with the tracked
type, and let **Add All** also add tracked objects that have no surface (with
default sizes). Every accepted object goes through `FurnitureValidator` and
`ObjectPlacementController.TryAdoptDetectedObject` as today, and each add is one
revision.

### 5. UI

- `ScanGuide` message: "Found: bed, desk, 2 chairs. Tap Add All."
- Per-item remove in the furniture list; the manual Place row stays for misses.
- World-space label markers on proposed objects (reuse `MarkerMaterials`).

## Tests (EditMode)

- Floor projection: box bottom-centre on a known camera pose lands at the
  expected Ghost point; boxes above the horizon are rejected.
- Surface matching: inside, near (≤ 0.3 m), far; picks the nearest when two
  qualify.
- Class map: every row of the table, plus an ignored class.
- Desk vs table rule at the boundaries.
- Tracker: 4 frames → not proposed, 5 → proposed; flicker merges; 3 s timeout;
  two chairs 1 m apart stay two objects.
- Workflow: Add All adds tracked objects, one revision each; refused outside
  `AddObjects`; manual placement unaffected.
- Remove `FurnitureTypeSuggesterTests.cs` with the suggester, and update the
  `SuggestedType` assertions in `FurnitureDetectionControllerTests.cs` (the only
  other test file that references it). Re-check `ScanGuideTests` if guide
  wording changes.
- `ScannerSceneTests` after adding any new component to `ScannerSceneBuilder`
  (owner regenerates the scene).

## Device check (owner)

In the `R3` room: for each piece of furniture, record whether it was detected,
the name given, and size vs tape. Record the detector's frame time from an
Xcode log line (`GhostMap yolo: <ms>`). Targets: bed, desk, table, chair, couch
each found at least 4 times out of 5; no camera stutter.

## Done when

EditMode tests pass in CI; the owner's device check is recorded in
`docs/status/integration.md`; ADR-0011 records the committed model's SHA-256;
`docs/status/scanner.md` is updated; a handoff is written.
