using System;
using System.IO;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Bootstrap;
using GhostMap.Scanner.UI;
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
