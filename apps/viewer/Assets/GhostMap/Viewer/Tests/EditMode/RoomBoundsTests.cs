using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V4's <see cref="RoomBounds"/>: the camera frames whatever room it
    /// is actually given, never a hard-coded 4 x 3 m fixture footprint.
    /// </summary>
    public sealed class RoomBoundsTests
    {
        private const float Tolerance = 1e-4f;

        private static RoomModel Room(float heightM, params Vector3[] corners)
        {
            var models = new CornerModel[corners.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                models[i] = new CornerModel
                {
                    id = "c" + i,
                    position = new Vec3Dto(corners[i].x, corners[i].y, corners[i].z)
                };
            }

            return new RoomModel
            {
                id = "room-1",
                name = "Room",
                heightM = heightM,
                corners = models,
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };
        }

        [Test]
        public void RectangularRoomProducesItsFootprintAndHeight()
        {
            RoomModel room = Room(2.5f,
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f));

            Assert.IsTrue(RoomBounds.TryCompute(room, out Bounds bounds));

            Assert.AreEqual(new Vector3(2f, 1.25f, 1.5f), bounds.center);
            Assert.AreEqual(4f, bounds.size.x, Tolerance);
            Assert.AreEqual(2.5f, bounds.size.y, Tolerance);
            Assert.AreEqual(3f, bounds.size.z, Tolerance);
        }

        [Test]
        public void ADifferentlySizedRoomProducesDifferentBounds()
        {
            RoomModel small = Room(2.2f,
                new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f),
                new Vector3(2f, 0f, 2f), new Vector3(0f, 0f, 2f));
            RoomModel large = Room(3.6f,
                new Vector3(0f, 0f, 0f), new Vector3(9f, 0f, 0f),
                new Vector3(9f, 0f, 7f), new Vector3(0f, 0f, 7f));

            Assert.IsTrue(RoomBounds.TryCompute(small, out Bounds smallBounds));
            Assert.IsTrue(RoomBounds.TryCompute(large, out Bounds largeBounds));

            Assert.AreEqual(2f, smallBounds.size.x, Tolerance);
            Assert.AreEqual(9f, largeBounds.size.x, Tolerance);
            Assert.AreEqual(7f, largeBounds.size.z, Tolerance);
            Assert.Less(smallBounds.extents.magnitude, largeBounds.extents.magnitude);
        }

        [Test]
        public void RoomNotAtTheOriginIsCentredOnItsOwnFootprint()
        {
            RoomModel room = Room(2.5f,
                new Vector3(10f, 0f, -6f), new Vector3(14f, 0f, -6f),
                new Vector3(14f, 0f, -3f), new Vector3(10f, 0f, -3f));

            Assert.IsTrue(RoomBounds.TryCompute(room, out Bounds bounds));

            Assert.AreEqual(12f, bounds.center.x, Tolerance);
            Assert.AreEqual(-4.5f, bounds.center.z, Tolerance);
        }

        [Test]
        public void RotatedRoomIsFullyEnclosed()
        {
            // A 4 x 3 room rotated 37 degrees about the origin: the axis-aligned
            // bounds must still contain every corner.
            const float deg = 37f;
            float c = Mathf.Cos(deg * Mathf.Deg2Rad);
            float s = Mathf.Sin(deg * Mathf.Deg2Rad);

            Vector3 Rotate(float x, float z) => new Vector3(x * c - z * s, 0f, x * s + z * c);

            RoomModel room = Room(2.5f,
                Rotate(0f, 0f), Rotate(4f, 0f), Rotate(4f, 3f), Rotate(0f, 3f));

            Assert.IsTrue(RoomBounds.TryCompute(room, out Bounds bounds));

            foreach (CornerModel corner in room.corners)
            {
                Vector3 p = corner.position.ToVector3();
                Assert.IsTrue(bounds.Contains(new Vector3(p.x, bounds.center.y, p.z)),
                    $"Corner {corner.id} at {p} is outside the computed bounds {bounds}.");
            }

            Assert.Greater(bounds.size.x, 4f, "A rotated room has a wider axis-aligned footprint.");
            Assert.Greater(bounds.size.z, 3f);
        }

        [Test]
        public void RoomWithoutHeightStillProducesAFootprint()
        {
            // Before S4's height capture, heightM is 0. The camera must still be
            // able to frame the partial scan.
            RoomModel room = Room(0f,
                new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(4f, 0f, 3f));

            Assert.IsTrue(RoomBounds.TryCompute(room, out Bounds bounds));

            Assert.AreEqual(4f, bounds.size.x, Tolerance);
            Assert.AreEqual(0f, bounds.size.y, Tolerance);
        }

        [Test]
        public void SingleCornerProducesADegenerateButValidPoint()
        {
            RoomModel room = Room(0f, new Vector3(1f, 0f, 2f));

            Assert.IsTrue(RoomBounds.TryCompute(room, out Bounds bounds));

            Assert.AreEqual(new Vector3(1f, 0f, 2f), bounds.center);
        }

        [Test]
        public void NoCornersProducesNoBounds()
        {
            Assert.IsFalse(RoomBounds.TryCompute(Room(2.5f), out _));
            Assert.IsFalse(RoomBounds.TryCompute(null, out _));
        }

        [Test]
        public void NonFiniteCornerIsRejectedRatherThanPoisoningTheBounds()
        {
            RoomModel room = Room(2.5f,
                new Vector3(0f, 0f, 0f), new Vector3(float.NaN, 0f, 0f), new Vector3(4f, 0f, 3f));

            Assert.IsTrue(RoomBounds.TryCompute(room, out Bounds bounds));

            Assert.IsFalse(float.IsNaN(bounds.center.x));
            Assert.IsFalse(float.IsNaN(bounds.size.x));
            Assert.AreEqual(4f, bounds.size.x, Tolerance, "Only the finite corners may contribute.");
        }
    }
}
