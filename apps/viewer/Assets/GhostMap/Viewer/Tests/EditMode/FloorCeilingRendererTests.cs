using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V2's <see cref="FloorCeilingRenderer"/>: fan triangulation of the
    /// room footprint into a floor mesh at y = 0 and a ceiling mesh at
    /// y = <see cref="RoomModel.heightM"/>, for both the axis-aligned fixture
    /// rooms and an arbitrarily rotated/skewed footprint.
    /// </summary>
    public sealed class FloorCeilingRendererTests
    {
        private static RoomModel BuildRectangularRoom(float heightM = 2.5f)
        {
            return new RoomModel
            {
                id = "room-1",
                name = "Bedroom",
                heightM = heightM,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
                },
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };
        }

        /// <summary>
        /// The same 4m x 3m rectangle, rotated 37 degrees about the floor
        /// origin, so the footprint is neither axis-aligned nor rectilinear
        /// in world X/Z.
        /// </summary>
        private static RoomModel BuildRotatedRoom(float heightM = 2.5f)
        {
            var localCorners = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };

            Quaternion rotation = Quaternion.Euler(0f, 37f, 0f);
            var corners = new CornerModel[localCorners.Length];

            for (int i = 0; i < localCorners.Length; i++)
            {
                Vector3 rotated = rotation * localCorners[i];
                corners[i] = new CornerModel { id = $"c{i}", position = Vec3Dto.FromVector3(rotated) };
            }

            return new RoomModel
            {
                id = "room-rotated",
                name = "Rotated Bedroom",
                heightM = heightM,
                corners = corners,
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };
        }

        [Test]
        public void RectangularRoom_FloorHasFourVerticesAtY0()
        {
            RoomModel room = BuildRectangularRoom();

            bool built = FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            Assert.IsTrue(built);
            Assert.AreEqual(4, mesh.vertexCount);
            foreach (Vector3 vertex in mesh.vertices)
            {
                Assert.AreEqual(0f, vertex.y, 1e-5f);
            }
        }

        [Test]
        public void RectangularRoom_CeilingIsAtRoomHeight()
        {
            RoomModel room = BuildRectangularRoom(heightM: 2.5f);

            bool built = FloorCeilingRenderer.TryBuildCeilingMesh(room, out Mesh mesh);

            Assert.IsTrue(built);
            foreach (Vector3 vertex in mesh.vertices)
            {
                Assert.AreEqual(2.5f, vertex.y, 1e-5f);
            }
        }

        [Test]
        public void FloorAndCeilingFootprintsMatchInXZ()
        {
            RoomModel room = BuildRectangularRoom();

            FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh floor);
            FloorCeilingRenderer.TryBuildCeilingMesh(room, out Mesh ceiling);

            Assert.AreEqual(floor.vertexCount, ceiling.vertexCount);
            for (int i = 0; i < floor.vertexCount; i++)
            {
                Assert.AreEqual(floor.vertices[i].x, ceiling.vertices[i].x, 1e-5f);
                Assert.AreEqual(floor.vertices[i].z, ceiling.vertices[i].z, 1e-5f);
            }
        }

        [Test]
        public void FloorVerticesMatchCornersExactlyAndAreNotMirrored()
        {
            RoomModel room = BuildRectangularRoom();

            FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            for (int i = 0; i < room.corners.Length; i++)
            {
                Vector3 expected = room.corners[i].position.ToVector3();
                Vector3 actual = mesh.vertices[i];
                Assert.AreEqual(expected.x, actual.x, 1e-5f, "Vertex X must match the corner exactly, never negated.");
                Assert.AreEqual(expected.z, actual.z, 1e-5f, "Vertex Z must match the corner exactly, never negated.");
            }
        }

        [Test]
        public void FloorTrianglesFaceUpward()
        {
            RoomModel room = BuildRectangularRoom();
            FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            AssertEveryTriangleFaces(mesh, Vector3.up);
        }

        [Test]
        public void CeilingTrianglesFaceDownward()
        {
            RoomModel room = BuildRectangularRoom();
            FloorCeilingRenderer.TryBuildCeilingMesh(room, out Mesh mesh);

            AssertEveryTriangleFaces(mesh, Vector3.down);
        }

        [Test]
        public void RotatedRoom_FloorAreaMatchesTheUnrotatedFootprint()
        {
            RoomModel room = BuildRotatedRoom();

            bool built = FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            Assert.IsTrue(built);
            Assert.AreEqual(4, mesh.vertexCount);
            AssertEveryTriangleFaces(mesh, Vector3.up);
            Assert.AreEqual(12f, ComputeMeshAreaXZ(mesh), 1e-3f, "A rotation must not change footprint area.");
        }

        [Test]
        public void RotatedRoom_CeilingFacesDownwardAtHeight()
        {
            RoomModel room = BuildRotatedRoom(heightM: 3.0f);

            bool built = FloorCeilingRenderer.TryBuildCeilingMesh(room, out Mesh mesh);

            Assert.IsTrue(built);
            AssertEveryTriangleFaces(mesh, Vector3.down);
            foreach (Vector3 vertex in mesh.vertices)
            {
                Assert.AreEqual(3.0f, vertex.y, 1e-5f);
            }
        }

        [Test]
        public void ZeroCornersProducesNoFloorMesh()
        {
            RoomModel room = BuildRectangularRoom();
            room.corners = new CornerModel[0];

            bool built = FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            Assert.IsFalse(built);
            Assert.IsNull(mesh);
        }

        [Test]
        public void OneCornerProducesNoFloorMesh()
        {
            RoomModel room = BuildRectangularRoom();
            room.corners = new[] { room.corners[0] };

            bool built = FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            Assert.IsFalse(built);
        }

        [Test]
        public void TwoCornersProducesNoFloorMesh()
        {
            RoomModel room = BuildRectangularRoom();
            room.corners = new[] { room.corners[0], room.corners[1] };

            bool built = FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            Assert.IsFalse(built);
        }

        [Test]
        public void ThreeCornersProducesAPreviewFloorTriangle()
        {
            RoomModel room = BuildRectangularRoom();
            room.corners = new[] { room.corners[0], room.corners[1], room.corners[2] };

            bool built = FloorCeilingRenderer.TryBuildFloorMesh(room, out Mesh mesh);

            Assert.IsTrue(built);
            Assert.AreEqual(3, mesh.vertexCount);
            AssertEveryTriangleFaces(mesh, Vector3.up);
        }

        [Test]
        public void HeightZeroProducesNoCeilingMesh()
        {
            RoomModel room = BuildRectangularRoom(heightM: 0f);

            bool built = FloorCeilingRenderer.TryBuildCeilingMesh(room, out Mesh mesh);

            Assert.IsFalse(built);
            Assert.IsNull(mesh);
        }

        [Test]
        public void NullRoomProducesNoFloorOrCeilingMesh()
        {
            Assert.IsFalse(FloorCeilingRenderer.TryBuildFloorMesh(null, out Mesh floor));
            Assert.IsFalse(FloorCeilingRenderer.TryBuildCeilingMesh(null, out Mesh ceiling));
            Assert.IsNull(floor);
            Assert.IsNull(ceiling);
        }

        private static void AssertEveryTriangleFaces(Mesh mesh, Vector3 expectedDirection)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 v0 = vertices[triangles[t]];
                Vector3 v1 = vertices[triangles[t + 1]];
                Vector3 v2 = vertices[triangles[t + 2]];

                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);

                Assert.Greater(
                    Vector3.Dot(normal, expectedDirection),
                    0f,
                    $"Triangle at index {t} does not face {expectedDirection}.");
            }
        }

        private static float ComputeMeshAreaXZ(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float area = 0f;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 v0 = vertices[triangles[t]];
                Vector3 v1 = vertices[triangles[t + 1]];
                Vector3 v2 = vertices[triangles[t + 2]];

                float cross = (v1.x - v0.x) * (v2.z - v0.z) - (v2.x - v0.x) * (v1.z - v0.z);
                area += Mathf.Abs(cross) * 0.5f;
            }

            return area;
        }
    }
}
