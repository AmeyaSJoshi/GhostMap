using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task F2 tests for <see cref="RoomValidator"/>, covering the rules in
    /// implementation plan sections 1.1 and 9.1.
    /// </summary>
    public sealed class RoomValidatorTests
    {
        private static List<CornerModel> Corners(params Vector3[] points)
        {
            var list = new List<CornerModel>();
            for (int i = 0; i < points.Length; i++)
            {
                list.Add(new CornerModel
                {
                    id = $"c{i}",
                    position = Vec3Dto.FromVector3(points[i])
                });
            }

            return list;
        }

        // -------------------------------------------------------------------
        // ValidateNewCorner
        // -------------------------------------------------------------------

        [Test]
        public void NewCorner_AcceptsFirstCorner()
        {
            ValidationResult result = RoomValidator.ValidateNewCorner(
                Corners(), new Vector3(0f, 0f, 0f));

            Assert.IsTrue(result.IsValid, result.Error);
        }

        [Test]
        public void NewCorner_AcceptsWellSpacedSecondCorner()
        {
            ValidationResult result = RoomValidator.ValidateNewCorner(
                Corners(Vector3.zero), new Vector3(4f, 0f, 0f));

            Assert.IsTrue(result.IsValid, result.Error);
        }

        [Test]
        public void NewCorner_RejectsCornerTooCloseToPrevious()
        {
            // 0.30 m apart, below the 0.50 m minimum.
            ValidationResult result = RoomValidator.ValidateNewCorner(
                Corners(Vector3.zero), new Vector3(0.30f, 0f, 0f));

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("0.50", result.Error);
        }

        [Test]
        public void NewCorner_RejectsNonFiniteValues()
        {
            ValidationResult result = RoomValidator.ValidateNewCorner(
                Corners(Vector3.zero), new Vector3(float.NaN, 0f, 0f));

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("finite", result.Error);
        }

        [Test]
        public void NewCorner_RejectsCornerOffTheFloorPlane()
        {
            ValidationResult result = RoomValidator.ValidateNewCorner(
                Corners(Vector3.zero), new Vector3(4f, 1.2f, 0f));

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("floor", result.Error);
        }

        [Test]
        public void NewCorner_RejectsCornerTooCloseToNonNeighbor()
        {
            // Candidate is far from the previous corner but almost on top of c0.
            var existing = Corners(
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f));

            ValidationResult result = RoomValidator.ValidateNewCorner(
                existing, new Vector3(0.1f, 0f, 0.05f));

            Assert.IsFalse(result.IsValid,
                "A corner landing on an existing non-neighbor corner must be rejected.");
        }

        [Test]
        public void NewCorner_RejectsFifthCorner()
        {
            var existing = Corners(
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f),
                new Vector3(0f, 0f, 3f));

            ValidationResult result = RoomValidator.ValidateNewCorner(
                existing, new Vector3(2f, 0f, 5f));

            Assert.IsFalse(result.IsValid, "MVP supports exactly four corners.");
            StringAssert.Contains("four", result.Error);
        }

        [Test]
        public void NewCorner_RejectsFourthCornerCreatingBowTie()
        {
            var existing = Corners(
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(0f, 0f, 3f));

            ValidationResult result = RoomValidator.ValidateNewCorner(
                existing, new Vector3(4f, 0f, 3f));

            Assert.IsFalse(result.IsValid,
                "Closing the polygon into a bow-tie must be rejected at capture time.");
        }

        [Test]
        public void NewCorner_AcceptsValidFourthCorner()
        {
            var existing = Corners(
                new Vector3(0f, 0f, 0f),
                new Vector3(4f, 0f, 0f),
                new Vector3(4f, 0f, 3f));

            ValidationResult result = RoomValidator.ValidateNewCorner(
                existing, new Vector3(0f, 0f, 3f));

            Assert.IsTrue(result.IsValid, result.Error);
        }

        // -------------------------------------------------------------------
        // ValidateRoom
        // -------------------------------------------------------------------

        [Test]
        public void Room_AcceptsValidRectangle()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsTrue(result.IsValid, result.Error);
        }

        [Test]
        public void Room_AcceptsEmptyRoomAfterFloorLock()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            room.corners = new CornerModel[0];
            room.heightM = 0f;

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsTrue(result.IsValid,
                "A room with no corners yet is the normal post-floor-lock state.");
        }

        [Test]
        public void Room_AcceptsPartialCornerCaptureInProgress()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            room.corners = new[] { room.corners[0], room.corners[1] };
            room.heightM = 0f;

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsTrue(result.IsValid,
                "Live snapshots arrive mid-capture and must not be rejected.");
        }

        [Test]
        public void Room_RejectsAreaBelowMinimum()
        {
            // 1.0 m x 1.0 m = 1 m^2, below the 2 m^2 floor.
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(1f, 1f);

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("area", result.Error.ToLowerInvariant());
        }

        [Test]
        public void Room_RejectsSelfIntersectingFootprint()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            // Swap the last two corners to create a bow-tie.
            CornerModel tmp = room.corners[2];
            room.corners[2] = room.corners[3];
            room.corners[3] = tmp;

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("intersect", result.Error.ToLowerInvariant());
        }

        [Test]
        public void Room_RejectsWallLongerThanMaximum()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(25f, 3f);

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid, "Walls longer than 20 m are out of MVP scope.");
        }

        [Test]
        public void Room_RejectsWallShorterThanMinimum()
        {
            // 0.30 m wall, below the 0.50 m minimum.
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(0.30f, 8f);

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Room_RejectsHeightBelowMinimum()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f, 1.5f);

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("height", result.Error.ToLowerInvariant());
        }

        [Test]
        public void Room_RejectsHeightAboveMaximum()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f, 5f);

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Room_AcceptsUncapturedHeight()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f, 0f);

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsTrue(result.IsValid,
                "Height 0 means not yet captured, which is valid mid-scan.");
        }

        [Test]
        public void Room_RejectsExtremeInternalAngle()
        {
            // A very thin sliver quad produces angles outside 35-145 degrees.
            var room = new RoomModel
            {
                id = "room-1",
                name = "Sliver",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(10f, 0f, 0f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(10f, 0f, 0.8f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(1.0f, 0f, 0.15f) }
                },
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid, "Extreme internal angles are rejected for MVP sanity.");
            StringAssert.Contains("angle", result.Error.ToLowerInvariant());
        }

        [Test]
        public void Room_RejectsNullRoom()
        {
            ValidationResult result = RoomValidator.ValidateRoom(null);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Room_RejectsDuplicateCornerIds()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            room.corners[2].id = "c0";

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid, "Corner IDs must be unique; openings reference them.");
        }

        // -------------------------------------------------------------------
        // Closure classification
        // -------------------------------------------------------------------

        [Test]
        public void Closure_ClassifiesExcellent()
        {
            Assert.AreEqual(ClosureQuality.Excellent, RoomValidator.ClassifyClosure(0.05f));
        }

        [Test]
        public void Closure_ClassifiesAcceptableAtYellowBand()
        {
            Assert.AreEqual(ClosureQuality.Acceptable, RoomValidator.ClassifyClosure(0.12f));
        }

        [Test]
        public void Closure_RejectsAboveThreshold()
        {
            Assert.AreEqual(ClosureQuality.Rejected, RoomValidator.ClassifyClosure(0.20f));
        }

        [Test]
        public void Closure_BoundariesAreInclusive()
        {
            Assert.AreEqual(ClosureQuality.Excellent, RoomValidator.ClassifyClosure(0.08f));
            Assert.AreEqual(ClosureQuality.Acceptable, RoomValidator.ClassifyClosure(0.15f));
        }

        [Test]
        public void Room_RejectsReflexInteriorAngle()
        {
            // Regression: a "dart" footprint is a simple, non-self-intersecting
            // quad with one 216.1 degree interior corner. Area (10.0 m2) and all
            // four wall lengths are inside their limits, so it must be rejected
            // on the 35-145 degree interior-angle rule alone. Before the fix the
            // reflex corner was reported as its complement, 143.9 degrees, and
            // the room was accepted.
            var room = new RoomModel
            {
                id = "room-1",
                name = "Dart",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(4.98f, 0f, 0f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(1.19f, 0f, 4.71f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(1.07f, 0f, 1.36f) }
                },
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid,
                "A reflex interior angle is outside the supported 35-145 degree range.");
            StringAssert.Contains("angle", result.Error.ToLowerInvariant());
        }

        [Test]
        public void Room_RejectsReflexInteriorAngleRegardlessOfWinding()
        {
            // The same dart, captured in the opposite direction. Winding must not
            // change whether a room is accepted.
            var room = new RoomModel
            {
                id = "room-1",
                name = "Dart reversed",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(1.07f, 0f, 1.36f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(1.19f, 0f, 4.71f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(4.98f, 0f, 0f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 0f) }
                },
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };

            ValidationResult result = RoomValidator.ValidateRoom(room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("angle", result.Error.ToLowerInvariant());
        }

        [Test]
        public void Room_AcceptsValidRectangleInEitherWinding()
        {
            // Guards the interior-angle fix against over-rejection: a plain
            // rectangle must stay valid whichever way the user walked the room.
            var clockwise = new RoomModel
            {
                id = "room-1",
                name = "Bedroom",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 3f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 3f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 0f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 0f) }
                },
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };

            ValidationResult result = RoomValidator.ValidateRoom(clockwise);

            Assert.IsTrue(result.IsValid, result.Error);
        }

    }
}
