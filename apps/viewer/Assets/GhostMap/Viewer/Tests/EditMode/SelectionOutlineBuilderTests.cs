using GhostMap.Viewer.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V5's selection-highlight geometry: a deterministic 12-edge
    /// wireframe box, in the same local frame <c>FurnitureFactory</c> uses.
    /// </summary>
    public sealed class SelectionOutlineBuilderTests
    {
        [Test]
        public void BuildEdgesReturnsExactlyTwelveBars()
        {
            SelectionEdgeBar[] edges = SelectionOutlineBuilder.BuildEdges(1f, 1f, 1f);

            Assert.AreEqual(12, edges.Length);
        }

        [Test]
        public void EveryBarHasAPositiveSizeOnEveryAxis()
        {
            SelectionEdgeBar[] edges = SelectionOutlineBuilder.BuildEdges(1.52f, 2.03f, 0.6f);

            foreach (SelectionEdgeBar bar in edges)
            {
                Assert.Greater(bar.LocalSize.x, 0f);
                Assert.Greater(bar.LocalSize.y, 0f);
                Assert.Greater(bar.LocalSize.z, 0f);
            }
        }

        [Test]
        public void TheUnionOfEdgesSpansExactlyTheInflatedBoxPlusBarThickness()
        {
            const float width = 2f;
            const float depth = 1f;
            const float height = 0.9f;
            const float margin = SelectionOutlineBuilder.DefaultMarginM;
            const float thickness = SelectionOutlineBuilder.DefaultThicknessM;

            SelectionEdgeBar[] edges = SelectionOutlineBuilder.BuildEdges(width, depth, height);

            var bounds = new Bounds(edges[0].LocalCenter, Vector3.zero);
            foreach (SelectionEdgeBar bar in edges)
            {
                bounds.Encapsulate(new Bounds(bar.LocalCenter, bar.LocalSize));
            }

            // Each bar is *centered* on the box edge it traces, so its outer
            // face pokes half a bar-thickness beyond the margin boundary.
            Assert.AreEqual(width + 2f * margin + thickness, bounds.size.x, 1e-4f);
            Assert.AreEqual(depth + 2f * margin + thickness, bounds.size.z, 1e-4f);
            Assert.AreEqual(height + 2f * margin + thickness, bounds.size.y, 1e-4f);
        }

        [Test]
        public void BoxSitsOnTheFloorWithinTheMarginPlusHalfBarThickness()
        {
            SelectionEdgeBar[] edges = SelectionOutlineBuilder.BuildEdges(1f, 1f, 1f);

            float minY = float.PositiveInfinity;
            foreach (SelectionEdgeBar bar in edges)
            {
                minY = Mathf.Min(minY, bar.LocalCenter.y - bar.LocalSize.y * 0.5f);
            }

            float expected = -(SelectionOutlineBuilder.DefaultMarginM + SelectionOutlineBuilder.DefaultThicknessM * 0.5f);
            Assert.AreEqual(expected, minY, 1e-4f);
        }

        [Test]
        public void BuildEdgesIsDeterministic()
        {
            SelectionEdgeBar[] first = SelectionOutlineBuilder.BuildEdges(1.4f, 0.7f, 0.75f);
            SelectionEdgeBar[] second = SelectionOutlineBuilder.BuildEdges(1.4f, 0.7f, 0.75f);

            for (int i = 0; i < first.Length; i++)
            {
                Assert.AreEqual(first[i].LocalCenter, second[i].LocalCenter);
                Assert.AreEqual(first[i].LocalSize, second[i].LocalSize);
            }
        }
    }
}
