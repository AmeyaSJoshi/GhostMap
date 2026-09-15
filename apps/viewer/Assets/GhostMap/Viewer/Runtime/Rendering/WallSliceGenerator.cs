using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// One solid rectangular region of a wall, in wall-local coordinates:
    /// <c>u</c> runs along the wall from its start corner, <c>v</c> runs up
    /// from the floor. Slices never overlap and never cover an opening.
    /// </summary>
    public readonly struct WallSlice
    {
        public float MinU { get; }
        public float MaxU { get; }
        public float MinV { get; }
        public float MaxV { get; }

        public WallSlice(float minU, float maxU, float minV, float maxV)
        {
            MinU = minU;
            MaxU = maxU;
            MinV = minV;
            MaxV = maxV;
        }

        public float WidthM => MaxU - MinU;
        public float HeightM => MaxV - MinV;
        public float CenterU => (MinU + MaxU) * 0.5f;
        public float CenterV => (MinV + MaxV) * 0.5f;

        public override string ToString()
            => $"u[{MinU:F3}, {MaxU:F3}] v[{MinV:F3}, {MaxV:F3}]";
    }

    /// <summary>
    /// Task V3: grid-cut wall segmentation, exactly as specified by
    /// implementation plan section 12.3.
    ///
    /// The wall is cut by every opening edge — horizontally at each opening's
    /// near and far edge, vertically at each sill and head — producing a grid
    /// of rectangular cells. A cell whose center lies inside an opening is
    /// dropped; every other cell becomes a solid wall segment. The result
    /// tiles the solid part of the wall exactly once: no overlaps, no gaps,
    /// no duplicates.
    ///
    /// This is deliberately *not* runtime CSG. Mesh booleans are fragile,
    /// non-deterministic across platforms, and expensive to run on every
    /// accepted snapshot; grid decomposition is a few floating-point
    /// comparisons and gives the same answer every time (see
    /// <c>GeneratorIsDeterministic</c> in the tests).
    ///
    /// Pure function: no <c>MonoBehaviour</c>, no GameObjects, no mutation of
    /// its inputs. World-space placement is <see cref="WallRenderer"/>'s job.
    /// </summary>
    public static class WallSliceGenerator
    {
        /// <summary>
        /// Cuts closer together than this are treated as the same cut, and a
        /// cell thinner than this is dropped rather than emitted as a
        /// zero-area sliver.
        /// </summary>
        public const float MinSliceExtentM = 1e-4f;

        public static IReadOnlyList<WallSlice> BuildSlices(
            float wallLengthM,
            float wallHeightM,
            IReadOnlyList<OpeningModel> openings)
        {
            return BuildSlices(wallLengthM, wallHeightM, openings, null);
        }

        /// <param name="diagnostics">
        /// Optional sink for a human-readable line per rejected opening. The
        /// scene store already validates every opening before a snapshot is
        /// accepted, so anything reported here means bad data reached the
        /// renderer by another route — the wall is still rendered solid rather
        /// than crashing, but the reason is surfaced.
        /// </param>
        public static IReadOnlyList<WallSlice> BuildSlices(
            float wallLengthM,
            float wallHeightM,
            IReadOnlyList<OpeningModel> openings,
            IList<string> diagnostics)
        {
            var slices = new List<WallSlice>();

            if (!IsUsableExtent(wallLengthM) || !IsUsableExtent(wallHeightM))
            {
                return slices;
            }

            List<WallSlice> holes = CollectHoles(wallLengthM, wallHeightM, openings, diagnostics);

            List<float> uCuts = CollectCuts(0f, wallLengthM, holes, horizontal: true);
            List<float> vCuts = CollectCuts(0f, wallHeightM, holes, horizontal: false);

            for (int i = 0; i < uCuts.Count - 1; i++)
            {
                float minU = uCuts[i];
                float maxU = uCuts[i + 1];

                if (maxU - minU < MinSliceExtentM)
                {
                    continue;
                }

                for (int j = 0; j < vCuts.Count - 1; j++)
                {
                    float minV = vCuts[j];
                    float maxV = vCuts[j + 1];

                    if (maxV - minV < MinSliceExtentM)
                    {
                        continue;
                    }

                    float centerU = (minU + maxU) * 0.5f;
                    float centerV = (minV + maxV) * 0.5f;

                    if (IsInsideAnyHole(holes, centerU, centerV))
                    {
                        continue;
                    }

                    slices.Add(new WallSlice(minU, maxU, minV, maxV));
                }
            }

            return slices;
        }

        /// <summary>
        /// Turns the openings that belong to this wall into wall-local hole
        /// rectangles, dropping (and reporting) anything that cannot be
        /// rendered safely. An opening that overlaps one already accepted is
        /// rejected here rather than at render time, so two overlapping
        /// openings can never produce overlapping or duplicated geometry.
        /// </summary>
        private static List<WallSlice> CollectHoles(
            float wallLengthM,
            float wallHeightM,
            IReadOnlyList<OpeningModel> openings,
            IList<string> diagnostics)
        {
            var holes = new List<WallSlice>();

            if (openings == null)
            {
                return holes;
            }

            for (int i = 0; i < openings.Count; i++)
            {
                OpeningModel opening = openings[i];

                if (opening == null)
                {
                    Report(diagnostics, $"Opening at index {i} is null; wall rendered solid there.");
                    continue;
                }

                if (!IsFinite(opening.offsetM) ||
                    !IsFinite(opening.widthM) ||
                    !IsFinite(opening.sillHeightM) ||
                    !IsFinite(opening.heightM))
                {
                    Report(diagnostics, $"Opening '{opening.id}' has non-finite dimensions; ignored.");
                    continue;
                }

                if (opening.type != OpeningValidator.TypeDoor &&
                    opening.type != OpeningValidator.TypeWindow)
                {
                    Report(diagnostics, $"Opening '{opening.id}' has unsupported type '{opening.type}'; ignored.");
                    continue;
                }

                if (opening.widthM < MinSliceExtentM || opening.heightM < MinSliceExtentM)
                {
                    Report(diagnostics,
                        $"Opening '{opening.id}' is degenerate " +
                        $"({opening.widthM:F3} x {opening.heightM:F3} m); ignored.");
                    continue;
                }

                if (opening.offsetM < -MinSliceExtentM ||
                    opening.offsetM + opening.widthM > wallLengthM + MinSliceExtentM)
                {
                    Report(diagnostics,
                        $"Opening '{opening.id}' spans {opening.offsetM:F2}-" +
                        $"{opening.offsetM + opening.widthM:F2} m on a {wallLengthM:F2} m wall; ignored.");
                    continue;
                }

                if (opening.sillHeightM < -MinSliceExtentM ||
                    opening.sillHeightM + opening.heightM > wallHeightM + MinSliceExtentM)
                {
                    Report(diagnostics,
                        $"Opening '{opening.id}' spans {opening.sillHeightM:F2}-" +
                        $"{opening.sillHeightM + opening.heightM:F2} m on a {wallHeightM:F2} m wall; ignored.");
                    continue;
                }

                var hole = new WallSlice(
                    Mathf.Clamp(opening.offsetM, 0f, wallLengthM),
                    Mathf.Clamp(opening.offsetM + opening.widthM, 0f, wallLengthM),
                    Mathf.Clamp(opening.sillHeightM, 0f, wallHeightM),
                    Mathf.Clamp(opening.sillHeightM + opening.heightM, 0f, wallHeightM));

                if (TryFindOverlap(holes, hole, out int overlappedIndex))
                {
                    Report(diagnostics,
                        $"Opening '{opening.id}' overlaps an earlier opening on the same wall " +
                        $"({holes[overlappedIndex]}); ignored.");
                    continue;
                }

                holes.Add(hole);
            }

            return holes;
        }

        private static bool TryFindOverlap(
            IReadOnlyList<WallSlice> holes,
            WallSlice candidate,
            out int index)
        {
            for (int i = 0; i < holes.Count; i++)
            {
                WallSlice existing = holes[i];

                bool overlapsU = candidate.MinU < existing.MaxU - MinSliceExtentM
                              && existing.MinU < candidate.MaxU - MinSliceExtentM;
                bool overlapsV = candidate.MinV < existing.MaxV - MinSliceExtentM
                              && existing.MinV < candidate.MaxV - MinSliceExtentM;

                if (overlapsU && overlapsV)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static List<float> CollectCuts(
            float min,
            float max,
            IReadOnlyList<WallSlice> holes,
            bool horizontal)
        {
            var cuts = new List<float> { min, max };

            for (int i = 0; i < holes.Count; i++)
            {
                WallSlice hole = holes[i];
                cuts.Add(horizontal ? hole.MinU : hole.MinV);
                cuts.Add(horizontal ? hole.MaxU : hole.MaxV);
            }

            cuts.Sort();

            var unique = new List<float>(cuts.Count);

            for (int i = 0; i < cuts.Count; i++)
            {
                float cut = Mathf.Clamp(cuts[i], min, max);

                if (unique.Count == 0 || cut - unique[unique.Count - 1] >= MinSliceExtentM)
                {
                    unique.Add(cut);
                }
            }

            return unique;
        }

        private static bool IsInsideAnyHole(IReadOnlyList<WallSlice> holes, float u, float v)
        {
            for (int i = 0; i < holes.Count; i++)
            {
                WallSlice hole = holes[i];

                if (u > hole.MinU && u < hole.MaxU && v > hole.MinV && v < hole.MaxV)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsableExtent(float value)
            => IsFinite(value) && value >= MinSliceExtentM;

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void Report(IList<string> diagnostics, string message)
        {
            diagnostics?.Add(message);
        }
    }
}
