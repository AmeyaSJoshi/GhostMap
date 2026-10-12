using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GhostMap.Scanner.Capture;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// Task R4 (ADR-0011): YOLO11n on the phone, through the native plugin
    /// <c>Assets/Plugins/iOS/GhostMapDetector/GhostMapDetector.mm</c>.
    ///
    /// <para>About five times a second it hands ARKit's current camera frame
    /// to the plugin (which drops frames while busy) and turns the plugin's raw
    /// boxes into screen boxes with <see cref="DetectionGeometry"/>. Created at
    /// runtime by <see cref="ObjectDetectors.Create"/>; nothing in the scene
    /// depends on it.</para>
    /// </summary>
    public sealed class CoreMlObjectDetector : MonoBehaviour, IObjectDetector
    {
        /// <summary>Under StreamingAssets.</summary>
        public const string ModelRelativePath = "GhostMap/yolo11n.mlpackage";

        public const float SubmitIntervalSeconds = 0.2f;

        private const int MaxDetections = 64;
        private const int LogEveryFrames = 25;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDetection
        {
            public int classIndex;
            public float confidence;
            public float x;
            public float y;
            public float width;
            public float height;
        }

        private readonly NativeDetection[] native = new NativeDetection[MaxDetections];
        private readonly float[] transform6 = new float[6];
        private readonly byte[] photoBuffer = new byte[ObjectPhotoRules.MaxBytes];
        private DisplayTransform lastDisplay = DisplayTransform.Identity;
        private Vector2 lastScreen;
        private string[] labels = Array.Empty<string>();

        private ARCameraManager cameraManager;
        private Camera arCamera;
        private float nextSubmitAt;
        private long lastFrameId;
        private bool loggedState;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int GhostMapDetector_State();
        [DllImport("__Internal")] private static extern void GhostMapDetector_Load(string modelPath);
        [DllImport("__Internal")] private static extern int GhostMapDetector_LabelCount();
        [DllImport("__Internal")] private static extern int GhostMapDetector_CopyLabel(int index, StringBuilder buffer, int size);
        [DllImport("__Internal")] private static extern int GhostMapDetector_CopyError(StringBuilder buffer, int size);
        [DllImport("__Internal")] private static extern int GhostMapDetector_Submit(IntPtr nativeFrame, float viewportWidth, float viewportHeight);
        [DllImport("__Internal")] private static extern int GhostMapDetector_Latest(
            [Out] NativeDetection[] detections, int maxCount, out long frameId, [Out] float[] transform6, out float elapsedMs);
        [DllImport("__Internal")] private static extern int GhostMapDetector_CropJpeg(
            float x, float y, float width, float height, int maxSide, float quality, [Out] byte[] buffer, int capacity);
        private const bool NativeAvailable = true;
#else
        private static int GhostMapDetector_State() => -1;
        private static void GhostMapDetector_Load(string modelPath) { }
        private static int GhostMapDetector_LabelCount() => 0;
        private static int GhostMapDetector_CopyLabel(int index, StringBuilder buffer, int size) => 0;
        private static int GhostMapDetector_CopyError(StringBuilder buffer, int size) => 0;
        private static int GhostMapDetector_Submit(IntPtr nativeFrame, float viewportWidth, float viewportHeight) => 0;
        private static int GhostMapDetector_Latest(NativeDetection[] detections, int maxCount, out long frameId, float[] transform6, out float elapsedMs)
        {
            frameId = 0;
            elapsedMs = 0f;
            return 0;
        }
        private static int GhostMapDetector_CropJpeg(
            float x, float y, float width, float height, int maxSide, float quality, byte[] buffer, int capacity) => 0;
        private const bool NativeAvailable = false;
#endif

        /// <summary>Plugin state: 0 idle, 1 loading, 2 ready, -1 failed.</summary>
        public int State => GhostMapDetector_State();

        public bool IsAvailable => NativeAvailable && State == 2 && labels.Length > 0;

        public static CoreMlObjectDetector Create(ARCameraManager cameraManager, Camera arCamera)
        {
            var go = new GameObject("GhostMapObjectDetector");
            DontDestroyOnLoad(go);
            CoreMlObjectDetector detector = go.AddComponent<CoreMlObjectDetector>();
            detector.cameraManager = cameraManager;
            detector.arCamera = arCamera;
            GhostMapDetector_Load(Path.Combine(Application.streamingAssetsPath, ModelRelativePath));
            return detector;
        }

        private void Update()
        {
            if (!NativeAvailable)
            {
                return;
            }

            int state = State;

            if (state == 2 && labels.Length == 0)
            {
                labels = ReadLabels();
            }
            else if (state == -1 && !loggedState)
            {
                var error = new StringBuilder(512);
                GhostMapDetector_CopyError(error, error.Capacity);
                Debug.LogWarning($"GhostMap yolo: unavailable ({error})");
                loggedState = true;
            }

            if (state != 2 || cameraManager == null || cameraManager.subsystem == null || Time.unscaledTime < nextSubmitAt)
            {
                return;
            }

            nextSubmitAt = Time.unscaledTime + SubmitIntervalSeconds;

            var cameraParams = new XRCameraParams
            {
                zNear = arCamera != null ? arCamera.nearClipPlane : 0.1f,
                zFar = arCamera != null ? arCamera.farClipPlane : 20f,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                screenOrientation = Screen.orientation
            };

            if (cameraManager.subsystem.TryGetLatestFrame(cameraParams, out XRCameraFrame frame) && frame.nativePtr != IntPtr.Zero)
            {
                GhostMapDetector_Submit(frame.nativePtr, Screen.width, Screen.height);
            }
        }

        public bool TryGetLatest(List<ObjectDetection> into, out double timestamp)
        {
            timestamp = 0;

            if (!IsAvailable)
            {
                return false;
            }

            int count = GhostMapDetector_Latest(native, MaxDetections, out long frameId, transform6, out float elapsedMs);

            if (frameId <= lastFrameId)
            {
                return false;
            }

            lastFrameId = frameId;
            timestamp = Time.unscaledTimeAsDouble;
            into.Clear();

            var display = new DisplayTransform(transform6[0], transform6[1], transform6[2], transform6[3], transform6[4], transform6[5]);
            var screen = new Vector2(Screen.width, Screen.height);
            lastDisplay = display;
            lastScreen = screen;

            for (int i = 0; i < count; i++)
            {
                NativeDetection d = native[i];

                if (d.classIndex < 0 || d.classIndex >= labels.Length)
                {
                    continue;
                }

                Rect box = DetectionGeometry.VisionBoxToScreen(new Rect(d.x, d.y, d.width, d.height), display, screen);
                into.Add(new ObjectDetection(labels[d.classIndex], d.confidence, box));
            }

            if (frameId % LogEveryFrames == 1)
            {
                Debug.Log($"GhostMap yolo: {elapsedMs:F1} ms, {count} objects, frame {frameId}");
            }

            return true;
        }

        /// <summary>
        /// Task R11: crops the frame last returned by <see cref="TryGetLatest"/>
        /// (the plugin keeps that one image) and encodes it as JPEG.
        /// </summary>
        public bool TryCapturePhoto(Rect screenBox, out byte[] jpeg)
        {
            jpeg = null;

            if (!IsAvailable || lastFrameId == 0
                || !DetectionGeometry.TryScreenToVisionBox(screenBox, lastDisplay, lastScreen, out Rect upright))
            {
                return false;
            }

            int length = GhostMapDetector_CropJpeg(
                upright.x, upright.y, upright.width, upright.height,
                ObjectPhotoRules.MaxSidePx, ObjectPhotoRules.JpegQuality / 100f,
                photoBuffer, photoBuffer.Length);

            if (length <= 0)
            {
                return false;
            }

            jpeg = new byte[length];
            Buffer.BlockCopy(photoBuffer, 0, jpeg, 0, length);
            return ObjectPhotoRules.IsAcceptable(jpeg);
        }

        private static string[] ReadLabels()
        {
            int count = GhostMapDetector_LabelCount();
            var result = new string[count];
            var buffer = new StringBuilder(64);

            for (int i = 0; i < count; i++)
            {
                buffer.Clear();
                result[i] = GhostMapDetector_CopyLabel(i, buffer, buffer.Capacity) == 1 ? buffer.ToString() : string.Empty;
            }

            return result;
        }
    }
}
