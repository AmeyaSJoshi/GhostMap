using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using NUnit.Framework;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task F2 tests for <see cref="OpeningValidator"/>.
    /// The reference room is 4.0 m x 3.0 m and 2.5 m high; wall c0-c1 is 4 m long.
    /// </summary>
    public sealed class OpeningValidatorTests
    {
        private static OpeningModel Door(
            float offsetM = 1.2f,
            float widthM = 0.9f,
            float heightM = 2.05f)
        {
            return new OpeningModel
            {
                id = "door-1",
                type = "door",
                wallStartCornerId = "c0",
                wallEndCornerId = "c1",
                offsetM = offsetM,
                widthM = widthM,
                sillHeightM = 0f,
                heightM = heightM
            };
        }

        private static OpeningModel Window(
            float offsetM = 1.0f,
            float widthM = 1.2f,
            float sillHeightM = 0.9f,
            float heightM = 1.1f)
        {
            return new OpeningModel
            {
                id = "window-1",
                type = "window",
                wallStartCornerId = "c1",
                wallEndCornerId = "c2",
                offsetM = offsetM,
                widthM = widthM,
                sillHeightM = sillHeightM,
                heightM = heightM
            };
        }

        // -------------------------------------------------------------------
        // Accept
        // -------------------------------------------------------------------

        [Test]
        public void AcceptsValidDoor()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = OpeningValidator.Validate(Door(), room);

            Assert.IsTrue(result.IsValid, result.Error);
        }

        [Test]
        public void AcceptsValidWindow()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = OpeningValidator.Validate(Window(), room);

            Assert.IsTrue(result.IsValid, result.Error);
        }

        [Test]
        public void AcceptsOpeningExactlyFillingWall()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = OpeningValidator.Validate(Door(0f, 4f, 2.0f), room);

            Assert.IsTrue(result.IsValid, result.Error);
        }

        // -------------------------------------------------------------------
        // Wall containment
        // -------------------------------------------------------------------

        [Test]
        public void RejectsDoorExtendingPastWallEnd()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            // Wall is 4 m; 3.6 + 0.9 = 4.5 m.
            ValidationResult result = OpeningValidator.Validate(Door(3.6f, 0.9f), room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("wall", result.Error.ToLowerInvariant());
        }

        [Test]
        public void RejectsNegativeOffset()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = OpeningValidator.Validate(Door(-0.5f), room);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void RejectsUnknownWall()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            OpeningModel door = Door();
            door.wallStartCornerId = "c0";
            door.wallEndCornerId = "c2";   // diagonal, not a wall

            ValidationResult result = OpeningValidator.Validate(door, room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("wall", result.Error.ToLowerInvariant());
        }

        // -------------------------------------------------------------------
        // Vertical extent
        // -------------------------------------------------------------------

        [Test]
        public void RejectsOpeningTallerThanRoom()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f, 2.5f);

            ValidationResult result = OpeningValidator.Validate(Door(1.2f, 0.9f, 2.8f), room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("height", result.Error.ToLowerInvariant());
        }

        [Test]
        public void RejectsWindowWhoseTopExceedsCeiling()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f, 2.5f);

            // sill 2.0 + height 1.0 = 3.0 > 2.5
            ValidationResult result = OpeningValidator.Validate(Window(1.0f, 1.2f, 2.0f, 1.0f), room);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void RejectsNegativeSill()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = OpeningValidator.Validate(Window(1.0f, 1.2f, -0.2f, 1.0f), room);

            Assert.IsFalse(result.IsValid);
        }

        // -------------------------------------------------------------------
        // Width bounds
        // -------------------------------------------------------------------

        [Test]
        public void RejectsWidthBelowMinimum()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = OpeningValidator.Validate(Door(1.2f, 0.2f), room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("width", result.Error.ToLowerInvariant());
        }

        [Test]
        public void RejectsWidthAboveMaximum()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(10f, 8f);

            ValidationResult result = OpeningValidator.Validate(Door(0.5f, 4.5f), room);

            Assert.IsFalse(result.IsValid);
        }

        // -------------------------------------------------------------------
        // Type, nulls, non-finite
        // -------------------------------------------------------------------

        [Test]
        public void RejectsUnknownType()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            OpeningModel opening = Door();
            opening.type = "skylight";

            ValidationResult result = OpeningValidator.Validate(opening, room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("type", result.Error.ToLowerInvariant());
        }

        [Test]
        public void RejectsNullOpening()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            Assert.IsFalse(OpeningValidator.Validate(null, room).IsValid);
        }

        [Test]
        public void RejectsNonFiniteDimensions()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();

            ValidationResult result = OpeningValidator.Validate(Door(1.2f, float.NaN), room);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void RejectsWhenRoomHeightNotYetCaptured()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom(4f, 3f, 0f);

            ValidationResult result = OpeningValidator.Validate(Door(), room);

            Assert.IsFalse(result.IsValid,
                "An opening cannot be validated before room height exists.");
        }

        // -------------------------------------------------------------------
        // Overlap
        // -------------------------------------------------------------------

        [Test]
        public void RejectsOverlappingOpeningOnSameWall()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            room.openings = new[] { Door(1.0f, 1.0f) };

            OpeningModel overlapping = Door(1.5f, 1.0f);
            overlapping.id = "door-2";

            ValidationResult result = OpeningValidator.Validate(overlapping, room);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("overlap", result.Error.ToLowerInvariant());
        }

        [Test]
        public void AcceptsAdjacentNonOverlappingOpenings()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            room.openings = new[] { Door(0.2f, 0.9f) };

            OpeningModel second = Door(1.5f, 0.9f);
            second.id = "door-2";

            ValidationResult result = OpeningValidator.Validate(second, room);

            Assert.IsTrue(result.IsValid, result.Error);
        }

        [Test]
        public void AllowsRevalidatingAnAlreadyStoredOpening()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            OpeningModel door = Door();
            room.openings = new[] { door };

            ValidationResult result = OpeningValidator.Validate(door, room);

            Assert.IsTrue(result.IsValid,
                "An opening must not be considered to overlap itself.");
        }

        [Test]
        public void AcceptsOpeningsOnDifferentWallsAtSameOffset()
        {
            RoomModel room = RoomGeometryTests.BuildRectangularRoom();
            room.openings = new[] { Door(1.0f, 0.9f) };

            OpeningModel other = Door(1.0f, 0.9f);
            other.id = "door-2";
            other.wallStartCornerId = "c2";
            other.wallEndCornerId = "c3";

            ValidationResult result = OpeningValidator.Validate(other, room);

            Assert.IsTrue(result.IsValid, result.Error);
        }
    }
}
