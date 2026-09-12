using GhostMap.Shared.Domain;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task F1 contract tests for scene schema v1.
    ///
    /// These assert that a complete scene snapshot survives a JsonUtility round
    /// trip with every field preserved. JsonUtility is the serializer the
    /// protocol uses (implementation plan section 7.3), so the schema must stay
    /// inside what JsonUtility supports: concrete [Serializable] types, public
    /// fields, arrays rather than interfaces, and no polymorphism.
    /// </summary>
    public sealed class SceneSchemaTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>
        /// Builds the canonical F1 scene: a 4.0 m x 3.0 m room, 2.5 m high,
        /// with one door and three furniture objects.
        /// </summary>
        private static SceneSnapshot BuildCompleteSnapshot()
        {
            var room = new RoomModel
            {
                id = "room-1",
                name = "Bedroom",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
                },
                openings = new[]
                {
                    new OpeningModel
                    {
                        id = "door-1",
                        type = "door",
                        wallStartCornerId = "c0",
                        wallEndCornerId = "c1",
                        offsetM = 1.2f,
                        widthM = 0.9f,
                        sillHeightM = 0f,
                        heightM = 2.05f
                    }
                },
                objects = new[]
                {
                    new SceneObjectModel
                    {
                        id = "bed-1",
                        type = "bed",
                        center = new Vec3Dto(1.0f, 0f, 2.2f),
                        yawDeg = 90f,
                        widthM = 1.52f,
                        depthM = 2.03f,
                        heightM = 0.60f
                    },
                    new SceneObjectModel
                    {
                        id = "desk-1",
                        type = "desk",
                        center = new Vec3Dto(3.2f, 0f, 0.5f),
                        yawDeg = 0f,
                        widthM = 1.40f,
                        depthM = 0.70f,
                        heightM = 0.75f
                    },
                    new SceneObjectModel
                    {
                        id = "chair-1",
                        type = "chair",
                        center = new Vec3Dto(3.2f, 0f, 1.3f),
                        yawDeg = 180f,
                        widthM = 0.50f,
                        depthM = 0.50f,
                        heightM = 0.90f
                    }
                }
            };

            return new SceneSnapshot
            {
                schemaVersion = 1,
                sessionId = "session-abc",
                revision = 7,
                scanPhase = "AddObjects",
                finalized = false,
                closureErrorM = 0.054f,
                room = room
            };
        }

        private static SceneSnapshot RoundTrip(SceneSnapshot source)
        {
            string json = JsonUtility.ToJson(source);
            Assert.IsNotNull(json, "JsonUtility produced null JSON.");
            Assert.IsNotEmpty(json, "JsonUtility produced empty JSON.");
            return JsonUtility.FromJson<SceneSnapshot>(json);
        }

        // -------------------------------------------------------------------
        // Vec3Dto
        // -------------------------------------------------------------------

        [Test]
        public void Vec3Dto_ConvertsToAndFromVector3()
        {
            var original = new Vector3(1.25f, -3.5f, 9.75f);

            Vec3Dto dto = Vec3Dto.FromVector3(original);
            Vector3 restored = dto.ToVector3();

            Assert.AreEqual(original.x, restored.x, Tolerance);
            Assert.AreEqual(original.y, restored.y, Tolerance);
            Assert.AreEqual(original.z, restored.z, Tolerance);
        }

        // -------------------------------------------------------------------
        // Snapshot-level fields
        // -------------------------------------------------------------------

        [Test]
        public void Snapshot_PreservesSchemaVersionAndRevision()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            Assert.AreEqual(1, restored.schemaVersion, "schemaVersion must survive the round trip.");
            Assert.AreEqual(7, restored.revision, "revision must survive the round trip.");
        }

        [Test]
        public void Snapshot_PreservesSessionMetadata()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            Assert.AreEqual("session-abc", restored.sessionId);
            Assert.AreEqual("AddObjects", restored.scanPhase);
            Assert.IsFalse(restored.finalized);
            Assert.AreEqual(0.054f, restored.closureErrorM, Tolerance);
        }

        [Test]
        public void Snapshot_PreservesFinalizedFlag()
        {
            SceneSnapshot source = BuildCompleteSnapshot();
            source.finalized = true;

            SceneSnapshot restored = RoundTrip(source);

            Assert.IsTrue(restored.finalized, "finalized must survive the round trip.");
        }

        // -------------------------------------------------------------------
        // Corners
        // -------------------------------------------------------------------

        [Test]
        public void Snapshot_PreservesFourCornersInOrder()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            Assert.IsNotNull(restored.room, "room must survive the round trip.");
            Assert.IsNotNull(restored.room.corners, "corners must survive the round trip.");
            Assert.AreEqual(4, restored.room.corners.Length, "Exactly four corners are expected.");

            Assert.AreEqual("c0", restored.room.corners[0].id, "Corner order must be preserved.");
            Assert.AreEqual("c1", restored.room.corners[1].id);
            Assert.AreEqual("c2", restored.room.corners[2].id);
            Assert.AreEqual("c3", restored.room.corners[3].id);
        }

        [Test]
        public void Snapshot_PreservesCornerPositions()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            Assert.AreEqual(4f, restored.room.corners[2].position.x, Tolerance);
            Assert.AreEqual(0f, restored.room.corners[2].position.y, Tolerance);
            Assert.AreEqual(3f, restored.room.corners[2].position.z, Tolerance);
        }

        [Test]
        public void Snapshot_AllCornersLieOnFloorPlane()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            foreach (CornerModel corner in restored.room.corners)
            {
                Assert.AreEqual(0f, corner.position.y, Tolerance,
                    $"Corner {corner.id} must lie on the floor plane (Y = 0).");
            }
        }

        // -------------------------------------------------------------------
        // Openings
        // -------------------------------------------------------------------

        [Test]
        public void Snapshot_PreservesOneDoor()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            Assert.IsNotNull(restored.room.openings, "openings must survive the round trip.");
            Assert.AreEqual(1, restored.room.openings.Length, "Exactly one opening is expected.");

            OpeningModel door = restored.room.openings[0];

            Assert.AreEqual("door-1", door.id);
            Assert.AreEqual("door", door.type);
            Assert.AreEqual("c0", door.wallStartCornerId);
            Assert.AreEqual("c1", door.wallEndCornerId);
            Assert.AreEqual(1.2f, door.offsetM, Tolerance);
            Assert.AreEqual(0.9f, door.widthM, Tolerance);
            Assert.AreEqual(0f, door.sillHeightM, Tolerance);
            Assert.AreEqual(2.05f, door.heightM, Tolerance);
        }

        // -------------------------------------------------------------------
        // Scene objects
        // -------------------------------------------------------------------

        [Test]
        public void Snapshot_PreservesThreeObjects()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            Assert.IsNotNull(restored.room.objects, "objects must survive the round trip.");
            Assert.AreEqual(3, restored.room.objects.Length, "Exactly three objects are expected.");
        }

        [Test]
        public void Snapshot_PreservesObjectTransformAndDimensions()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            SceneObjectModel desk = System.Array.Find(restored.room.objects, o => o.id == "desk-1");

            Assert.IsNotNull(desk, "desk-1 must survive the round trip.");
            Assert.AreEqual("desk", desk.type);
            Assert.AreEqual(3.2f, desk.center.x, Tolerance);
            Assert.AreEqual(0f, desk.center.y, Tolerance);
            Assert.AreEqual(0.5f, desk.center.z, Tolerance);
            Assert.AreEqual(0f, desk.yawDeg, Tolerance);
            Assert.AreEqual(1.40f, desk.widthM, Tolerance);
            Assert.AreEqual(0.70f, desk.depthM, Tolerance);
            Assert.AreEqual(0.75f, desk.heightM, Tolerance);
        }

        [Test]
        public void Snapshot_PreservesObjectYaw()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            SceneObjectModel bed = System.Array.Find(restored.room.objects, o => o.id == "bed-1");

            Assert.IsNotNull(bed);
            Assert.AreEqual(90f, bed.yawDeg, Tolerance, "Object yaw must survive the round trip.");
        }

        // -------------------------------------------------------------------
        // Room
        // -------------------------------------------------------------------

        [Test]
        public void Snapshot_PreservesRoomIdentityAndHeight()
        {
            SceneSnapshot restored = RoundTrip(BuildCompleteSnapshot());

            Assert.AreEqual("room-1", restored.room.id);
            Assert.AreEqual("Bedroom", restored.room.name);
            Assert.AreEqual(2.5f, restored.room.heightM, Tolerance);
        }

        /// <summary>
        /// Walls are derived from consecutive corners and must never appear in
        /// the serialized schema. Duplicated wall state is exactly what schema
        /// v1 exists to prevent (implementation plan section 6.5).
        /// </summary>
        [Test]
        public void Snapshot_DoesNotSerializeWalls()
        {
            string json = JsonUtility.ToJson(BuildCompleteSnapshot());

            StringAssert.DoesNotContain("\"walls\"", json,
                "Walls must be derived from corners, never serialized.");
        }

        /// <summary>
        /// An empty room is the state published immediately after floor lock.
        /// It must round trip without throwing.
        /// </summary>
        [Test]
        public void Snapshot_HandlesEmptyRoomAfterFloorLock()
        {
            var snapshot = new SceneSnapshot
            {
                schemaVersion = 1,
                sessionId = "session-empty",
                revision = 1,
                scanPhase = "FloorLocked",
                finalized = false,
                closureErrorM = 0f,
                room = new RoomModel
                {
                    id = "room-1",
                    name = "Room",
                    heightM = 0f,
                    corners = new CornerModel[0],
                    openings = new OpeningModel[0],
                    objects = new SceneObjectModel[0]
                }
            };

            SceneSnapshot restored = RoundTrip(snapshot);

            Assert.AreEqual(0, restored.room.corners.Length);
            Assert.AreEqual(0, restored.room.openings.Length);
            Assert.AreEqual(0, restored.room.objects.Length);
            Assert.AreEqual(0f, restored.room.heightM, Tolerance);
        }
    }
}
