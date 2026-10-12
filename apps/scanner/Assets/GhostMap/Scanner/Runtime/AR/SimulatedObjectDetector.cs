using System.Collections.Generic;
using UnityEngine;

namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// Demo mode's stand-in for YOLO (task R4): reports the simulated room's
    /// furniture that is in view, with the COCO name YOLO would give it, five
    /// times a second. Lets the identify-then-Add-All flow be tried in the
    /// Editor and the iOS Simulator, where there is no camera image.
    /// </summary>
    public sealed class SimulatedObjectDetector : IObjectDetector
    {
        public float IntervalSeconds { get; set; } = ObjectDetectors.ActiveIntervalSeconds;
        public const float Confidence = 0.9f;

        private readonly SimulatedRoom room;
        private readonly Camera camera;
        private double lastTimestamp = double.NegativeInfinity;

        public SimulatedObjectDetector(SimulatedRoom room, Camera camera)
        {
            this.room = room;
            this.camera = camera;
        }

        public bool IsAvailable => room != null && camera != null;

        public bool TryGetLatest(List<ObjectDetection> into, out double timestamp)
        {
            timestamp = Time.unscaledTimeAsDouble;

            if (!IsAvailable || timestamp - lastTimestamp < IntervalSeconds)
            {
                return false;
            }

            lastTimestamp = timestamp;
            into.Clear();

            foreach (SimulatedRoom.Furniture item in room.FurnitureInRoom)
            {
                if (TryProject(item.WorldBounds, out Rect box))
                {
                    into.Add(new ObjectDetection(item.CocoClass, Confidence, box));
                }
            }

            return true;
        }

        /// <summary>
        /// No photos in demo mode: encoding a JPEG in Unity needs the Image
        /// Conversion module, which this project does not include (adding it
        /// is a dependency change, AGENTS.md rule 10). On the phone the native
        /// plugin encodes them.
        /// </summary>
        public bool TryCapturePhoto(Rect screenBox, out byte[] jpeg)
        {
            jpeg = null;
            return false;
        }

        private bool TryProject(Bounds bounds, out Rect box)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 screen = camera.WorldToScreenPoint(corner);

                if (screen.z <= 0.05f)
                {
                    box = default;
                    return false;
                }

                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            box = Rect.MinMaxRect(
                Mathf.Max(min.x, 0f), Mathf.Max(min.y, 0f),
                Mathf.Min(max.x, camera.pixelWidth), Mathf.Min(max.y, camera.pixelHeight));

            // Mostly off screen: a camera would not recognise it either.
            return box.width > 0.15f * (max.x - min.x) && box.height > 0.15f * (max.y - min.y) && box.width > 8f && box.height > 8f;
        }
    }

    /// <summary>No detector: the Editor outside demo mode, or a platform without the model.</summary>
    public sealed class NoObjectDetector : IObjectDetector
    {
        public bool IsAvailable => false;

        public float IntervalSeconds { get; set; }

        public bool TryGetLatest(List<ObjectDetection> into, out double timestamp)
        {
            timestamp = 0;
            return false;
        }

        public bool TryCapturePhoto(Rect screenBox, out byte[] jpeg)
        {
            jpeg = null;
            return false;
        }
    }

    /// <summary>Picks the detector for where the scanner runs.</summary>
    public static class ObjectDetectors
    {
        /// <summary>Five frames a second in the furniture step.</summary>
        public const float ActiveIntervalSeconds = 0.2f;

        /// <summary>
        /// Two a second before it (R2): during the room scan ARKit needs the
        /// phone's capacity to find walls; the detector only collects early
        /// sightings then.
        /// </summary>
        public const float BackgroundIntervalSeconds = 0.5f;

        public static float IntervalFor(Workflow.ScanPhase phase)
            => phase == Workflow.ScanPhase.AddObjects ? ActiveIntervalSeconds : BackgroundIntervalSeconds;

        public static IObjectDetector Create(ArSpatialProvider provider)
        {
            if (provider != null && provider.IsSimulated)
            {
                return new SimulatedObjectDetector(provider.Simulation, provider.ArCamera);
            }

#if UNITY_IOS && !UNITY_EDITOR
            var cameraManager = Object.FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARCameraManager>();
            if (cameraManager != null)
            {
                return CoreMlObjectDetector.Create(cameraManager, provider != null ? provider.ArCamera : null);
            }
#endif
            return new NoObjectDetector();
        }
    }
}
