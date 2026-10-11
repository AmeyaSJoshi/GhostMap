using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// ADR-0007: pre-selects a furniture type for a detected surface from
    /// ARKit's own plane label and the size GhostMap measured, so the user
    /// usually only has to tap Add.
    ///
    /// <para>It is a suggestion, never a decision: the user can change it, and
    /// a type the user picked always wins. ARKit only distinguishes Table and
    /// Seat, so desk / table / dresser, chair / couch and bed are told apart
    /// by measured height and footprint.</para>
    ///
    /// <para>Detected extents are a lower bound (ARKit reports what it has
    /// seen), so the size thresholds are deliberately on the small side of
    /// real furniture.</para>
    ///
    /// <para>Plain C#, so every rule is testable without a device.</para>
    /// </summary>
    public static class FurnitureTypeSuggester
    {
        public const string Fallback = "generic";

        /// <summary>A seat-labelled surface at least this long is a couch.</summary>
        public const float CouchMinLengthM = 1.2f;

        /// <summary>A surface at least this long and wide, low down, is a bed.</summary>
        public const float BedMinLengthM = 1.6f;
        public const float BedMinWidthM = 0.85f;
        public const float BedMaxHeightM = 0.75f;

        /// <summary>Table height band: below is a coffee table, above is a dresser or counter.</summary>
        public const float DeskMinHeightM = 0.62f;
        public const float DeskMaxHeightM = 0.85f;

        /// <summary>A table-height surface at least this deep is a table rather than a desk.</summary>
        public const float TableMinDepthM = 0.80f;

        public static string Suggest(
            PlaneClassifications classifications, float widthM, float depthM, float heightM)
        {
            float length = Mathf.Max(widthM, depthM);
            float width = Mathf.Min(widthM, depthM);

            bool looksLikeBed =
                length >= BedMinLengthM && width >= BedMinWidthM && heightM <= BedMaxHeightM;

            if ((classifications & PlaneClassifications.SeatOfAnyType) != 0)
            {
                // ARKit may call a mattress a seat; a bed-sized seat is a bed.
                if (looksLikeBed && width >= 1.0f)
                {
                    return "bed";
                }

                return (classifications & PlaneClassifications.Couch) != 0 || length >= CouchMinLengthM
                    ? "couch"
                    : "chair";
            }

            if ((classifications & PlaneClassifications.Table) != 0)
            {
                return ByTableHeight(width, heightM, belowBand: "table");
            }

            // ARKit has not labelled it (yet). Size alone, and only where the
            // shape is distinctive; otherwise say nothing specific.
            if (looksLikeBed)
            {
                return "bed";
            }

            return ByTableHeight(width, heightM, belowBand: Fallback);
        }

        private static string ByTableHeight(float width, float heightM, string belowBand)
        {
            if (heightM > DeskMaxHeightM)
            {
                return "dresser";
            }

            if (heightM >= DeskMinHeightM)
            {
                return width >= TableMinDepthM ? "table" : "desk";
            }

            return belowBand;
        }
    }
}
