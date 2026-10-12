using UnityEngine;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// ARKit's <c>ARFrame.displayTransform(for:viewportSize:)</c> as six
    /// numbers, in Core Graphics order: <c>x' = a x + c y + tx</c>,
    /// <c>y' = b x + d y + ty</c>. It maps normalized coordinates in the raw
    /// captured image (origin top-left) to normalized view coordinates (origin
    /// top-left), including the rotation to portrait and the aspect-fill crop.
    /// </summary>
    public readonly struct DisplayTransform
    {
        public DisplayTransform(float a, float b, float c, float d, float tx, float ty)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            Tx = tx;
            Ty = ty;
        }

        public float A { get; }
        public float B { get; }
        public float C { get; }
        public float D { get; }
        public float Tx { get; }
        public float Ty { get; }

        public static DisplayTransform Identity => new DisplayTransform(1f, 0f, 0f, 1f, 0f, 0f);

        public Vector2 Apply(Vector2 p) => new Vector2(A * p.x + C * p.y + Tx, B * p.x + D * p.y + Ty);

        /// <summary>The point <see cref="Apply"/> maps to <paramref name="v"/>; false for a degenerate transform.</summary>
        public bool TryInvert(Vector2 v, out Vector2 p)
        {
            float det = A * D - C * B;

            if (Mathf.Abs(det) < 1e-8f)
            {
                p = default;
                return false;
            }

            float x = v.x - Tx;
            float y = v.y - Ty;
            p = new Vector2((D * x - C * y) / det, (A * y - B * x) / det);
            return true;
        }
    }

    /// <summary>
    /// Task R4: puts a Vision bounding box on the Unity screen.
    ///
    /// <para>The native plugin runs Vision with the image orientation
    /// <c>.right</c> (the back camera's sensor is landscape; .right turns it
    /// upright for a portrait phone), so YOLO sees furniture the right way up.
    /// Vision then reports boxes normalized in that <b>upright</b> image with a
    /// bottom-left origin. This undoes the orientation to get raw-image
    /// coordinates (top-left origin), applies ARKit's display transform, and
    /// scales to Unity screen pixels (bottom-left origin).</para>
    ///
    /// <para>Kept in C#, not in the plugin, so it is unit-tested.</para>
    /// </summary>
    public static class DetectionGeometry
    {
        /// <summary>
        /// Upright (orientation .right) normalized point, bottom-left origin,
        /// to raw captured-image normalized point, top-left origin. Rotating
        /// the raw image 90° clockwise sends raw (x, y) to upright
        /// (1 - y, x) in top-left coordinates; this is the inverse.
        /// </summary>
        public static Vector2 UprightToRawImage(Vector2 upright)
            => new Vector2(1f - upright.y, 1f - upright.x);

        /// <summary>The screen-pixel box (bottom-left origin) covering a Vision box.</summary>
        public static Rect VisionBoxToScreen(Rect uprightBox, DisplayTransform transform, Vector2 screenSize)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            Vector2[] corners =
            {
                new Vector2(uprightBox.xMin, uprightBox.yMin),
                new Vector2(uprightBox.xMax, uprightBox.yMin),
                new Vector2(uprightBox.xMin, uprightBox.yMax),
                new Vector2(uprightBox.xMax, uprightBox.yMax)
            };

            foreach (Vector2 corner in corners)
            {
                Vector2 view = transform.Apply(UprightToRawImage(corner));
                var screen = new Vector2(view.x * screenSize.x, (1f - view.y) * screenSize.y);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>The point the floor ray goes through: the middle of the box's bottom edge.</summary>
        /// <summary>
        /// The inverse of <see cref="VisionBoxToScreen"/>: a screen box (Unity
        /// pixels, bottom-left origin) as a box in the upright camera image
        /// (normalized, Vision's bottom-left origin), clamped to the image.
        /// Task R11 crops object photos with it.
        /// </summary>
        public static bool TryScreenToVisionBox(Rect screenBox, DisplayTransform transform, Vector2 screenSize, out Rect uprightBox)
        {
            uprightBox = default;

            if (screenSize.x <= 0f || screenSize.y <= 0f)
            {
                return false;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            Vector2[] corners =
            {
                new Vector2(screenBox.xMin, screenBox.yMin),
                new Vector2(screenBox.xMax, screenBox.yMin),
                new Vector2(screenBox.xMin, screenBox.yMax),
                new Vector2(screenBox.xMax, screenBox.yMax)
            };

            foreach (Vector2 corner in corners)
            {
                var view = new Vector2(corner.x / screenSize.x, 1f - corner.y / screenSize.y);

                if (!transform.TryInvert(view, out Vector2 raw))
                {
                    return false;
                }

                // UprightToRawImage is its own inverse.
                Vector2 upright = UprightToRawImage(raw);
                min = Vector2.Min(min, upright);
                max = Vector2.Max(max, upright);
            }

            min = Vector2.Max(min, Vector2.zero);
            max = Vector2.Min(max, Vector2.one);

            if (max.x <= min.x || max.y <= min.y)
            {
                return false;
            }

            uprightBox = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        public static Vector2 BottomCentre(Rect screenBox) => new Vector2(screenBox.center.x, screenBox.yMin);
    }
}
