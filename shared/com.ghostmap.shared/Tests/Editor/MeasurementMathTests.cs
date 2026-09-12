using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>Task F2 tests for <see cref="MeasurementMath"/>.</summary>
    public sealed class MeasurementMathTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void Distance3D_IncludesVerticalComponent()
        {
            float d = MeasurementMath.Distance(new Vector3(0f, 0f, 0f), new Vector3(3f, 4f, 0f));

            Assert.AreEqual(5f, d, Tolerance);
        }

        [Test]
        public void DistanceXZ_IgnoresVerticalComponent()
        {
            float d = MeasurementMath.DistanceXZ(new Vector3(0f, 10f, 0f), new Vector3(3f, -7f, 4f));

            Assert.AreEqual(5f, d, Tolerance);
        }

        [Test]
        public void RoomArea_MatchesFootprint()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f);

            Assert.AreEqual(12f, MeasurementMath.RoomAreaM2(room), Tolerance);
        }

        [Test]
        public void RoomVolume_IsAreaTimesHeight()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f, 2.5f);

            Assert.AreEqual(30f, MeasurementMath.RoomVolumeM3(room), Tolerance);
        }

        [Test]
        public void RoomPerimeter_SumsWallLengths()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f);

            Assert.AreEqual(14f, MeasurementMath.RoomPerimeterM(room), Tolerance);
        }
    }
}
