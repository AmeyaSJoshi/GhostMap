using System;
using System.IO;
using GhostMap.Scanner.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Builds the Task S1 scanner scene from scratch and saves it. Uses AR
    /// Foundation's own "GameObject/XR/AR Session" and
    /// "GameObject/XR/XR Origin (Mobile AR)" menu commands to create the AR
    /// Session and AR Camera hierarchy, so the tracked-pose wiring matches
    /// exactly what the package expects, then adds ARPlaneManager and
    /// ARRaycastManager, a diagnostics Canvas, and ScannerBootstrap.
    /// </summary>
    public static class ScannerSceneBuilder
    {
        private const string ScenePath = "Assets/GhostMap/Scanner/Scanner.unity";

        [MenuItem("GhostMap/Build Scanner Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject arSessionGo = CreateFromMenu("GameObject/XR/AR Session");
            GameObject xrOriginGo = CreateFromMenu("GameObject/XR/XR Origin (Mobile AR)");

            var planeManager = xrOriginGo.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            xrOriginGo.AddComponent<ARRaycastManager>();

            Camera arCamera = xrOriginGo.GetComponentInChildren<Camera>();
            if (arCamera == null)
            {
                throw new InvalidOperationException("XR Origin (Mobile AR) did not create an AR camera.");
            }

            Text diagnosticsText = CreateDiagnosticsCanvas();

            var bootstrapGo = new GameObject("ScannerBootstrap", typeof(ScannerBootstrap));
            var bootstrap = bootstrapGo.GetComponent<ScannerBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("arSession").objectReferenceValue = arSessionGo.GetComponent<ARSession>();
            serialized.FindProperty("planeManager").objectReferenceValue = planeManager;
            serialized.FindProperty("arCamera").objectReferenceValue = arCamera;
            serialized.FindProperty("diagnosticsText").objectReferenceValue = diagnosticsText;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) !);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log($"GhostMap: scanner scene written to {ScenePath}");
        }

        private static Text CreateDiagnosticsCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var textGo = new GameObject("DiagnosticsText", typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);

            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(24f, -48f);
            textRect.sizeDelta = new Vector2(-48f, 320f);

            return text;
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
