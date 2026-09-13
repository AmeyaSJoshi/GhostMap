using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task F2 tests for <see cref="RoomGeometry"/> and <see cref="WallGeometry"/>.
    /// </summary>
    public sealed class RoomGeometryTests
    {
        private const float Tolerance = 1e-4f;

        internal static RoomModel BuildRectangularRoom(
            float width = 4f,
            float depth = 3f,
            float heightM = 2.5f)
        {
            return new RoomModel
            {
                id = "room-1",
                name = "Bedroom",
                heightM = heightM,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(width, 0f, 0f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(width, 0f, depth) },
                    new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, depth) }
                },
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };
        }

        // -------------------------------------------------------------------
        // Polygon area
        // -------------------------------------------------------------------

        [Test]
        public void PolygonArea_ComputesRectangleArea()
        {
            var corners = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };

            Assert.AreEqual(12f, RoomGeometry.PolygonAreaXZ(corners), Tolerance);
        }

        [Test]
        public void PolygonArea_IsOrientationIndependent()
        {
            var clockwise = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 3f),
                new Vector3(4f, 0f, 3f),
                new Vector3(4f, 0f, 0f)
            };

            Assert.AreEqual(12f, RoomGeometry.PolygonAreaXZ(clockwise), Tolerance,
                "Area must be absolute regardless of winding.");
        }

        [Test]
        public void SignedPolygonArea_RevealsWinding()
        {
            var ccw = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };
            var cw = new List<Vector3>(ccw);
            cw.Reverse();

            float a = RoomGeometry.SignedPolygonAreaXZ(ccw);
            float b = RoomGeometry.SignedPolygonAreaXZ(cw);

            Assert.AreEqual(12f, Mathf.Abs(a), Tolerance);
            Assert.AreEqual(12f, Mathf.Abs(b), Tolerance);
            Assert.AreNotEqual(Mathf.Sign(a), Mathf.Sign(b),
                "Reversing winding must flip the sign of the signed area.");
        }

        [Test]
        public void PolygonArea_IgnoresY()
        {
            var corners = new List<Vector3>
            {
                new Vector3(0f, 9f, 0f),
                new Vector3(4f, -3f, 0f),
                new Vector3(4f, 1f, 3f),
                new Vector3(0f, 0f, 3f)
            };

            Assert.AreEqual(12f, RoomGeometry.PolygonAreaXZ(corners), Tolerance,
                "Area is measured in XZ only.");
        }

        // -------------------------------------------------------------------
        // Self-intersection
        // -------------------------------------------------------------------

        [Test]
        public void SelfIntersection_AcceptsConvexRectangle()
        {
            var corners = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };

            Assert.IsFalse(RoomGeometry.HasSelfIntersectionXZ(corners));
        }

        [Test]
        public void SelfIntersection_RejectsBowTie()
        {
            // Swapping the last two corners crosses the polygon.
            var corners = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(0f, 0f, 3f),
                new Vector3(4f, 0f, 3f)
            };

            Assert.IsTrue(RoomGeometry.HasSelfIntersectionXZ(corners),
                "A bow-tie polygon must be detected as self-intersecting.");
        }

        [Test]
        public void SelfIntersection_AcceptsNonRectangularQuad()
        {
            var corners = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0.5f),
                new Vector3(3.6f, 0f, 3f),
                new Vector3(0.2f, 0f, 2.7f)
            };

            Assert.IsFalse(RoomGeometry.HasSelfIntersectionXZ(corners),
                "A non-rectangular but simple quad is valid.");
        }

        // -------------------------------------------------------------------
        // Wall derivation
        // -------------------------------------------------------------------

        [Test]
        public void BuildWalls_ProducesFourWallsForFourCorners()
        {
            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(BuildRectangularRoom());

            Assert.AreEqual(4, walls.Count);
        }

        [Test]
        public void BuildWalls_ClosesTheLoop()
        {
            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(BuildRectangularRoom());

            Assert.AreEqual("c0", walls[0].StartCornerId);
            Assert.AreEqual("c1", walls[0].EndCornerId);

            Assert.AreEqual("c3", walls[3].StartCornerId);
            Assert.AreEqual("c0", walls[3].EndCornerId,
                "The last wall must close back to the first corner.");
        }

        [Test]
        public void BuildWalls_ComputesLengths()
        {
            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(BuildRectangularRoom(4f, 3f));

            Assert.AreEqual(4f, walls[0].LengthM, Tolerance);
            Assert.AreEqual(3f, walls[1].LengthM, Tolerance);
            Assert.AreEqual(4f, walls[2].LengthM, Tolerance);
            Assert.AreEqual(3f, walls[3].LengthM, Tolerance);
        }

        [Test]
        public void BuildWalls_ComputesUnitTangent()
        {
            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(BuildRectangularRoom());

            Assert.AreEqual(1f, walls[0].Tangent.magnitude, Tolerance,
                "Tangent must be a unit vector.");
            Assert.AreEqual(1f, walls[0].Tangent.x, Tolerance);
            Assert.AreEqual(0f, walls[0].Tangent.z, Tolerance);
        }

        [Test]
        public void BuildWalls_ReturnsEmptyForTooFewCorners()
        {
            RoomModel room = BuildRectangularRoom();
            room.corners = new[] { room.corners[0] };

            Assert.AreEqual(0, RoomGeometry.BuildWalls(room).Count,
                "A single corner defines no wall.");
        }

        [Test]
        public void BuildWalls_ReturnsEmptyForNullCorners()
        {
            RoomModel room = BuildRectangularRoom();
            room.corners = null;

            Assert.AreEqual(0, RoomGeometry.BuildWalls(room).Count);
        }

        [Test]
        public void TryFindWall_LocatesWallByCornerIds()
        {
            RoomModel room = BuildRectangularRoom();

            bool found = RoomGeometry.TryFindWall(room, "c1", "c2", out WallDefinition wall);

            Assert.IsTrue(found);
            Assert.AreEqual(3f, wall.LengthM, Tolerance);
        }

        [Test]
        public void TryFindWall_FailsForNonAdjacentCorners()
        {
            RoomModel room = BuildRectangularRoom();

            bool found = RoomGeometry.TryFindWall(room, "c0", "c2", out _);

            Assert.IsFalse(found, "c0 and c2 are diagonal, not a wall.");
        }

        // -------------------------------------------------------------------
        // Wall-local coordinates - drives height, doors and windows.
        // -------------------------------------------------------------------

        [Test]
        public void WallGeometry_ComputesNormalPerpendicularToTangentAndUp()
        {
            var wall = new WallDefinition("a", "b", Vector3.zero, new Vector3(4f, 0f, 0f));

            Vector3 normal = WallGeometry.Normal(wall);

            Assert.AreEqual(1f, normal.magnitude, Tolerance);
            Assert.AreEqual(0f, Vector3.Dot(normal, wall.Tangent), Tolerance);
            Assert.AreEqual(0f, Vector3.Dot(normal, Vector3.up), Tolerance);
        }

        [Test]
        public void WallGeometry_PlaneContainsBothCorners()
        {
            var wall = new WallDefinition("a", "b", new Vector3(1f, 0f, 1f), new Vector3(4f, 0f, 2f));

            Plane plane = WallGeometry.PlaneFor(wall);

            Assert.AreEqual(0f, plane.GetDistanceToPoint(wall.Start), Tolerance);
            Assert.AreEqual(0f, plane.GetDistanceToPoint(wall.End), Tolerance);
        }

        [Test]
        public void WallGeometry_ToWallLocalComputesOffsetAndHeight()
        {
            var wall = new WallDefinition("a", "b", Vector3.zero, new Vector3(4f, 0f, 0f));

            WallGeometry.ToWallLocal(wall, new Vector3(1.2f, 2.05f, 0f), out float u, out float v);

            Assert.AreEqual(1.2f, u, Tolerance, "u is distance along the wall from the start.");
            Assert.AreEqual(2.05f, v, Tolerance, "v is height above the floor.");
        }

        [Test]
        public void WallGeometry_WallLocalRoundTrips()
        {
            var wall = new WallDefinition("a", "b", new Vector3(1f, 0f, 1f), new Vector3(4f, 0f, 3f));

            Vector3 original = WallGeometry.FromWallLocal(wall, 1.5f, 2.1f);
            WallGeometry.ToWallLocal(wall, original, out float u, out float v);

            Assert.AreEqual(1.5f, u, Tolerance);
            Assert.AreEqual(2.1f, v, Tolerance);
        }

        // -------------------------------------------------------------------
        // Interior angles. Regression coverage added during the F0-F3 review:
        // InteriorAngleDeg previously returned the unsigned corner angle, which
        // cannot exceed 180 degrees, so a reflex interior angle was silently
        // reported as its 360-degree complement.
        // -------------------------------------------------------------------

        [Test]
        public void InteriorAngle_IsNinetyDegreesAtEveryRectangleCorner()
        {
            List<Vector3> points = Points(
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f));

            for (int i = 0; i < points.Count; i++)
            {
                Assert.AreEqual(90f, RoomGeometry.InteriorAngleDeg(points, i), 1e-3f,
                    $"Corner {i} of a rectangle is a right angle.");
            }
        }

        [Test]
        public void InteriorAngle_IsIndependentOfWinding()
        {
            List<Vector3> ccw = Points(
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f));

            List<Vector3> cw = Points(
                new Vector3(0f, 0f, 3f),
                new Vector3(4f, 0f, 3f),
                new Vector3(4f, 0f, 0f),
                new Vector3(0f, 0f, 0f));

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(90f, RoomGeometry.InteriorAngleDeg(ccw, i), 1e-3f);
                Assert.AreEqual(90f, RoomGeometry.InteriorAngleDeg(cw, i), 1e-3f);
            }
        }

        [Test]
        public void InteriorAngle_ReportsReflexAngleBeyond180()
        {
            // A "dart": a simple, non-self-intersecting quad whose fourth corner
            // is pulled inside the triangle formed by the other three. The true
            // interior angle there is 216.1 degrees.
            List<Vector3> dart = DartFootprint();

            Assert.Greater(RoomGeometry.InteriorAngleDeg(dart, 3), 180f,
                "A reflex corner must report more than 180 degrees, not its complement.");
            Assert.AreEqual(216.1f, RoomGeometry.InteriorAngleDeg(dart, 3), 0.2f);
        }

        [Test]
        public void InteriorAngle_SumsTo360ForAQuad()
        {
            List<Vector3> dart = DartFootprint();

            float sum = 0f;
            for (int i = 0; i < dart.Count; i++)
            {
                sum += RoomGeometry.InteriorAngleDeg(dart, i);
            }

            Assert.AreEqual(360f, sum, 0.1f,
                "Interior angles of any simple quad sum to 360 degrees.");
        }

        [Test]
        public void TryFindWall_RejectsReversedCornerPair()
        {
            RoomModel room = BuildRectangularRoom();

            Assert.IsTrue(RoomGeometry.TryFindWall(room, "c0", "c1", out _));
            Assert.IsFalse(RoomGeometry.TryFindWall(room, "c1", "c0", out _),
                "An opening offset is measured from its declared start corner, so a "
                + "reversed pair must fail loudly rather than mirror the opening.");
        }

        /// <summary>
        /// A simple quad with one reflex (216.1 degree) interior corner. Its area
        /// is 10.0 m2 and every wall is between 1.73 m and 6.05 m, so it passes
        /// every room rule except the interior-angle rule.
        /// </summary>
        internal static List<Vector3> DartFootprint()
        {
            return Points(
                new Vector3(0f, 0f, 0f),
                new Vector3(4.98f, 0f, 0f),
                new Vector3(1.19f, 0f, 4.71f),
                new Vector3(1.07f, 0f, 1.36f));
        }

        private static List<Vector3> Points(params Vector3[] values)
            => new List<Vector3>(values);

    }
}
