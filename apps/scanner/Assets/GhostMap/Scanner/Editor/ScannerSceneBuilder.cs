using System;
using System.IO;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Bootstrap;
using GhostMap.Scanner.UI;
using GhostMap.Shared.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Builds the scanner scene from scratch and saves it. Uses AR Foundation's
    /// own "GameObject/XR/AR Session" and "GameObject/XR/XR Origin (Mobile AR)"
    /// menu commands to create the AR Session and AR Camera hierarchy, so the
    /// tracked-pose wiring matches exactly what the package expects, then adds
    /// ARPlaneManager and ARRaycastManager, the S1 diagnostics Canvas, and the
    /// Task S2 floor-lock UI.
    /// </summary>
    public static class ScannerSceneBuilder
    {
        private const string ScenePath = "Assets/GhostMap/Scanner/Scanner.unity";

        /// <summary>
        /// Y of the Task S3 button row, above the Lock Floor button (90-220)
        /// and below the readouts. All three S3 buttons share it.
        /// </summary>
        private const float S3ButtonRowY = 240f;

        /// <summary>
        /// Y of the two Task S4 button rows, stacked above the Task S3 corner
        /// readout (690-990) rather than squeezed into the already-tight space
        /// below it. This screen is bring-up instrumentation, not the capture
        /// UI Task S6 owns — see the S3 handoff's "screen is now crowded" note.
        /// </summary>
        private const float S4WallRowY = 1080f;

        private const float S4ManualRowY = 1200f;

        /// <summary>
        /// Task S5 Part 1 (openings) button rows, stacked above the Task S4
        /// block (1080-1290). Bring-up instrumentation, not the capture UI —
        /// see the S3/S4 handoffs' "screen is now crowded" notes, which this
        /// continues rather than solves; Task S6 owns the real layout.
        /// </summary>
        private const float S5OpeningsRow1Y = 1330f;

        private const float S5OpeningsRow2Y = 1440f;

        private const float S5OpeningsReadoutY = 1500f;

        /// <summary>Task S5 Part 2 (furniture) button rows, stacked above the openings block.</summary>
        private const float S5ObjectsRow1Y = 1710f;

        private const float S5ObjectsRow2Y = 1810f;

        private const float S5ObjectsRow3Y = 1900f;

        private const float S5ObjectsReadoutY = 1990f;

        /// <summary>
        /// Task S6 elements are top-anchored rather than added to the bottom
        /// stack above: the bottom stack already runs past the 1920-tall
        /// reference resolution (see the S5 handoff's "screen is now extremely
        /// crowded" note), so status, networking and finalization — controls
        /// that must stay reachable regardless of that overflow — sit just
        /// below the Task S1 diagnostics block instead, measured in pixels
        /// down from the top of the canvas.
        /// </summary>
        private const float S6StatusY = 400f;

        private const float S6NetworkStatusY = 520f;

        private const float S6ConnectRowY = 580f;

        private const float S6ActionRowY = 660f;

        [MenuItem("GhostMap/Build Scanner Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject arSessionGo = CreateFromMenu("GameObject/XR/AR Session");
            GameObject xrOriginGo = CreateFromMenu("GameObject/XR/XR Origin (Mobile AR)");

            var planeManager = xrOriginGo.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            var raycastManager = xrOriginGo.AddComponent<ARRaycastManager>();

            Camera arCamera = xrOriginGo.GetComponentInChildren<Camera>();
            if (arCamera == null)
            {
                throw new InvalidOperationException("XR Origin (Mobile AR) did not create an AR camera.");
            }

            var spatialProvider = xrOriginGo.AddComponent<ArSpatialProvider>();
            AssignSerializedReferences(
                spatialProvider,
                ("raycastManager", raycastManager),
                ("arCamera", arCamera));

            // The Button needs an EventSystem to receive touches at all. The
            // Input System backend is the one this project ships (see
            // ScannerInputSettings), so the matching module is the Input System
            // one; the legacy StandaloneInputModule reads UnityEngine.Input,
            // which is exactly the layer that was dead in the second S1 device
            // failure.
            CreateEventSystem();

            GameObject canvasGo = CreateCanvas();
            Text diagnosticsText = CreateDiagnosticsText(canvasGo);
            Graphic crosshair = CreateCrosshair(canvasGo);
            Text floorLockReadout = CreateFloorLockReadout(canvasGo);
            Button lockFloorButton = CreateLockFloorButton(canvasGo);

            var bootstrapGo = new GameObject("ScannerBootstrap", typeof(ScannerBootstrap));
            AssignSerializedReferences(
                bootstrapGo.GetComponent<ScannerBootstrap>(),
                ("arSession", arSessionGo.GetComponent<ARSession>()),
                ("planeManager", planeManager),
                ("arCamera", arCamera),
                ("diagnosticsText", diagnosticsText));

            var hudGo = new GameObject("FloorLockHud", typeof(FloorLockHud));
            var floorLockHud = hudGo.GetComponent<FloorLockHud>();
            AssignSerializedReferences(
                floorLockHud,
                ("spatialProvider", spatialProvider),
                ("lockFloorButton", lockFloorButton),
                ("crosshair", crosshair),
                ("readoutText", floorLockReadout));

            // Task S3 corner capture.
            Text cornerReadout = CreateCornerReadout(canvasGo);
            Button primaryButton = CreateActionButton(
                canvasGo, "CaptureCornerButton", "Start Corners",
                new Vector2(0f, S3ButtonRowY), new Vector2(380f, 110f), 34, out Text primaryLabel);
            Button undoButton = CreateActionButton(
                canvasGo, "UndoCornerButton", "Undo",
                new Vector2(-350f, S3ButtonRowY), new Vector2(300f, 110f), 34, out _);
            Button redoButton = CreateActionButton(
                canvasGo, "RedoCornersButton", "Redo Corners",
                new Vector2(350f, S3ButtonRowY), new Vector2(300f, 110f), 30, out _);

            var cornerHudGo = new GameObject("CornerCaptureHud", typeof(CornerCaptureHud));
            AssignSerializedReferences(
                cornerHudGo.GetComponent<CornerCaptureHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("primaryButton", primaryButton),
                ("primaryButtonLabel", primaryLabel),
                ("undoButton", undoButton),
                ("redoButton", redoButton),
                ("readoutText", cornerReadout));

            // Task S4 height capture.
            Text heightReadout = CreateHeightReadout(canvasGo);
            Button selectWallButton = CreateActionButton(
                canvasGo, "SelectWallButton", "Wall 1/4",
                new Vector2(-260f, S4WallRowY), new Vector2(300f, 100f), 32, out Text selectWallLabel);
            Button captureHeightButton = CreateActionButton(
                canvasGo, "CaptureHeightButton", "Capture Height",
                new Vector2(260f, S4WallRowY), new Vector2(300f, 100f), 30, out Text captureHeightLabel);
            InputField manualHeightInput = CreateManualHeightInputField(
                canvasGo, new Vector2(-220f, S4ManualRowY), new Vector2(340f, 90f));
            Button useManualHeightButton = CreateActionButton(
                canvasGo, "UseManualHeightButton", "Use Manual Height",
                new Vector2(260f, S4ManualRowY), new Vector2(340f, 90f), 26, out _);

            var heightHudGo = new GameObject("HeightCaptureHud", typeof(HeightCaptureHud));
            AssignSerializedReferences(
                heightHudGo.GetComponent<HeightCaptureHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("selectWallButton", selectWallButton),
                ("selectWallLabel", selectWallLabel),
                ("captureHeightButton", captureHeightButton),
                ("captureHeightLabel", captureHeightLabel),
                ("manualHeightInput", manualHeightInput),
                ("useManualHeightButton", useManualHeightButton),
                ("readoutText", heightReadout));

            // Task S5 Part 1: openings.
            Text openingReadout = CreateOpeningReadout(canvasGo);
            Button selectOpeningWallButton = CreateActionButton(
                canvasGo, "SelectOpeningWallButton", "Wall 1/4",
                new Vector2(-350f, S5OpeningsRow1Y), new Vector2(280f, 90f), 28, out Text selectOpeningWallLabel);
            Button toggleOpeningTypeButton = CreateActionButton(
                canvasGo, "ToggleOpeningTypeButton", "Type: door",
                new Vector2(0f, S5OpeningsRow1Y), new Vector2(280f, 90f), 28, out Text toggleOpeningTypeLabel);
            Button captureOpeningPointButton = CreateActionButton(
                canvasGo, "CaptureOpeningPointButton", "Capture Lower-Left",
                new Vector2(350f, S5OpeningsRow1Y), new Vector2(280f, 90f), 24, out Text captureOpeningPointLabel);
            Button undoOpeningButton = CreateActionButton(
                canvasGo, "UndoOpeningButton", "Undo Opening",
                new Vector2(-260f, S5OpeningsRow2Y), new Vector2(300f, 90f), 28, out _);
            Button finishOpeningsButton = CreateActionButton(
                canvasGo, "FinishOpeningsButton", "Finish Openings",
                new Vector2(260f, S5OpeningsRow2Y), new Vector2(300f, 90f), 28, out _);

            var openingHudGo = new GameObject("OpeningCaptureHud", typeof(OpeningCaptureHud));
            AssignSerializedReferences(
                openingHudGo.GetComponent<OpeningCaptureHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("selectWallButton", selectOpeningWallButton),
                ("selectWallLabel", selectOpeningWallLabel),
                ("toggleTypeButton", toggleOpeningTypeButton),
                ("toggleTypeLabel", toggleOpeningTypeLabel),
                ("capturePointButton", captureOpeningPointButton),
                ("capturePointLabel", captureOpeningPointLabel),
                ("undoOpeningButton", undoOpeningButton),
                ("finishOpeningsButton", finishOpeningsButton),
                ("readoutText", openingReadout));

            // Task S5 Part 2: furniture.
            Text objectReadout = CreateObjectReadout(canvasGo);
            Button selectObjectTypeButton = CreateActionButton(
                canvasGo, "SelectObjectTypeButton", "Type: bed",
                new Vector2(-350f, S5ObjectsRow1Y), new Vector2(280f, 80f), 26, out Text selectObjectTypeLabel);
            Button placeObjectButton = CreateActionButton(
                canvasGo, "PlaceObjectButton", "Place Object",
                new Vector2(0f, S5ObjectsRow1Y), new Vector2(280f, 80f), 26, out _);
            Button undoObjectButton = CreateActionButton(
                canvasGo, "UndoObjectButton", "Undo Object",
                new Vector2(350f, S5ObjectsRow1Y), new Vector2(280f, 80f), 26, out _);

            Button widthMinusButton = CreateActionButton(
                canvasGo, "WidthMinusButton", "W -",
                new Vector2(-450f, S5ObjectsRow2Y), new Vector2(160f, 70f), 26, out _);
            Button widthPlusButton = CreateActionButton(
                canvasGo, "WidthPlusButton", "W +",
                new Vector2(-270f, S5ObjectsRow2Y), new Vector2(160f, 70f), 26, out _);
            Button depthMinusButton = CreateActionButton(
                canvasGo, "DepthMinusButton", "D -",
                new Vector2(-70f, S5ObjectsRow2Y), new Vector2(160f, 70f), 26, out _);
            Button depthPlusButton = CreateActionButton(
                canvasGo, "DepthPlusButton", "D +",
                new Vector2(110f, S5ObjectsRow2Y), new Vector2(160f, 70f), 26, out _);
            Button heightMinusButton = CreateActionButton(
                canvasGo, "HeightMinusButton", "H -",
                new Vector2(290f, S5ObjectsRow2Y), new Vector2(160f, 70f), 26, out _);
            Button heightPlusButton = CreateActionButton(
                canvasGo, "HeightPlusButton", "H +",
                new Vector2(470f, S5ObjectsRow2Y), new Vector2(160f, 70f), 26, out _);

            Button yawMinusButton = CreateActionButton(
                canvasGo, "YawMinusButton", "Yaw -",
                new Vector2(-260f, S5ObjectsRow3Y), new Vector2(200f, 70f), 26, out _);
            Button yawPlusButton = CreateActionButton(
                canvasGo, "YawPlusButton", "Yaw +",
                new Vector2(-40f, S5ObjectsRow3Y), new Vector2(200f, 70f), 26, out _);
            Button finishObjectsButton = CreateActionButton(
                canvasGo, "FinishObjectsButton", "Finish Objects",
                new Vector2(260f, S5ObjectsRow3Y), new Vector2(300f, 70f), 26, out _);

            var objectHudGo = new GameObject("ObjectPlacementHud", typeof(ObjectPlacementHud));
            AssignSerializedReferences(
                objectHudGo.GetComponent<ObjectPlacementHud>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("selectTypeButton", selectObjectTypeButton),
                ("selectTypeLabel", selectObjectTypeLabel),
                ("placeObjectButton", placeObjectButton),
                ("undoObjectButton", undoObjectButton),
                ("widthPlusButton", widthPlusButton),
                ("widthMinusButton", widthMinusButton),
                ("depthPlusButton", depthPlusButton),
                ("depthMinusButton", depthMinusButton),
                ("heightPlusButton", heightPlusButton),
                ("heightMinusButton", heightMinusButton),
                ("yawPlusButton", yawPlusButton),
                ("yawMinusButton", yawMinusButton),
                ("finishObjectsButton", finishObjectsButton),
                ("readoutText", objectReadout));

            // Task S6: networking, finalization and one consolidated status line.
            Text scanStatusText = CreateTopAnchoredText(canvasGo, "ScanStatusText", 26, S6StatusY, 110f);
            Text networkStatusText = CreateTopAnchoredText(canvasGo, "NetworkStatusText", 24, S6NetworkStatusY, 50f);

            InputField hostInput = CreateTopAnchoredInputField(
                canvasGo, "HostInput", "Laptop IP", 24f, S6ConnectRowY, new Vector2(420f, 70f));
            InputField portInput = CreateTopAnchoredInputField(
                canvasGo, "PortInput", "Port", 460f, S6ConnectRowY, new Vector2(160f, 70f));
            portInput.text = ProtocolConstants.Port.ToString();
            portInput.contentType = InputField.ContentType.IntegerNumber;

            Button connectButton = CreateTopAnchoredButton(
                canvasGo, "ConnectButton", "Connect", 640f, S6ConnectRowY, new Vector2(220f, 70f), 30, out _);

            Button resetButton = CreateTopAnchoredButton(
                canvasGo, "ResetButton", "Reset", 24f, S6ActionRowY, new Vector2(260f, 70f), 30, out _);
            Button finalizeButton = CreateTopAnchoredButton(
                canvasGo, "FinalizeButton", "Finalize GhostMap", 300f, S6ActionRowY, new Vector2(360f, 70f), 26, out _);

            var scannerHudGo = new GameObject("ScannerHudController", typeof(ScannerHudController));
            AssignSerializedReferences(
                scannerHudGo.GetComponent<ScannerHudController>(),
                ("floorLockHud", floorLockHud),
                ("spatialProvider", spatialProvider),
                ("hostInput", hostInput),
                ("portInput", portInput),
                ("connectButton", connectButton),
                ("networkStatusText", networkStatusText),
                ("resetButton", resetButton),
                ("finalizeButton", finalizeButton),
                ("statusText", scanStatusText));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) !);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"GhostMap: scanner scene written to {ScenePath}");
        }

        /// <summary>
        /// Opens the saved scene and asserts that every component Task S2 needs
        /// is present and wired.
        ///
        /// This exists for the same reason
        /// <c>ScannerXrSettings.VerifyIosArKitConfiguration</c> does: a scene
        /// reference that is silently null fails only on a phone, and looks
        /// identical to a tracking problem when it does. Throws on the first
        /// broken link.
        /// </summary>
        public static void VerifyScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                Require(FindInScene<ARSession>(scene) != null, "no ARSession");
                Require(FindInScene<ARRaycastManager>(scene) != null, "no ARRaycastManager");
                Require(FindInScene<EventSystem>(scene) != null, "no EventSystem; UI touches are dead");
                Require(
                    FindInScene<BaseInputModule>(scene) != null,
                    "no input module on the EventSystem; the Lock Floor button can never fire");

                var planeManager = FindInScene<ARPlaneManager>(scene);
                Require(planeManager != null, "no ARPlaneManager");
                Require(
                    (planeManager.requestedDetectionMode & PlaneDetectionMode.Horizontal) != 0,
                    "ARPlaneManager does not request horizontal planes, so no floor can be detected");

                var provider = FindInScene<ArSpatialProvider>(scene);
                Require(provider != null, "no ArSpatialProvider");
                RequireAssigned(provider, "raycastManager", "arCamera");

                var hud = FindInScene<FloorLockHud>(scene);
                Require(hud != null, "no FloorLockHud");
                RequireAssigned(hud, "spatialProvider", "lockFloorButton", "crosshair", "readoutText");

                var cornerHud = FindInScene<CornerCaptureHud>(scene);
                Require(cornerHud != null, "no CornerCaptureHud");
                RequireAssigned(
                    cornerHud,
                    "floorLockHud",
                    "spatialProvider",
                    "primaryButton",
                    "primaryButtonLabel",
                    "undoButton",
                    "redoButton",
                    "readoutText");

                var heightHud = FindInScene<HeightCaptureHud>(scene);
                Require(heightHud != null, "no HeightCaptureHud");
                RequireAssigned(
                    heightHud,
                    "floorLockHud",
                    "spatialProvider",
                    "selectWallButton",
                    "selectWallLabel",
                    "captureHeightButton",
                    "captureHeightLabel",
                    "manualHeightInput",
                    "useManualHeightButton",
                    "readoutText");

                var openingHud = FindInScene<OpeningCaptureHud>(scene);
                Require(openingHud != null, "no OpeningCaptureHud");
                RequireAssigned(
                    openingHud,
                    "floorLockHud",
                    "spatialProvider",
                    "selectWallButton",
                    "selectWallLabel",
                    "toggleTypeButton",
                    "toggleTypeLabel",
                    "capturePointButton",
                    "capturePointLabel",
                    "undoOpeningButton",
                    "finishOpeningsButton",
                    "readoutText");

                var objectHud = FindInScene<ObjectPlacementHud>(scene);
                Require(objectHud != null, "no ObjectPlacementHud");
                RequireAssigned(
                    objectHud,
                    "floorLockHud",
                    "spatialProvider",
                    "selectTypeButton",
                    "selectTypeLabel",
                    "placeObjectButton",
                    "undoObjectButton",
                    "widthPlusButton",
                    "widthMinusButton",
                    "depthPlusButton",
                    "depthMinusButton",
                    "heightPlusButton",
                    "heightMinusButton",
                    "yawPlusButton",
                    "yawMinusButton",
                    "finishObjectsButton",
                    "readoutText");

                var scannerHud = FindInScene<ScannerHudController>(scene);
                Require(scannerHud != null, "no ScannerHudController");
                RequireAssigned(
                    scannerHud,
                    "floorLockHud",
                    "spatialProvider",
                    "hostInput",
                    "portInput",
                    "connectButton",
                    "networkStatusText",
                    "resetButton",
                    "finalizeButton",
                    "statusText");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static void Require(bool condition, string whatIsWrong)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Scanner scene is not capture-ready: {whatIsWrong}.");
            }
        }

        private static void RequireAssigned(UnityEngine.Object target, params string[] fieldNames)
        {
            var serialized = new SerializedObject(target);
            foreach (string fieldName in fieldNames)
            {
                SerializedProperty property = serialized.FindProperty(fieldName);
                Require(property != null, $"{target.GetType().Name}.{fieldName} does not exist");
                Require(
                    property.objectReferenceValue != null,
                    $"{target.GetType().Name}.{fieldName} is not assigned");
            }
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(includeInactive: true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void AssignSerializedReferences(
            UnityEngine.Object target,
            params (string Field, UnityEngine.Object Value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach ((string field, UnityEngine.Object value) in references)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null)
                {
                    throw new InvalidOperationException(
                        $"{target.GetType().Name} has no serialized field '{field}'.");
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateEventSystem()
        {
            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
            var module = eventSystemGo.AddComponent<InputSystemUIInputModule>();

            // Without actions the module resolves nothing and every tap is
            // swallowed. The defaults cover point/click, which is all the
            // floor-lock button needs.
            module.AssignDefaultActions();
        }

        private static GameObject CreateCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvasGo;
        }

        private static Text CreateDiagnosticsText(GameObject canvasGo)
        {
            Text text = CreateText(canvasGo, "DiagnosticsText", 24);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -48f);
            rect.sizeDelta = new Vector2(-48f, 340f);

            return text;
        }

        /// <summary>
        /// The Task S2 readout, anchored to the bottom so it sits clear of the
        /// S1 diagnostics block above and the Lock Floor button below.
        /// </summary>
        private static Text CreateFloorLockReadout(GameObject canvasGo)
        {
            Text text = CreateText(canvasGo, "FloorLockReadout", 26);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(24f, 380f);
            rect.sizeDelta = new Vector2(-48f, 280f);

            return text;
        }

        /// <summary>
        /// The Task S3 readout, above the floor-lock block: corner count, the
        /// live crosshair projection, every captured corner in Ghost
        /// coordinates, and the closure result.
        /// </summary>
        private static Text CreateCornerReadout(GameObject canvasGo)
        {
            Text text = CreateText(canvasGo, "CornerCaptureReadout", 26);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(24f, 690f);
            rect.sizeDelta = new Vector2(-48f, 300f);

            return text;
        }

        /// <summary>
        /// The Task S4 readout, above the S3 corner readout (690-990): the
        /// selected wall, the live aim projection, and the captured height.
        /// </summary>
        private static Text CreateHeightReadout(GameObject canvasGo)
        {
            Text text = CreateText(canvasGo, "HeightCaptureReadout", 26);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(24f, 1000f);
            rect.sizeDelta = new Vector2(-48f, 260f);

            return text;
        }

        /// <summary>
        /// The Task S5 Part 1 readout, above the Task S4 block: the selected
        /// wall and type, the live aim and pending-point projections, and
        /// every captured opening.
        /// </summary>
        private static Text CreateOpeningReadout(GameObject canvasGo)
        {
            Text text = CreateText(canvasGo, "OpeningCaptureReadout", 24);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(24f, S5OpeningsReadoutY);
            rect.sizeDelta = new Vector2(-48f, 200f);

            return text;
        }

        /// <summary>
        /// The Task S5 Part 2 readout, above the openings block: the object
        /// count, next type, live floor aim, and every placed object's
        /// dimensions and yaw.
        /// </summary>
        private static Text CreateObjectReadout(GameObject canvasGo)
        {
            Text text = CreateText(canvasGo, "ObjectPlacementReadout", 24);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(24f, S5ObjectsReadoutY);
            rect.sizeDelta = new Vector2(-48f, 220f);

            return text;
        }

        /// <summary>
        /// A legacy <see cref="InputField"/> for the Task S4 manual height
        /// fallback (implementation plan section 8.7): a failed automatic
        /// capture must never block the scan.
        /// </summary>
        private static InputField CreateManualHeightInputField(
            GameObject canvasGo, Vector2 anchoredPosition, Vector2 size)
        {
            var fieldGo = new GameObject("ManualHeightInput", typeof(Image), typeof(InputField));
            fieldGo.transform.SetParent(canvasGo.transform, false);

            var background = fieldGo.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.92f, 0.92f, 0.92f, 0.95f);

            RectTransform rect = fieldGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Text text = CreateText(fieldGo, "Text", 32);
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 6f);
            textRect.offsetMax = new Vector2(-16f, -6f);

            Text placeholder = CreateText(fieldGo, "Placeholder", 32);
            placeholder.text = "Height (m)";
            placeholder.color = new Color(0f, 0f, 0f, 0.4f);
            placeholder.fontStyle = FontStyle.Italic;

            RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(16f, 6f);
            placeholderRect.offsetMax = new Vector2(-16f, -6f);

            var field = fieldGo.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.contentType = InputField.ContentType.DecimalNumber;

            return field;
        }

        /// <summary>
        /// A top-anchored, full-width readout. Used by Task S6's status lines,
        /// which must stay visible regardless of how far the bottom button/
        /// readout stack already runs past the reference resolution.
        /// </summary>
        private static Text CreateTopAnchoredText(GameObject canvasGo, string name, int fontSize, float yFromTop, float height)
        {
            Text text = CreateText(canvasGo, name, fontSize);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -yFromTop);
            rect.sizeDelta = new Vector2(-48f, height);

            return text;
        }

        /// <summary>A top-anchored button at an explicit x offset, for Task S6's connection/action row.</summary>
        private static Button CreateTopAnchoredButton(
            GameObject canvasGo,
            string name,
            string labelText,
            float x,
            float yFromTop,
            Vector2 size,
            int fontSize,
            out Text label)
        {
            var buttonGo = new GameObject(name, typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var background = buttonGo.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.16f, 0.16f, 0.18f, 0.92f);

            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -yFromTop);
            rect.sizeDelta = size;

            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = background;

            label = CreateText(buttonGo, "Label", fontSize);
            label.text = labelText;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;

            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = Vector2.zero;

            return button;
        }

        /// <summary>A top-anchored text input at an explicit x offset, for Task S6's laptop IP / port fields.</summary>
        private static InputField CreateTopAnchoredInputField(
            GameObject canvasGo, string name, string placeholderText, float x, float yFromTop, Vector2 size)
        {
            var fieldGo = new GameObject(name, typeof(Image), typeof(InputField));
            fieldGo.transform.SetParent(canvasGo.transform, false);

            var background = fieldGo.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.92f, 0.92f, 0.92f, 0.95f);

            RectTransform rect = fieldGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -yFromTop);
            rect.sizeDelta = size;

            Text text = CreateText(fieldGo, "Text", 30);
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 6f);
            textRect.offsetMax = new Vector2(-16f, -6f);

            Text placeholder = CreateText(fieldGo, "Placeholder", 30);
            placeholder.text = placeholderText;
            placeholder.color = new Color(0f, 0f, 0f, 0.4f);
            placeholder.fontStyle = FontStyle.Italic;

            RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(16f, 6f);
            placeholderRect.offsetMax = new Vector2(-16f, -6f);

            var field = fieldGo.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;

            return field;
        }

        private static Text CreateText(GameObject canvasGo, string name, int fontSize)
        {
            var textGo = new GameObject(name, typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);

            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        /// <summary>
        /// The reticle the floor raycast is fired through: dead center, because
        /// <c>ISpatialProvider.CenterScreenPoint</c> is what the lock reads.
        /// Its color is the fastest read on whether a lock is possible.
        /// </summary>
        private static Graphic CreateCrosshair(GameObject canvasGo)
        {
            var crosshairGo = new GameObject("Crosshair", typeof(Image));
            crosshairGo.transform.SetParent(canvasGo.transform, false);

            var image = crosshairGo.GetComponent<Image>();
            image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            image.color = Color.white;
            image.raycastTarget = false;

            RectTransform rect = crosshairGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(48f, 48f);

            return image;
        }

        private static Button CreateLockFloorButton(GameObject canvasGo)
        {
            var buttonGo = new GameObject("LockFloorButton", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var background = buttonGo.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.16f, 0.16f, 0.18f, 0.92f);

            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 90f);
            rect.sizeDelta = new Vector2(560f, 130f);

            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = background;

            Text label = CreateText(buttonGo, "Label", 40);
            label.text = "Lock Floor";
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;

            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = Vector2.zero;

            return button;
        }

        /// <summary>
        /// A bottom-anchored button. The label is handed back so
        /// <see cref="CornerCaptureHud"/> can retitle the primary control as
        /// the phase changes.
        /// </summary>
        private static Button CreateActionButton(
            GameObject canvasGo,
            string name,
            string labelText,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            out Text label)
        {
            var buttonGo = new GameObject(name, typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var background = buttonGo.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.16f, 0.16f, 0.18f, 0.92f);

            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = background;

            label = CreateText(buttonGo, "Label", fontSize);
            label.text = labelText;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;

            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = Vector2.zero;

            return button;
        }

        private static GameObject CreateFromMenu(string menuPath)
        {
            Selection.activeGameObject = null;
            EditorApplication.ExecuteMenuItem(menuPath);
            GameObject created = Selection.activeGameObject;
            if (created == null)
            {
                throw new InvalidOperationException($"Menu item '{menuPath}' did not create a GameObject.");
            }

            return created;
        }
    }
}
