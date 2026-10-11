using System.Collections.Generic;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// ADR-0006 tests for <see cref="RoomGeometry.ContainsPointXZ"/>, the
    /// "is this detected surface actually in this room" gate.
    /// </summary>
    public sealed class RoomContainmentTests
    {
        /// <summary>A 4.0 m x 3.0 m room with corners at the origin.</summary>
        private static List<Vector3> Rectangle(float width = 4f, float depth = 3f)
        {
            return new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(width, 0f, 0f),
                new Vector3(width, 0f, depth),
                new Vector3(0f, 0f, depth)
            };
        }

        [Test]
        public void ContainsPoint_TrueForTheCentre()
        {
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(
                Rectangle(), new Vector3(2f, 0f, 1.5f)));
        }

        [Test]
        public void ContainsPoint_TrueNearEachWallButInside()
        {
            List<Vector3> room = Rectangle();

            Assert.IsTrue(RoomGeometry.ContainsPointXZ(room, new Vector3(0.05f, 0f, 1.5f)));
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(room, new Vector3(3.95f, 0f, 1.5f)));
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(room, new Vector3(2f, 0f, 0.05f)));
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(room, new Vector3(2f, 0f, 2.95f)));
        }

        [Test]
        public void ContainsPoint_FalseOutsideEachWall()
        {
            List<Vector3> room = Rectangle();

            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(-0.5f, 0f, 1.5f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(4.5f, 0f, 1.5f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(2f, 0f, -0.5f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(2f, 0f, 3.5f)));
        }

        [Test]
        public void ContainsPoint_FalseDiagonallyOutside()
        {
            List<Vector3> room = Rectangle();

            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(-1f, 0f, -1f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(5f, 0f, 4f)));
        }

        /// <summary>
        /// A point level with the Z of a shared vertex is where a naive crossing
        /// test double-counts and reports an interior point as outside.
        /// </summary>
        [Test]
        public void ContainsPoint_HandlesPointsLevelWithAVertex()
        {
            List<Vector3> room = Rectangle();

            // z = 0 is the Z of corners 0 and 1; z = 3 is the Z of corners 2 and 3.
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(-1f, 0f, 0f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(5f, 0f, 0f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(-1f, 0f, 3f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(5f, 0f, 3f)));
        }

        [Test]
        public void ContainsPoint_IsIndependentOfWinding()
        {
            List<Vector3> clockwise = Rectangle();
            var counterClockwise = new List<Vector3>(clockwise);
            counterClockwise.Reverse();

            var inside = new Vector3(2f, 0f, 1.5f);
            var outside = new Vector3(9f, 0f, 1.5f);

            Assert.IsTrue(RoomGeometry.ContainsPointXZ(clockwise, inside));
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(counterClockwise, inside));

            Assert.IsFalse(RoomGeometry.ContainsPointXZ(clockwise, outside));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(counterClockwise, outside));
        }

        [Test]
        public void ContainsPoint_IgnoresY()
        {
            List<Vector3> room = Rectangle();

            // A detected plane sits well above the floor; only XZ may matter.
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(room, new Vector3(2f, 0.75f, 1.5f)));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(room, new Vector3(-2f, 0.75f, 1.5f)));
        }

        /// <summary>
        /// An L-shaped footprint. Outside the MVP's four-corner rule, but the
        /// test must be correct for concave polygons so Stretch 3 does not
        /// inherit a subtly wrong containment check.
        /// </summary>
        [Test]
        public void ContainsPoint_IsCorrectForAConcaveFootprint()
        {
            var lShape = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 1f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };

            // Inside the long arm, and inside the short arm.
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(lShape, new Vector3(3f, 0f, 0.5f)));
            Assert.IsTrue(RoomGeometry.ContainsPointXZ(lShape, new Vector3(0.5f, 0f, 2.5f)));

            // In the notch the L does not cover.
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(lShape, new Vector3(3f, 0f, 2.5f)));
        }

        [Test]
        public void ContainsPoint_FalseForDegenerateInput()
        {
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(null, Vector3.zero));
            Assert.IsFalse(RoomGeometry.ContainsPointXZ(new List<Vector3>(), Vector3.zero));

            Assert.IsFalse(RoomGeometry.ContainsPointXZ(
                new List<Vector3> { Vector3.zero, Vector3.right },
                new Vector3(0.5f, 0f, 0f)));
        }
    }
}
