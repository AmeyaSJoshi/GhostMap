using UnityEngine;

namespace GhostMap.Viewer.Interaction
{
    /// <summary>
    /// One thin bar of the selection wireframe, in the selected object's own
    /// local frame — origin at its centre on the floor, +x width, +y up,
    /// +z depth, exactly <c>FurnitureFactory</c>'s convention, so an edge bar
    /// parented under the object root needs no extra transform.
    /// </summary>
    public readonly struct SelectionEdgeBar
    {
        public Vector3 LocalCenter { get; }
        public Vector3 LocalSize { get; }

        public SelectionEdgeBar(Vector3 localCenter, Vector3 localSize)
        {
            LocalCenter = localCenter;
            LocalSize = localSize;
        }
    }

    /// <summary>
    /// Task V5's selection highlight geometry: a simple wireframe bounding
    /// box built from 12 thin cuboids, one per edge — the same
    /// "procedural primitive cubes" technique <c>FurnitureFactory</c> and
    /// <c>WallRenderer</c> already use, rather than a <c>LineRenderer</c> path
    /// or a third-party outline package. Pure and fully testable: given a
    /// declared width/depth/height it always returns exactly 12 bars whose
    /// union traces the box's edges, slightly inflated by <paramref
    /// name="marginM"/> so the highlight does not z-fight the object's own
    /// surfaces.
    /// </summary>
    public static class SelectionOutlineBuilder
    {
        public const float DefaultThicknessM = 0.02f;
        public const float DefaultMarginM = 0.02f;

        public static SelectionEdgeBar[] BuildEdges(
            float widthM,
            float depthM,
            float heightM,
            float thicknessM = DefaultThicknessM,
            float marginM = DefaultMarginM)
        {
            float hw = (widthM * 0.5f) + marginM;
            float hd = (depthM * 0.5f) + marginM;
            float bottomY = -marginM;
            float topY = heightM + marginM;

            var b0 = new Vector3(-hw, bottomY, -hd);
            var b1 = new Vector3(hw, bottomY, -hd);
            var b2 = new Vector3(hw, bottomY, hd);
            var b3 = new Vector3(-hw, bottomY, hd);
            var t0 = new Vector3(-hw, topY, -hd);
            var t1 = new Vector3(hw, topY, -hd);
            var t2 = new Vector3(hw, topY, hd);
            var t3 = new Vector3(-hw, topY, hd);

            float widthWithMargin = 2f * hw;
            float depthWithMargin = 2f * hd;
            float heightWithMargin = topY - bottomY;

            return new[]
            {
                Bar(b0, b1, widthWithMargin, thicknessM, thicknessM),
                Bar(b1, b2, thicknessM, thicknessM, depthWithMargin),
                Bar(b2, b3, widthWithMargin, thicknessM, thicknessM),
                Bar(b3, b0, thicknessM, thicknessM, depthWithMargin),

                Bar(t0, t1, widthWithMargin, thicknessM, thicknessM),
                Bar(t1, t2, thicknessM, thicknessM, depthWithMargin),
                Bar(t2, t3, widthWithMargin, thicknessM, thicknessM),
                Bar(t3, t0, thicknessM, thicknessM, depthWithMargin),

                Bar(b0, t0, thicknessM, heightWithMargin, thicknessM),
                Bar(b1, t1, thicknessM, heightWithMargin, thicknessM),
                Bar(b2, t2, thicknessM, heightWithMargin, thicknessM),
                Bar(b3, t3, thicknessM, heightWithMargin, thicknessM)
            };
        }

        private static SelectionEdgeBar Bar(Vector3 a, Vector3 b, float sx, float sy, float sz)
            => new SelectionEdgeBar((a + b) * 0.5f, new Vector3(sx, sy, sz));
    }
}
