using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V2's <see cref="WallRenderer"/>: one solid cuboid per consecutive
    /// corner pair, derived from the shared package's own
    /// <c>RoomGeometry.BuildWalls</c> and never a reimplementation of wall
    /// derivation.
    /// </summary>
    public sealed class WallRendererTests
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

        [Test]
        public void FourCornerRoomYieldsFourWalls()
        {
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            Assert.AreEqual(4, walls.Count);
        }

        [Test]
        public void EachWallJoinsConsecutiveCornersInOrderWithWraparound()
        {
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            Assert.AreEqual("c0", walls[0].StartCornerId);
            Assert.AreEqual("c1", walls[0].EndCornerId);
            Assert.AreEqual("c1", walls[1].StartCornerId);
            Assert.AreEqual("c2", walls[1].EndCornerId);
            Assert.AreEqual("c2", walls[2].StartCornerId);
            Assert.AreEqual("c3", walls[2].EndCornerId);
            Assert.AreEqual("c3", walls[3].StartCornerId);
            Assert.AreEqual("c0", walls[3].EndCornerId, "The closing wall must return from the last corner to the first.");
        }

        [Test]
        public void NoWallIsGeneratedFromNonConsecutiveCorners()
        {
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            var pairs = new HashSet<(string, string)>();
            foreach (WallRenderSpec wall in walls)
            {
                pairs.Add((wall.StartCornerId, wall.EndCornerId));
            }

            Assert.IsFalse(pairs.Contains(("c0", "c2")));
            Assert.IsFalse(pairs.Contains(("c1", "c3")));
        }

        [Test]
        public void NoDuplicateWalls()
        {
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            var seen = new HashSet<(string, string)>();
            foreach (WallRenderSpec wall in walls)
            {
                Assert.IsTrue(seen.Add((wall.StartCornerId, wall.EndCornerId)), "Duplicate wall detected.");
            }
        }

        [Test]
        public void WallLengthMatchesTheModel()
        {
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            Assert.AreEqual(4f, walls[0].LengthM, 1e-5f);
            Assert.AreEqual(3f, walls[1].LengthM, 1e-5f);
            Assert.AreEqual(4f, walls[2].LengthM, 1e-5f);
            Assert.AreEqual(3f, walls[3].LengthM, 1e-5f);
            Assert.AreEqual(4f, walls[0].Scale.x, 1e-5f, "Scale.x carries the wall's rendered length.");
        }

        [Test]
        public void WallHeightMatchesRoomHeight()
        {
            RoomModel room = BuildRectangularRoom(heightM: 2.5f);

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            foreach (WallRenderSpec wall in walls)
            {
                Assert.AreEqual(2.5f, wall.Scale.y, 1e-5f);
            }
        }

        [Test]
        public void WallMidpointIsCorrectAndCenteredBetweenFloorAndCeiling()
        {
            RoomModel room = BuildRectangularRoom(heightM: 2.5f);

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            // Wall 0: c0 (0,0,0) -> c1 (4,0,0). Midpoint (2, 1.25, 0).
            Assert.AreEqual(2f, walls[0].Position.x, 1e-5f);
            Assert.AreEqual(1.25f, walls[0].Position.y, 1e-5f);
            Assert.AreEqual(0f, walls[0].Position.z, 1e-5f);
        }

        [Test]
        public void WallYawOrientationPointsAlongTheWallTangent()
        {
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            // Wall 0 runs from c0 to c1 along +X.
            Vector3 rotatedRight0 = walls[0].Rotation * Vector3.right;
            Assert.AreEqual(1f, rotatedRight0.x, 1e-4f);
            Assert.AreEqual(0f, rotatedRight0.z, 1e-4f);

            // Wall 1 runs from c1 to c2 along +Z.
            Vector3 rotatedRight1 = walls[1].Rotation * Vector3.right;
            Assert.AreEqual(0f, rotatedRight1.x, 1e-4f);
            Assert.AreEqual(1f, rotatedRight1.z, 1e-4f);
        }

        [Test]
        public void RotatedRoomWallsHaveCorrectLengthAndYaw()
        {
            Quaternion rotation = Quaternion.Euler(0f, 37f, 0f);
            var localCorners = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f)
            };

            var corners = new CornerModel[localCorners.Length];
            for (int i = 0; i < localCorners.Length; i++)
            {
                corners[i] = new CornerModel
                {
                    id = $"c{i}",
                    position = Vec3Dto.FromVector3(rotation * localCorners[i])
                };
            }

            var room = new RoomModel
            {
                id = "room-rotated",
                heightM = 2.5f,
                corners = corners,
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            Assert.AreEqual(4, walls.Count);
            Assert.AreEqual(4f, walls[0].LengthM, 1e-4f);
            Assert.AreEqual(3f, walls[1].LengthM, 1e-4f);

            Vector3 expectedTangent0 = (rotation * new Vector3(1f, 0f, 0f)).normalized;
            Vector3 actualTangent0 = (walls[0].Rotation * Vector3.right).normalized;
            Assert.AreEqual(expectedTangent0.x, actualTangent0.x, 1e-4f);
            Assert.AreEqual(expectedTangent0.z, actualTangent0.z, 1e-4f);
        }

        [Test]
        public void HeightZeroYieldsNoWalls()
        {
            RoomModel room = BuildRectangularRoom(heightM: 0f);

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            Assert.AreEqual(0, walls.Count);
        }

        [Test]
        public void FewerThanTwoCornersYieldsNoWalls()
        {
            RoomModel room = BuildRectangularRoom();
            room.corners = new[] { room.corners[0] };

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            Assert.AreEqual(0, walls.Count);
        }

        [Test]
        public void NullRoomYieldsNoWalls()
        {
            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(null);

            Assert.AreEqual(0, walls.Count);
        }
    }
}
