using System.Collections.Generic;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// Task R4: ADR-0011's table from COCO class names (what YOLO11n reports)
    /// to GhostMap furniture types. Anything not listed is ignored.
    /// </summary>
    public static class FurnitureClassMap
    {
        public const string DiningTable = "dining table";

        /// <summary>A matched surface this high (m) and at most <see cref="DeskMaxDepthM"/> deep makes a dining table a desk.</summary>
        public const float DeskMinHeightM = 0.65f;
        public const float DeskMaxHeightM = 0.80f;
        public const float DeskMaxDepthM = 0.80f;

        private static readonly Dictionary<string, string> Direct = new Dictionary<string, string>
        {
            { "bed", "bed" },
            { "chair", "chair" },
            { "couch", "couch" },
            { "tv", "tv" },
            { "refrigerator", "generic" },
            { "potted plant", "generic" },
            { "toilet", "generic" },
            { "sink", "generic" },
            { "oven", "generic" },
            { "microwave", "generic" },
            { "bench", "generic" }
        };

        /// <summary>
        /// The heights (m) of the ARKit surface each class can sit under: a
        /// table's top, a bed's mattress, a chair's or couch's seat. A surface
        /// outside the range is something else nearby (a chair tucked under a
        /// desk must not take the desk's top). Classes not listed never take a
        /// surface.
        /// </summary>
        private static readonly Dictionary<string, (float min, float max)> SurfaceHeights =
            new Dictionary<string, (float min, float max)>
            {
                { DiningTable, (0.55f, 1.00f) },
                { "bed", (0.25f, 0.85f) },
                { "chair", (0.35f, 0.60f) },
                { "couch", (0.30f, 0.60f) },
                { "bench", (0.35f, 0.60f) }
            };

        /// <summary>Whether YOLO's class is furniture GhostMap records.</summary>
        public static bool IsFurniture(string className)
            => className == DiningTable || (className != null && Direct.ContainsKey(className));

        /// <summary>
        /// The GhostMap type for a detection. <c>dining table</c> becomes a
        /// <c>desk</c> when its matched surface is desk-shaped (COCO has no desk
        /// class), otherwise a <c>table</c>.
        /// </summary>
        /// <param name="surface">The measured top surface under the detection, if one matched.</param>
        public static bool TryMap(string className, FurnitureCandidate? surface, out string type)
        {
            if (className == DiningTable)
            {
                type = surface.HasValue && IsDeskShaped(surface.Value) ? "desk" : "table";
                return true;
            }

            if (className != null && Direct.TryGetValue(className, out type))
            {
                return true;
            }

            type = null;
            return false;
        }

        /// <summary>Whether a measured surface at this height can be the top of this class.</summary>
        public static bool CanSitUnder(string className, float surfaceHeightM)
        {
            return className != null
                && SurfaceHeights.TryGetValue(className, out (float min, float max) range)
                && surfaceHeightM >= range.min
                && surfaceHeightM <= range.max;
        }

        /// <summary>
        /// Types whose measured surface is a seat, not the top: their height
        /// is at least the type's default, because ARKit never sees the back.
        /// </summary>
        public static bool SurfaceIsSeat(string type) => type == "chair" || type == "couch";

        public static bool IsDeskShaped(FurnitureCandidate surface)
        {
            float depth = surface.WidthM < surface.DepthM ? surface.WidthM : surface.DepthM;
            return surface.HeightM >= DeskMinHeightM && surface.HeightM <= DeskMaxHeightM && depth <= DeskMaxDepthM;
        }
    }
}
