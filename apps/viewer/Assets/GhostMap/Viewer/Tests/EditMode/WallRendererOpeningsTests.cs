using System.Collections.Generic;
using System.IO;
using System.Linq;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V3's addition to <see cref="WallRenderer"/>: matching each
    /// <see cref="OpeningModel"/> to its wall by ordered corner pair, and
    /// turning the resulting wall-local <see cref="WallSlice"/> grid into
    /// world-space cuboid segments.
    ///
    /// The segmentation maths itself lives in
    /// <see cref="WallSliceGeneratorTests"/>; this file is about which wall an
    /// opening lands on and where its segments end up in world space.
    /// </summary>
    public sealed class WallRendererOpeningsTests
    {
        private const float Tolerance = 1e-4f;

        private static RoomModel BuildRectangularRoom(
            float heightM = 2.5f,
            params OpeningModel[] openings)
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
                openings = openings ?? new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };
        }

        private static OpeningModel Door(
            string startCornerId = "c0",
            string endCornerId = "c1",
            float offsetM = 1.2f,
            float widthM = 0.9f,
            float heightM = 2.05f,
            string id = "door-1")
        {
            return new OpeningModel
            {
                id = id,
                type = "door",
                wallStartCornerId = startCornerId,
                wallEndCornerId = endCornerId,
                offsetM = offsetM,
                widthM = widthM,
                sillHeightM = 0f,
                heightM = heightM
            };
        }

        private static OpeningModel Window(
            string startCornerId = "c1",
            string endCornerId = "c2",
            float offsetM = 0.9f,
            float widthM = 1.2f,
            float sillHeightM = 0.9f,
            float heightM = 1.1f,
            string id = "window-1")
        {
            return new OpeningModel
            {
                id = id,
                type = "window",
                wallStartCornerId = startCornerId,
                wallEndCornerId = endCornerId,
                offsetM = offsetM,
                widthM = widthM,
                sillHeightM = sillHeightM,
                heightM = heightM
            };
        }

        private static WallRenderSpec WallFor(
            IReadOnlyList<WallRenderSpec> walls,
            string startCornerId,
            string endCornerId)
        {
            return walls.First(w => w.StartCornerId == startCornerId && w.EndCornerId == endCornerId);
        }

        private static float SolidArea(WallRenderSpec wall)
            => wall.Segments.Sum(s => s.Slice.WidthM * s.Slice.HeightM);

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        [Test]
        public void WallWithoutOpeningsHasExactlyOneFullHeightSegment()
        {
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            foreach (WallRenderSpec wall in walls)
            {
                Assert.AreEqual(1, wall.Segments.Count, $"Wall {wall.StartCornerId}->{wall.EndCornerId}.");
                Assert.AreEqual(wall.LengthM, wall.Segments[0].Slice.WidthM, Tolerance);
                Assert.AreEqual(room.heightM, wall.Segments[0].Slice.HeightM, Tolerance);
            }
        }

        [Test]
        public void WallWithoutOpeningsRendersExactlyWhereV2RenderedIt()
        {
            // V2's contract: one cuboid centered on the wall's floor-to-ceiling
            // midpoint. A wall with no openings must be pixel-identical under V3.
            RoomModel room = BuildRectangularRoom();

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            foreach (WallRenderSpec wall in walls)
            {
                WallSegmentSpec segment = wall.Segments[0];

                Assert.AreEqual(wall.Position.x, segment.Position.x, Tolerance);
                Assert.AreEqual(wall.Position.y, segment.Position.y, Tolerance);
                Assert.AreEqual(wall.Position.z, segment.Position.z, Tolerance);
                Assert.AreEqual(0f, Quaternion.Angle(wall.Rotation, segment.Rotation), 1e-3f);
                Assert.AreEqual(wall.Scale.x, segment.Scale.x, Tolerance);
                Assert.AreEqual(wall.Scale.y, segment.Scale.y, Tolerance);
                Assert.AreEqual(wall.Scale.z, segment.Scale.z, Tolerance);
            }
        }

        [Test]
        public void DoorCutsOnlyTheWallItNames()
        {
            RoomModel room = BuildRectangularRoom(2.5f, Door());

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            Assert.Greater(WallFor(walls, "c0", "c1").Segments.Count, 1,
                "The named wall must be segmented around the door.");
            Assert.AreEqual(1, WallFor(walls, "c1", "c2").Segments.Count);
            Assert.AreEqual(1, WallFor(walls, "c2", "c3").Segments.Count);
            Assert.AreEqual(1, WallFor(walls, "c3", "c0").Segments.Count);
        }

        [Test]
        public void DoorRemovesItsAreaFromTheNamedWall()
        {
            OpeningModel door = Door();
            RoomModel room = BuildRectangularRoom(2.5f, door);

            WallRenderSpec wall = WallFor(WallRenderer.BuildWalls(room), "c0", "c1");

            float expected = wall.LengthM * room.heightM - door.widthM * door.heightM;

            Assert.AreEqual(expected, SolidArea(wall), Tolerance);
        }

        [Test]
        public void WindowRemovesItsAreaFromTheNamedWall()
        {
            OpeningModel window = Window();
            RoomModel room = BuildRectangularRoom(2.5f, window);

            WallRenderSpec wall = WallFor(WallRenderer.BuildWalls(room), "c1", "c2");

            float expected = wall.LengthM * room.heightM - window.widthM * window.heightM;

            Assert.AreEqual(expected, SolidArea(wall), Tolerance);
        }

        [Test]
        public void ReversedCornerPairDoesNotCutTheWall()
        {
            // An opening declares its wall as an *ordered* pair, because its
            // offset is measured from the start corner. Accepting the reverse
            // pair would silently mirror the opening to the other end of the
            // wall, which is worse than not rendering it at all.
            var diagnostics = new List<string>();
            RoomModel room = BuildRectangularRoom(2.5f, Door(startCornerId: "c1", endCornerId: "c0"));

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room, diagnostics);

            foreach (WallRenderSpec wall in walls)
            {
                Assert.AreEqual(1, wall.Segments.Count,
                    $"Wall {wall.StartCornerId}->{wall.EndCornerId} must stay solid.");
            }

            Assert.AreEqual(1, diagnostics.Count, "The unmatched opening must be diagnosed, not swallowed.");
            StringAssert.Contains("door-1", diagnostics[0]);
        }

        [Test]
        public void OpeningNamingUnknownCornersIsDiagnosedAndIgnored()
        {
            var diagnostics = new List<string>();
            RoomModel room = BuildRectangularRoom(2.5f, Door(startCornerId: "nope", endCornerId: "also-nope"));

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room, diagnostics);

            Assert.IsTrue(walls.All(w => w.Segments.Count == 1));
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void NonConsecutiveCornerPairIsDiagnosedAndIgnored()
        {
            var diagnostics = new List<string>();
            RoomModel room = BuildRectangularRoom(2.5f, Door(startCornerId: "c0", endCornerId: "c2"));

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room, diagnostics);

            Assert.IsTrue(walls.All(w => w.Segments.Count == 1));
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void OpeningsOnSeparateWallsRemainIndependent()
        {
            OpeningModel door = Door();
            OpeningModel window = Window();
            RoomModel room = BuildRectangularRoom(2.5f, door, window);

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(room);

            WallRenderSpec doorWall = WallFor(walls, "c0", "c1");
            WallRenderSpec windowWall = WallFor(walls, "c1", "c2");

            Assert.AreEqual(
                doorWall.LengthM * room.heightM - door.widthM * door.heightM,
                SolidArea(doorWall), Tolerance);
            Assert.AreEqual(
                windowWall.LengthM * room.heightM - window.widthM * window.heightM,
                SolidArea(windowWall), Tolerance);
            Assert.AreEqual(1, WallFor(walls, "c2", "c3").Segments.Count);
            Assert.AreEqual(1, WallFor(walls, "c3", "c0").Segments.Count);
        }

        [Test]
        public void TwoOpeningsOnOneWallBothCutIt()
        {
            OpeningModel door = Door(offsetM: 0.4f, id: "door-1");
            OpeningModel window = Window(
                startCornerId: "c0", endCornerId: "c1", offsetM: 2.2f, id: "window-1");
            RoomModel room = BuildRectangularRoom(2.5f, door, window);

            WallRenderSpec wall = WallFor(WallRenderer.BuildWalls(room), "c0", "c1");

            float expected = wall.LengthM * room.heightM
                           - door.widthM * door.heightM
                           - window.widthM * window.heightM;

            Assert.AreEqual(expected, SolidArea(wall), Tolerance);
        }

        [Test]
        public void SegmentsStayOnTheWallCenterlineAndKeepWallOrientation()
        {
            // Wall c1->c2 runs from (4,0,0) to (4,0,3): tangent +Z, so every
            // segment centre must sit on x = 4 with the wall's own rotation.
            RoomModel room = BuildRectangularRoom(2.5f, Window());

            WallRenderSpec wall = WallFor(WallRenderer.BuildWalls(room), "c1", "c2");

            Assert.Greater(wall.Segments.Count, 1);

            foreach (WallSegmentSpec segment in wall.Segments)
            {
                Assert.AreEqual(4f, segment.Position.x, Tolerance, "Segment left the wall plane.");
                Assert.AreEqual(0f, Quaternion.Angle(wall.Rotation, segment.Rotation), 1e-3f);
                Assert.GreaterOrEqual(segment.Position.z, -Tolerance);
                Assert.LessOrEqual(segment.Position.z, 3f + Tolerance);
            }
        }

        [Test]
        public void WindowSegmentsSitAtTheExpectedHeights()
        {
            OpeningModel window = Window();
            RoomModel room = BuildRectangularRoom(2.5f, window);

            WallRenderSpec wall = WallFor(WallRenderer.BuildWalls(room), "c1", "c2");

            float sillTop = window.sillHeightM;
            float headBottom = window.sillHeightM + window.heightM;

            // The sill segment directly under the window: y-centre 0.45, height 0.9.
            Assert.IsTrue(
                wall.Segments.Any(s =>
                    Mathf.Abs(s.Position.y - sillTop * 0.5f) < Tolerance &&
                    Mathf.Abs(s.Scale.y - sillTop) < Tolerance),
                "Expected a sill segment beneath the window.");

            // The header segment above it: spans 2.0 -> 2.5.
            Assert.IsTrue(
                wall.Segments.Any(s =>
                    Mathf.Abs(s.Position.y - (headBottom + room.heightM) * 0.5f) < Tolerance &&
                    Mathf.Abs(s.Scale.y - (room.heightM - headBottom)) < Tolerance),
                "Expected a header segment above the window.");
        }

        [Test]
        public void EverySegmentKeepsTheWallThickness()
        {
            RoomModel room = BuildRectangularRoom(2.5f, Door(), Window());

            foreach (WallRenderSpec wall in WallRenderer.BuildWalls(room))
            {
                foreach (WallSegmentSpec segment in wall.Segments)
                {
                    Assert.AreEqual(WallRenderer.WallThicknessM, segment.Scale.z, Tolerance);
                }
            }
        }

        [Test]
        public void MalformedOpeningLeavesTheWallSolidWithoutThrowing()
        {
            var diagnostics = new List<string>();
            OpeningModel broken = Door(offsetM: float.NaN);
            RoomModel room = BuildRectangularRoom(2.5f, broken);

            IReadOnlyList<WallRenderSpec> walls = null;
            Assert.DoesNotThrow(() => walls = WallRenderer.BuildWalls(room, diagnostics));

            Assert.AreEqual(4, walls.Count);
            Assert.IsTrue(walls.All(w => w.Segments.Count == 1));
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void NullOpeningEntryDoesNotThrow()
        {
            RoomModel room = BuildRectangularRoom();
            room.openings = new OpeningModel[] { null };

            IReadOnlyList<WallRenderSpec> walls = null;
            Assert.DoesNotThrow(() => walls = WallRenderer.BuildWalls(room));

            Assert.AreEqual(4, walls.Count);
        }

        [Test]
        public void DoorWindowFixtureProducesTheExpectedOpenings()
        {
            bool loaded = FixtureLoader.TryLoadFromFile(
                Path.Combine(RepoRoot(), "fixtures", "room-with-door-window-v1.json"),
                out SceneSnapshot snapshot,
                out string error);
            Assert.IsTrue(loaded, error);

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(snapshot.room);

            Assert.AreEqual(4, walls.Count);

            WallRenderSpec doorWall = WallFor(walls, "c0", "c1");
            WallRenderSpec windowWall = WallFor(walls, "c1", "c2");

            // door-1: offset 1.2, width 0.9, sill 0, height 2.05 on a 4 m wall.
            Assert.AreEqual(4f * 2.5f - 0.9f * 2.05f, SolidArea(doorWall), Tolerance);
            Assert.IsFalse(
                doorWall.Segments.Any(s =>
                    s.Slice.CenterU > 1.2f && s.Slice.CenterU < 2.1f && s.Slice.CenterV < 2.05f),
                "No segment may sit inside the doorway.");

            // window-1: offset 0.9, width 1.2, sill 0.9, height 1.1 on a 3 m wall.
            Assert.AreEqual(3f * 2.5f - 1.2f * 1.1f, SolidArea(windowWall), Tolerance);
            Assert.IsFalse(
                windowWall.Segments.Any(s =>
                    s.Slice.CenterU > 0.9f && s.Slice.CenterU < 2.1f &&
                    s.Slice.CenterV > 0.9f && s.Slice.CenterV < 2.0f),
                "No segment may sit inside the window.");

            // The two untouched walls stay solid.
            Assert.AreEqual(1, WallFor(walls, "c2", "c3").Segments.Count);
            Assert.AreEqual(1, WallFor(walls, "c3", "c0").Segments.Count);
        }

        [Test]
        public void ValidRoomFixtureKeepsItsSingleOpeningWallIntactElsewhere()
        {
            bool loaded = FixtureLoader.TryLoadFromFile(
                Path.Combine(RepoRoot(), "fixtures", "valid-room-v1.json"),
                out SceneSnapshot snapshot,
                out string error);
            Assert.IsTrue(loaded, error);

            IReadOnlyList<WallRenderSpec> walls = WallRenderer.BuildWalls(snapshot.room);

            Assert.AreEqual(4, walls.Count);

            int cutWalls = walls.Count(w => w.Segments.Count > 1);
            Assert.AreEqual(
                snapshot.room.openings.Length,
                cutWalls,
                "Exactly the walls named by an opening may be segmented.");
        }
    }
}
