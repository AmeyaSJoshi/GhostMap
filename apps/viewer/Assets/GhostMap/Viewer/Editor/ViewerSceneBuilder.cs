using System;
using System.IO;
using GhostMap.Viewer.Bootstrap;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace GhostMap.Viewer.Editor
{
    /// <summary>
    /// Builds the Viewer scene from scratch and saves it: an
    /// <see cref="EventSystem"/>, a diagnostics/interaction
    /// <see cref="Canvas"/>, and the <see cref="ViewerBootstrap"/> /
    /// <see cref="ViewerHudController"/> / <see cref="InspectorPanelController"/>
    /// trio that between them own the TCP server, the scene store/ownership
    /// layer, camera, selection/editing/measurement, and every button.
    ///
    /// Mirrors <c>GhostMap.Scanner.Editor.ScannerSceneBuilder</c>'s approach:
    /// build programmatically and save, rather than hand-author a scene asset,
    /// so the wiring is reproducible and diffable.
    /// </summary>
    public static class ViewerSceneBuilder
    {
        private const string ScenePath = "Assets/GhostMap/Viewer/Viewer.unity";

        [MenuItem("GhostMap/Build Viewer Scene")]
        public static void BuildScene()
        {
            UnityScene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateEventSystem();
            OrbitCameraController cameraController = CreateRoomCamera();
            Camera roomCamera = cameraController.GetComponent<Camera>();
            GameObject canvasGo = CreateCanvas();

            Text statusText = CreateStatusText(canvasGo);
            Button loadFixtureButton = CreateActionButton(canvasGo, "LoadFixtureButton", "Load Fixture", -360f);
            Button resetViewButton = CreateActionButton(canvasGo, "ResetViewButton", "Reset View (F)", -440f);
            Button dollhouseButton = CreateActionButton(canvasGo, "DollhouseButton", "Dollhouse (D)", -520f);
            Button measureButton = CreateActionButton(canvasGo, "MeasureButton", "Measure (M)", -600f);
            Text measureButtonLabel = measureButton.GetComponentInChildren<Text>();
            Button clearMeasurementButton = CreateActionButton(canvasGo, "ClearMeasurementButton", "Clear Measurement", -680f);
            Text measurementText = CreateInspectorLine(canvasGo, "MeasurementText", -760f, 320f);

            var roomRendererGo = new GameObject("RoomRenderer", typeof(RoomRenderer));
            var roomRenderer = roomRendererGo.GetComponent<RoomRenderer>();

            AssignSerializedReferences(cameraController, ("roomRenderer", roomRenderer));

            var selectionGo = new GameObject("ObjectSelectionController", typeof(ObjectSelectionController));
            var selectionController = selectionGo.GetComponent<ObjectSelectionController>();
            AssignSerializedReferences(
                selectionController,
                ("roomRenderer", roomRenderer),
                ("targetCamera", roomCamera));

            var editGo = new GameObject("ObjectEditController", typeof(ObjectEditController));
            var editController = editGo.GetComponent<ObjectEditController>();
            AssignSerializedReferences(editController, ("selection", selectionController));

            var measurementGo = new GameObject("MeasurementController", typeof(MeasurementController));
            var measurementController = measurementGo.GetComponent<MeasurementController>();

            var routerGo = new GameObject("ViewerInteractionRouter", typeof(ViewerInteractionRouter));
            var interactionRouter = routerGo.GetComponent<ViewerInteractionRouter>();
            AssignSerializedReferences(
                interactionRouter,
                ("targetCamera", roomCamera),
                ("selection", selectionController),
                ("edit", editController),
                ("measurement", measurementController),
                ("orbitCamera", cameraController));

            Text selectedInfoText = CreateInspectorLine(canvasGo, "SelectedInfoText", -24f, 340f, TextAnchor.UpperRight);
            Text editStatusText = CreateInspectorLine(canvasGo, "EditStatusText", -56f, 340f, TextAnchor.UpperRight);
            InputField positionXField = CreateLabeledField(canvasGo, "PositionX", "Position X", -130f);
            InputField positionZField = CreateLabeledField(canvasGo, "PositionZ", "Position Z", -180f);
            InputField yawField = CreateLabeledField(canvasGo, "Yaw", "Yaw (deg)", -230f);
            InputField widthField = CreateLabeledField(canvasGo, "Width", "Width (m)", -280f);
            InputField depthField = CreateLabeledField(canvasGo, "Depth", "Depth (m)", -330f);
            InputField heightField = CreateLabeledField(canvasGo, "Height", "Height (m)", -380f);

            var inspectorGo = new GameObject("InspectorPanelController", typeof(InspectorPanelController));
            var inspectorPanel = inspectorGo.GetComponent<InspectorPanelController>();
            AssignSerializedReferences(
                inspectorPanel,
                ("selection", selectionController),
                ("edit", editController),
                ("measurement", measurementController),
                ("selectedInfoText", selectedInfoText),
                ("editStatusText", editStatusText),
                ("positionXField", positionXField),
                ("positionZField", positionZField),
                ("yawField", yawField),
                ("widthField", widthField),
                ("depthField", depthField),
                ("heightField", heightField),
                ("measureToggleButton", measureButton),
                ("measureToggleLabel", measureButtonLabel),
                ("clearMeasurementButton", clearMeasurementButton),
                ("measurementText", measurementText));

            var bootstrapGo = new GameObject("ViewerBootstrap", typeof(ViewerBootstrap));
            var bootstrap = bootstrapGo.GetComponent<ViewerBootstrap>();
            AssignSerializedReferences(
                bootstrap,
                ("roomRenderer", roomRenderer),
                ("cameraController", cameraController),
                ("selectionController", selectionController),
                ("editController", editController),
                ("measurementController", measurementController));

            var hudGo = new GameObject("ViewerHudController", typeof(ViewerHudController));
            AssignSerializedReferences(
                hudGo.GetComponent<ViewerHudController>(),
                ("bootstrap", bootstrap),
                ("loadFixtureButton", loadFixtureButton),
                ("statusText", statusText),
                ("resetViewButton", resetViewButton),
                ("dollhouseButton", dollhouseButton),
                ("cameraController", cameraController));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) !);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"GhostMap: viewer scene written to {ScenePath}");
        }

        /// <summary>
        /// Opens the saved scene and asserts every component is present and
        /// wired, so a null reference fails on a laptop rather than silently
        /// at runtime.
        /// </summary>
        public static void VerifyScene()
        {
            UnityScene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                Require(FindInScene<EventSystem>(scene) != null, "no EventSystem; the Load fixture button is dead");
                Require(
                    FindInScene<BaseInputModule>(scene) != null,
                    "no input module on the EventSystem; clicks are dead");

                Require(FindInScene<Camera>(scene) != null, "no Camera; the rendered room cannot be inspected visually");

                var roomRenderer = FindInScene<RoomRenderer>(scene);
                Require(roomRenderer != null, "no RoomRenderer");

                var cameraController = FindInScene<OrbitCameraController>(scene);
                Require(cameraController != null, "no OrbitCameraController; the room cannot be navigated");
                RequireAssigned(cameraController, "roomRenderer");

                var selectionController = FindInScene<ObjectSelectionController>(scene);
                Require(selectionController != null, "no ObjectSelectionController; V5 selection is dead");
                RequireAssigned(selectionController, "roomRenderer", "targetCamera");

                var editController = FindInScene<ObjectEditController>(scene);
                Require(editController != null, "no ObjectEditController; V5 editing is dead");
                RequireAssigned(editController, "selection");

                var measurementController = FindInScene<MeasurementController>(scene);
                Require(measurementController != null, "no MeasurementController; V5 measurement is dead");

                var interactionRouter = FindInScene<ViewerInteractionRouter>(scene);
                Require(interactionRouter != null, "no ViewerInteractionRouter; clicks never reach selection/measurement");
                RequireAssigned(interactionRouter, "targetCamera", "selection", "edit", "measurement", "orbitCamera");

                var inspectorPanel = FindInScene<InspectorPanelController>(scene);
                Require(inspectorPanel != null, "no InspectorPanelController; the edit/measure UI is dead");
                RequireAssigned(
                    inspectorPanel,
                    "selection", "edit", "measurement",
                    "selectedInfoText", "editStatusText",
                    "positionXField", "positionZField", "yawField", "widthField", "depthField", "heightField",
                    "measureToggleButton", "measureToggleLabel", "clearMeasurementButton", "measurementText");

                var bootstrap = FindInScene<ViewerBootstrap>(scene);
                Require(bootstrap != null, "no ViewerBootstrap");
                RequireAssigned(
                    bootstrap,
                    "roomRenderer", "cameraController",
                    "selectionController", "editController", "measurementController");

                var hud = FindInScene<ViewerHudController>(scene);
                Require(hud != null, "no ViewerHudController");
                RequireAssigned(
                    hud,
                    "bootstrap", "loadFixtureButton", "statusText",
                    "resetViewButton", "dollhouseButton", "cameraController");
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
                throw new InvalidOperationException($"Viewer scene is not ready: {whatIsWrong}.");
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

        private static T FindInScene<T>(UnityScene scene) where T : Component
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

        /// <summary>
        /// Desktop mouse/keyboard only: the legacy <see cref="StandaloneInputModule"/>
        /// needs no extra package, unlike the scanner's touch-driven
        /// <c>InputSystemUIInputModule</c>.
        /// </summary>
        private static void CreateEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        /// <summary>
        /// Task V4: the orbit/dollhouse camera, which supersedes V2's fixed
        /// overview position. The starting transform no longer matters much —
        /// the first accepted scene frames itself from its own bounds — but a
        /// sane default keeps the Editor's scene view legible before any room
        /// arrives.
        /// </summary>
        private static OrbitCameraController CreateRoomCamera()
        {
            var cameraGo = new GameObject("RoomCamera", typeof(Camera));
            cameraGo.transform.position = new Vector3(2f, 10f, -3f);
            cameraGo.transform.LookAt(new Vector3(2f, 0f, 1.5f));

            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.10f, 0.12f);

            return cameraGo.AddComponent<OrbitCameraController>();
        }

        private static GameObject CreateCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 800f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvasGo;
        }

        private static Text CreateStatusText(GameObject canvasGo)
        {
            Text text = CreateText(canvasGo, "StatusText", 22);

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -24f);
            rect.sizeDelta = new Vector2(-48f, 320f);

            return text;
        }

        private static Button CreateActionButton(
            GameObject canvasGo,
            string name,
            string caption,
            float anchoredY)
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
            rect.anchoredPosition = new Vector2(24f, anchoredY);
            rect.sizeDelta = new Vector2(260f, 70f);

            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = background;

            Text label = CreateText(buttonGo, "Label", 28);
            label.text = caption;
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

        /// <summary>Task V5: a top-right, right-anchored single line of text —
        /// used for the selected-object summary, the editing-enabled state,
        /// and the measurement readout.</summary>
        private static Text CreateInspectorLine(
            GameObject canvasGo,
            string name,
            float anchoredY,
            float width,
            TextAnchor alignment = TextAnchor.UpperRight)
        {
            Text text = CreateText(canvasGo, name, 20);
            text.alignment = alignment;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, anchoredY);
            rect.sizeDelta = new Vector2(width, 44f);

            return text;
        }

        /// <summary>Task V5: one inspector row — a right-aligned label plus a
        /// numeric-entry field flush with the right edge, both at
        /// <paramref name="anchoredY"/>.</summary>
        private static InputField CreateLabeledField(GameObject canvasGo, string baseName, string label, float anchoredY)
        {
            Text labelText = CreateText(canvasGo, baseName + "Label", 18);
            labelText.text = label;
            labelText.alignment = TextAnchor.MiddleRight;

            RectTransform labelRect = labelText.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(1f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(1f, 1f);
            labelRect.anchoredPosition = new Vector2(-300f, anchoredY);
            labelRect.sizeDelta = new Vector2(150f, 36f);

            return CreateInputField(canvasGo, baseName + "Field", -24f, anchoredY, 260f, 36f);
        }

        private static InputField CreateInputField(
            GameObject canvasGo,
            string name,
            float anchoredX,
            float anchoredY,
            float width,
            float height)
        {
            var fieldGo = new GameObject(name, typeof(Image), typeof(InputField));
            fieldGo.transform.SetParent(canvasGo.transform, false);

            var background = fieldGo.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.92f, 0.92f, 0.92f, 1f);

            RectTransform rect = fieldGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(anchoredX, anchoredY);
            rect.sizeDelta = new Vector2(width, height);

            Text text = CreateText(fieldGo, "Text", 20);
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(8f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);

            var field = fieldGo.GetComponent<InputField>();
            field.textComponent = text;
            field.targetGraphic = background;
            field.contentType = InputField.ContentType.DecimalNumber;
            field.lineType = InputField.LineType.SingleLine;

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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }
    }
}
