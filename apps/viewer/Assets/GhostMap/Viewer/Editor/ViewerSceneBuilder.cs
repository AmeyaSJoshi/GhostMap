using System;
using System.IO;
using GhostMap.Viewer.Bootstrap;
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
    /// Builds the Task V1 viewer scene from scratch and saves it: an
    /// <see cref="EventSystem"/>, a diagnostics <see cref="Canvas"/>, and the
    /// <see cref="ViewerBootstrap"/> / <see cref="ViewerHudController"/> pair
    /// that between them own the TCP server, the scene store and the
    /// "Load fixture" developer button.
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
            CreateRoomCamera();
            GameObject canvasGo = CreateCanvas();

            Text statusText = CreateStatusText(canvasGo);
            Button loadFixtureButton = CreateLoadFixtureButton(canvasGo);

            var roomRendererGo = new GameObject("RoomRenderer", typeof(RoomRenderer));
            var roomRenderer = roomRendererGo.GetComponent<RoomRenderer>();

            var bootstrapGo = new GameObject("ViewerBootstrap", typeof(ViewerBootstrap));
            var bootstrap = bootstrapGo.GetComponent<ViewerBootstrap>();
            AssignSerializedReferences(bootstrap, ("roomRenderer", roomRenderer));

            var hudGo = new GameObject("ViewerHudController", typeof(ViewerHudController));
            AssignSerializedReferences(
                hudGo.GetComponent<ViewerHudController>(),
                ("bootstrap", bootstrap),
                ("loadFixtureButton", loadFixtureButton),
                ("statusText", statusText));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) !);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"GhostMap: viewer scene written to {ScenePath}");
        }

        /// <summary>
        /// Opens the saved scene and asserts every Task V1 component is
        /// present and wired, so a null reference fails on a laptop rather
        /// than silently at runtime.
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

                var bootstrap = FindInScene<ViewerBootstrap>(scene);
                Require(bootstrap != null, "no ViewerBootstrap");
                RequireAssigned(bootstrap, "roomRenderer");

                var hud = FindInScene<ViewerHudController>(scene);
                Require(hud != null, "no ViewerHudController");
                RequireAssigned(hud, "bootstrap", "loadFixtureButton", "statusText");
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
        /// Task V2: a fixed overview position, not the orbit/dollhouse camera
        /// V4 introduces — just enough to visually inspect the rendered room
        /// shell in the Editor, framed for the fixture rooms' roughly 4m x 3m
        /// footprint.
        /// </summary>
        private static void CreateRoomCamera()
        {
            var cameraGo = new GameObject("RoomCamera", typeof(Camera));
            cameraGo.transform.position = new Vector3(2f, 10f, -3f);
            cameraGo.transform.LookAt(new Vector3(2f, 0f, 1.5f));

            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.10f, 0.12f);
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

        private static Button CreateLoadFixtureButton(GameObject canvasGo)
        {
            var buttonGo = new GameObject("LoadFixtureButton", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var background = buttonGo.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.16f, 0.16f, 0.18f, 0.92f);

            RectTransform rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -360f);
            rect.sizeDelta = new Vector2(260f, 70f);

            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = background;

            Text label = CreateText(buttonGo, "Label", 28);
            label.text = "Load Fixture";
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
