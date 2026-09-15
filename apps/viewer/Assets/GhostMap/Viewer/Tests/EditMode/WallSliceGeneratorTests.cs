using System.Collections.Generic;
using System.Linq;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V3's <see cref="WallSliceGenerator"/>: the grid-cut wall
    /// segmentation from implementation plan section 12.3.
    ///
    /// Everything here is expressed in wall-local coordinates: <c>u</c> runs
    /// along the wall from the start corner, <c>v</c> runs up from the floor.
    /// The generator knows nothing about which wall it is cutting — matching
    /// an opening to its ordered corner pair is <see cref="WallRenderer"/>'s
    /// job, and is tested there.
    /// </summary>
    public sealed class WallSliceGeneratorTests
    {
        private const float WallLength = 4f;
        private const float WallHeight = 2.5f;
        private const float Tolerance = 1e-4f;

        private static OpeningModel Door(
            float offsetM,
            float widthM = 0.9f,
            float heightM = 2.05f,
            string id = "door-1")
        {
            return new OpeningModel
            {
                id = id,
                type = "door",
                wallStartCornerId = "c0",
                wallEndCornerId = "c1",
                offsetM = offsetM,
                widthM = widthM,
                sillHeightM = 0f,
                heightM = heightM
            };
        }

        private static OpeningModel Window(
            float offsetM,
            float widthM = 1.2f,
            float sillHeightM = 0.9f,
            float heightM = 1.1f,
            string id = "window-1")
        {
            return new OpeningModel
            {
                id = id,
                type = "window",
                wallStartCornerId = "c0",
                wallEndCornerId = "c1",
                offsetM = offsetM,
                widthM = widthM,
                sillHeightM = sillHeightM,
                heightM = heightM
            };
        }

        private static float TotalArea(IReadOnlyList<WallSlice> slices)
            => slices.Sum(s => s.WidthM * s.HeightM);

        private static float OverlapArea(WallSlice a, WallSlice b)
        {
            float du = Mathf.Min(a.MaxU, b.MaxU) - Mathf.Max(a.MinU, b.MinU);
            float dv = Mathf.Min(a.MaxV, b.MaxV) - Mathf.Max(a.MinV, b.MinV);
            return du <= 0f || dv <= 0f ? 0f : du * dv;
        }

        private static void AssertNoOverlaps(IReadOnlyList<WallSlice> slices)
        {
            for (int i = 0; i < slices.Count; i++)
            {
                for (int j = i + 1; j < slices.Count; j++)
                {
                    Assert.AreEqual(
                        0f,
                        OverlapArea(slices[i], slices[j]),
                        Tolerance,
                        $"Slices {i} and {j} overlap: {slices[i]} vs {slices[j]}.");
                }
            }
        }

        private static bool Contains(WallSlice slice, float u, float v)
            => u >= slice.MinU && u <= slice.MaxU && v >= slice.MinV && v <= slice.MaxV;

        private static bool InsideOpening(OpeningModel opening, float u, float v)
            => u > opening.offsetM
            && u < opening.offsetM + opening.widthM
            && v > opening.sillHeightM
            && v < opening.sillHeightM + opening.heightM;

        /// <summary>
        /// Samples the whole wall on a fine grid: every point that is not
        /// inside an opening must be covered by exactly one slice, and every
        /// point inside an opening must be covered by none. This catches both
        /// gaps and double-coverage that an area total alone could mask.
        ///
        /// Coverage is only well defined in the interior of a cell — adjacent
        /// slices legitimately share an edge, and an opening's own edge belongs
        /// to neither the hole nor the wall — so samples that land on (or
        /// within floating-point reach of) any cut line are skipped rather
        /// than counted.
        /// </summary>
        private static void AssertExactCoverage(
            IReadOnlyList<WallSlice> slices,
            float wallLength,
            float wallHeight,
            params OpeningModel[] openings)
        {
            const int steps = 61;
            const float edgeMargin = 1e-3f;

            var uEdges = new List<float> { 0f, wallLength };
            var vEdges = new List<float> { 0f, wallHeight };

            foreach (OpeningModel opening in openings)
            {
                uEdges.Add(opening.offsetM);
                uEdges.Add(opening.offsetM + opening.widthM);
                vEdges.Add(opening.sillHeightM);
                vEdges.Add(opening.sillHeightM + opening.heightM);
            }

            foreach (WallSlice slice in slices)
            {
                uEdges.Add(slice.MinU);
                uEdges.Add(slice.MaxU);
                vEdges.Add(slice.MinV);
                vEdges.Add(slice.MaxV);
            }

            bool NearEdge(IEnumerable<float> edges, float value)
                => edges.Any(e => Mathf.Abs(e - value) < edgeMargin);

            int sampled = 0;

            for (int i = 0; i < steps; i++)
            {
                float u = wallLength * (i + 0.5f) / steps;

                if (NearEdge(uEdges, u))
                {
                    continue;
                }

                for (int j = 0; j < steps; j++)
                {
                    float v = wallHeight * (j + 0.5f) / steps;

                    if (NearEdge(vEdges, v))
                    {
                        continue;
                    }

                    sampled++;

                    bool hole = openings.Any(o => InsideOpening(o, u, v));
                    int covering = slices.Count(s => Contains(s, u, v));

                    if (hole)
                    {
                        Assert.AreEqual(0, covering, $"Point ({u:F3}, {v:F3}) is inside an opening but covered.");
                    }
                    else
                    {
                        Assert.AreEqual(1, covering, $"Point ({u:F3}, {v:F3}) is solid wall but covered {covering} times.");
                    }
                }
            }

            Assert.Greater(sampled, 1000, "Too few interior samples to be a meaningful coverage check.");
        }

        [Test]
        public void WallWithNoOpeningsProducesOneFullSlice()
        {
            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, null);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(0f, slices[0].MinU, Tolerance);
            Assert.AreEqual(WallLength, slices[0].MaxU, Tolerance);
            Assert.AreEqual(0f, slices[0].MinV, Tolerance);
            Assert.AreEqual(WallHeight, slices[0].MaxV, Tolerance);
            Assert.AreEqual(WallLength * WallHeight, TotalArea(slices), Tolerance);
        }

        [Test]
        public void EmptyOpeningArrayProducesOneFullSlice()
        {
            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new OpeningModel[0]);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(WallLength * WallHeight, TotalArea(slices), Tolerance);
        }

        [Test]
        public void DoorRemovesExactlyTheDoorRegion()
        {
            OpeningModel door = Door(1.2f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            foreach (WallSlice slice in slices)
            {
                Assert.IsFalse(
                    InsideOpening(door, slice.CenterU, slice.CenterV),
                    $"Slice {slice} sits inside the door.");
            }

            AssertNoOverlaps(slices);
            AssertExactCoverage(slices, WallLength, WallHeight, door);
        }

        [Test]
        public void DoorWithZeroSillHasNoSliceBelowIt()
        {
            OpeningModel door = Door(1.2f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            bool below = slices.Any(s =>
                s.MinU >= door.offsetM - Tolerance &&
                s.MaxU <= door.offsetM + door.widthM + Tolerance &&
                s.MaxV <= door.sillHeightM + Tolerance);

            Assert.IsFalse(below, "A door with sill 0 cannot have wall beneath it.");
        }

        [Test]
        public void DoorLeavesSideAndHeaderSegments()
        {
            OpeningModel door = Door(1.2f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            float doorEnd = door.offsetM + door.widthM;

            Assert.IsTrue(
                slices.Any(s => s.MaxU <= door.offsetM + Tolerance),
                "Expected wall to the left of the door.");
            Assert.IsTrue(
                slices.Any(s => s.MinU >= doorEnd - Tolerance),
                "Expected wall to the right of the door.");
            Assert.IsTrue(
                slices.Any(s =>
                    s.MinV >= door.heightM - Tolerance &&
                    s.MinU >= door.offsetM - Tolerance &&
                    s.MaxU <= doorEnd + Tolerance),
                "Expected a header segment above the door.");
        }

        [Test]
        public void WindowLeavesSillSegmentBelowIt()
        {
            OpeningModel window = Window(0.9f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { window });

            float windowEnd = window.offsetM + window.widthM;

            Assert.IsTrue(
                slices.Any(s =>
                    s.MaxV <= window.sillHeightM + Tolerance &&
                    s.MinU >= window.offsetM - Tolerance &&
                    s.MaxU <= windowEnd + Tolerance),
                "A window with a positive sill must keep wall beneath it.");
            Assert.IsTrue(
                slices.Any(s =>
                    s.MinV >= window.sillHeightM + window.heightM - Tolerance &&
                    s.MinU >= window.offsetM - Tolerance &&
                    s.MaxU <= windowEnd + Tolerance),
                "Expected a header segment above the window.");
        }

        [Test]
        public void WindowRemovesExactlyTheWindowRegion()
        {
            OpeningModel window = Window(0.9f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { window });

            AssertNoOverlaps(slices);
            AssertExactCoverage(slices, WallLength, WallHeight, window);
        }

        [Test]
        public void OpeningWidthIsHonouredExactly()
        {
            OpeningModel door = Door(1.2f, widthM: 0.9f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            Assert.IsTrue(slices.Any(s => Mathf.Abs(s.MaxU - 1.2f) < Tolerance),
                "Expected a cut at the door's near edge (u = 1.2).");
            Assert.IsTrue(slices.Any(s => Mathf.Abs(s.MinU - 2.1f) < Tolerance),
                "Expected a cut at the door's far edge (u = 2.1).");
        }

        [Test]
        public void OpeningHeightIsHonouredExactly()
        {
            OpeningModel window = Window(0.9f, sillHeightM: 0.9f, heightM: 1.1f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { window });

            Assert.IsTrue(slices.Any(s => Mathf.Abs(s.MaxV - 0.9f) < Tolerance),
                "Expected a cut at the window sill (v = 0.9).");
            Assert.IsTrue(slices.Any(s => Mathf.Abs(s.MinV - 2.0f) < Tolerance),
                "Expected a cut at the window top (v = 2.0).");
        }

        [Test]
        public void HorizontalOffsetIsMeasuredFromWallStart()
        {
            IReadOnlyList<WallSlice> near =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { Door(0.5f) });
            IReadOnlyList<WallSlice> far =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { Door(2.5f) });

            float nearLeft = near.Where(s => s.MaxU <= 0.5f + Tolerance).Sum(s => s.WidthM * s.HeightM);
            float farLeft = far.Where(s => s.MaxU <= 2.5f + Tolerance).Sum(s => s.WidthM * s.HeightM);

            Assert.Less(nearLeft, farLeft,
                "A larger offset must leave more solid wall between the start corner and the opening.");
        }

        [Test]
        public void OpeningFlushWithWallStartLeavesNoLeftSegment()
        {
            OpeningModel door = Door(0f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            Assert.IsFalse(slices.Any(s => s.MaxU <= Tolerance),
                "A zero-width left segment must not be emitted.");
            AssertNoOverlaps(slices);
            AssertExactCoverage(slices, WallLength, WallHeight, door);
        }

        [Test]
        public void OpeningFlushWithWallEndLeavesNoRightSegment()
        {
            OpeningModel door = Door(WallLength - 0.9f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            Assert.IsFalse(slices.Any(s => s.MinU >= WallLength - Tolerance),
                "A zero-width right segment must not be emitted.");
            AssertNoOverlaps(slices);
            AssertExactCoverage(slices, WallLength, WallHeight, door);
        }

        [Test]
        public void FullHeightDoorLeavesNoHeaderSegment()
        {
            OpeningModel door = Door(1.2f, heightM: WallHeight);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            Assert.IsFalse(
                slices.Any(s =>
                    s.MinU >= door.offsetM - Tolerance &&
                    s.MaxU <= door.offsetM + door.widthM + Tolerance),
                "A full-height door leaves nothing above or below it.");
            AssertExactCoverage(slices, WallLength, WallHeight, door);
        }

        [Test]
        public void TwoNonOverlappingOpeningsOnOneWallBothCutThrough()
        {
            OpeningModel door = Door(0.5f);
            OpeningModel window = Window(2.2f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door, window });

            AssertNoOverlaps(slices);
            AssertExactCoverage(slices, WallLength, WallHeight, door, window);
        }

        [Test]
        public void SolidAreaEqualsWallAreaMinusOpeningAreas()
        {
            OpeningModel door = Door(0.5f);
            OpeningModel window = Window(2.2f);

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door, window });

            float expected = WallLength * WallHeight
                           - door.widthM * door.heightM
                           - window.widthM * window.heightM;

            Assert.AreEqual(expected, TotalArea(slices), Tolerance);
        }

        [Test]
        public void NoSliceHasZeroOrNegativeExtent()
        {
            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { Door(0f), Window(2.2f) });

            foreach (WallSlice slice in slices)
            {
                Assert.Greater(slice.WidthM, 0f, $"Degenerate slice {slice}.");
                Assert.Greater(slice.HeightM, 0f, $"Degenerate slice {slice}.");
            }
        }

        [Test]
        public void NullOpeningIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new OpeningModel[] { null }, diagnostics);

            Assert.AreEqual(1, slices.Count, "A null opening must leave the wall solid.");
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void NonFiniteOpeningIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();
            OpeningModel broken = Door(float.NaN);

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { broken }, diagnostics);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(WallLength * WallHeight, TotalArea(slices), Tolerance);
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void OpeningPastWallEndIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { Door(3.8f, widthM: 0.9f) }, diagnostics);

            Assert.AreEqual(1, slices.Count, "An opening that does not fit must not cut the wall.");
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void NegativeOffsetIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { Door(-0.5f) }, diagnostics);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void OpeningTallerThanWallIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { Door(1.2f, heightM: 3.5f) }, diagnostics);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void NegativeSillIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { Window(1.0f, sillHeightM: -0.4f) }, diagnostics);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void ZeroWidthOpeningIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { Door(1.2f, widthM: 0f) }, diagnostics);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void UnsupportedOpeningTypeIsIgnoredAndDiagnosed()
        {
            var diagnostics = new List<string>();
            OpeningModel hatch = Door(1.2f);
            hatch.type = "hatch";

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { hatch }, diagnostics);

            Assert.AreEqual(1, slices.Count);
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void OverlappingOpeningIsRejectedBeforeRendering()
        {
            var diagnostics = new List<string>();
            OpeningModel first = Door(1.0f, widthM: 1.0f, id: "door-1");
            OpeningModel second = Door(1.5f, widthM: 1.0f, id: "door-2");

            IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                WallLength, WallHeight, new[] { first, second }, diagnostics);

            float expected = WallLength * WallHeight - first.widthM * first.heightM;

            Assert.AreEqual(expected, TotalArea(slices), Tolerance,
                "Only the first of two overlapping openings may cut the wall.");
            Assert.AreEqual(1, diagnostics.Count);
            AssertNoOverlaps(slices);
            AssertExactCoverage(slices, WallLength, WallHeight, first);
        }

        [Test]
        public void VerticallySeparatedOpeningsAtTheSameOffsetBothCut()
        {
            // Not an overlap: same horizontal span, disjoint vertical spans.
            OpeningModel low = Window(1.0f, widthM: 1.0f, sillHeightM: 0.2f, heightM: 0.6f, id: "w-low");
            OpeningModel high = Window(1.0f, widthM: 1.0f, sillHeightM: 1.4f, heightM: 0.6f, id: "w-high");

            IReadOnlyList<WallSlice> slices =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { low, high });

            float expected = WallLength * WallHeight
                           - low.widthM * low.heightM
                           - high.widthM * high.heightM;

            Assert.AreEqual(expected, TotalArea(slices), Tolerance);
            AssertNoOverlaps(slices);
            AssertExactCoverage(slices, WallLength, WallHeight, low, high);
        }

        [Test]
        public void DegenerateWallLengthProducesNoSlices()
        {
            Assert.AreEqual(0, WallSliceGenerator.BuildSlices(0f, WallHeight, null).Count);
            Assert.AreEqual(0, WallSliceGenerator.BuildSlices(-2f, WallHeight, null).Count);
            Assert.AreEqual(0, WallSliceGenerator.BuildSlices(float.NaN, WallHeight, null).Count);
        }

        [Test]
        public void DegenerateWallHeightProducesNoSlices()
        {
            Assert.AreEqual(0, WallSliceGenerator.BuildSlices(WallLength, 0f, null).Count);
            Assert.AreEqual(0, WallSliceGenerator.BuildSlices(WallLength, -1f, null).Count);
            Assert.AreEqual(0, WallSliceGenerator.BuildSlices(WallLength, float.PositiveInfinity, null).Count);
        }

        [Test]
        public void GeneratorIsDeterministic()
        {
            var openings = new[] { Door(0.5f), Window(2.2f) };

            IReadOnlyList<WallSlice> first =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, openings);
            IReadOnlyList<WallSlice> second =
                WallSliceGenerator.BuildSlices(WallLength, WallHeight, openings);

            Assert.AreEqual(first.Count, second.Count);

            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].MinU, second[i].MinU, Tolerance);
                Assert.AreEqual(first[i].MaxU, second[i].MaxU, Tolerance);
                Assert.AreEqual(first[i].MinV, second[i].MinV, Tolerance);
                Assert.AreEqual(first[i].MaxV, second[i].MaxV, Tolerance);
            }
        }

        [Test]
        public void GeneratorDoesNotMutateTheOpeningsItIsGiven()
        {
            OpeningModel door = Door(1.2f);

            WallSliceGenerator.BuildSlices(WallLength, WallHeight, new[] { door });

            Assert.AreEqual(1.2f, door.offsetM, Tolerance);
            Assert.AreEqual(0.9f, door.widthM, Tolerance);
            Assert.AreEqual(0f, door.sillHeightM, Tolerance);
            Assert.AreEqual(2.05f, door.heightM, Tolerance);
        }
    }
}
