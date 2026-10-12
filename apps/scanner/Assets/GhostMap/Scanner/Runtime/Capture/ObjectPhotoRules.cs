using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using UnityEngine;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// Task R11: the rules for the photo kept with each furniture object and
    /// exported beside it (<c>docs/contracts/export-bundle-v1.md</c>, section
    /// 1). A photo only shows what the object is; it never sets a dimension.
    /// </summary>
    public static class ObjectPhotoRules
    {
        /// <summary>Longest side of a photo, in pixels.</summary>
        public const int MaxSidePx = 480;

        /// <summary>JPEG quality, 0-100.</summary>
        public const int JpegQuality = 70;

        /// <summary>A larger photo is refused (contract limit).</summary>
        public const int MaxBytes = 256 * 1024;

        /// <summary>Margin added around an object's screen box, as a fraction of its size.</summary>
        public const float PaddingFraction = 0.1f;

        /// <summary>At least this much of an object's screen box must be on screen to photograph it.</summary>
        public const float MinVisibleFraction = 0.6f;

        /// <summary>Nearer than this to the camera plane (m), a corner counts as behind it.</summary>
        private const float MinDepthM = 0.05f;

        /// <summary>A JPEG (starts FF D8 FF) no larger than <see cref="MaxBytes"/>.</summary>
        public static bool IsAcceptable(byte[] photo)
        {
            return photo != null
                && photo.Length >= 4
                && photo.Length <= MaxBytes
                && photo[0] == 0xFF && photo[1] == 0xD8 && photo[2] == 0xFF;
        }

        /// <summary>The box grown by <see cref="PaddingFraction"/> and clamped to the screen.</summary>
        public static Rect Pad(Rect box, Vector2 screenSize)
        {
            float dx = box.width * PaddingFraction;
            float dy = box.height * PaddingFraction;
            return Rect.MinMaxRect(
                Mathf.Max(0f, box.xMin - dx),
                Mathf.Max(0f, box.yMin - dy),
                Mathf.Min(screenSize.x, box.xMax + dx),
                Mathf.Min(screenSize.y, box.yMax + dy));
        }

        /// <summary>
        /// Where a placed object is on screen right now, padded, for a photo
        /// of it. False when it is behind the camera or mostly off screen.
        /// </summary>
        /// <param name="worldToScreen">A camera's WorldToScreenPoint: x, y in pixels, z the depth in metres.</param>
        public static bool TryFrameObject(
            SceneObjectModel model,
            GhostCoordinateFrame frame,
            Func<Vector3, Vector3> worldToScreen,
            Vector2 screenSize,
            out Rect box)
        {
            box = default;

            if (model == null || frame == null || worldToScreen == null || screenSize.x <= 0f || screenSize.y <= 0f)
            {
                return false;
            }

            Vector3 center = model.center.ToVector3();
            Quaternion yaw = Quaternion.Euler(0f, model.yawDeg, 0f);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < 8; i++)
            {
                var local = new Vector3(
                    ((i & 1) == 0 ? -0.5f : 0.5f) * model.widthM,
                    (i & 2) == 0 ? 0f : model.heightM,
                    ((i & 4) == 0 ? -0.5f : 0.5f) * model.depthM);
                Vector3 ghost = new Vector3(center.x, 0f, center.z) + yaw * local;
                Vector3 screen = worldToScreen(frame.GhostToWorld(ghost));

                if (screen.z <= MinDepthM)
                {
                    return false;
                }

                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            Rect full = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            Rect visible = Rect.MinMaxRect(
                Mathf.Max(0f, full.xMin), Mathf.Max(0f, full.yMin),
                Mathf.Min(screenSize.x, full.xMax), Mathf.Min(screenSize.y, full.yMax));

            if (visible.width <= 0f || visible.height <= 0f
                || visible.width * visible.height < MinVisibleFraction * full.width * full.height)
            {
                return false;
            }

            box = Pad(visible, screenSize);
            return true;
        }
    }
}
