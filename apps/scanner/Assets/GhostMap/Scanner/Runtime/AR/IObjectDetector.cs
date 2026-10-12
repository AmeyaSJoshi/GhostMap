using System.Collections.Generic;
using UnityEngine;

namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// One object the camera-image detector found (ADR-0011): a COCO class
    /// name, its confidence, and where it is on screen.
    /// </summary>
    public readonly struct ObjectDetection
    {
        public ObjectDetection(string className, float confidence, Rect screenBox)
        {
            ClassName = className ?? string.Empty;
            Confidence = confidence;
            ScreenBox = screenBox;
        }

        /// <summary>COCO class name as the model reports it, e.g. <c>"dining table"</c>.</summary>
        public string ClassName { get; }

        /// <summary>0 to 1.</summary>
        public float Confidence { get; }

        /// <summary>Unity screen pixels, bottom-left origin: the space <see cref="ISpatialProvider.GetScreenRay"/> takes.</summary>
        public Rect ScreenBox { get; }
    }

    /// <summary>
    /// Task R4 (ADR-0011): the seam onto the on-device furniture detector, so
    /// every identification rule is testable in EditMode with a fake, the same
    /// pattern as <see cref="ISpatialProvider"/>. On an iPhone it is the
    /// YOLO11n Core ML model (<see cref="CoreMlObjectDetector"/>); in demo mode
    /// it reports the simulated room's furniture.
    ///
    /// <para>The detector only names things. Geometry always comes from ARKit
    /// surfaces and GhostMap's own maths (AGENTS.md rule 6).</para>
    /// </summary>
    public interface IObjectDetector
    {
        /// <summary>False until a model is loaded, and where none exists (Editor without demo mode).</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Fills <paramref name="into"/> with the newest frame's detections.
        /// Returns true only when that frame is newer than the one returned by
        /// the previous call, so a caller polling every Update counts each
        /// detector frame once.
        /// </summary>
        bool TryGetLatest(List<ObjectDetection> into, out double timestamp);

        /// <summary>
        /// Task R11: a JPEG of part of the camera image from the frame last
        /// returned by <see cref="TryGetLatest"/> (screen pixels, bottom-left
        /// origin), at most <c>ObjectPhotoRules.MaxSidePx</c> on its longest
        /// side. False when there is no frame or the box is off the image.
        /// </summary>
        bool TryCapturePhoto(Rect screenBox, out byte[] jpeg);
    }
}
