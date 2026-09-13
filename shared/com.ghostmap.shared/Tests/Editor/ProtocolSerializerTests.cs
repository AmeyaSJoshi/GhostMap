using System.IO;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task F3 tests for protocol v1.
    ///
    /// The wire format is newline-delimited JSON, so the single most important
    /// invariant is that a serialized message never contains a raw newline.
    /// </summary>
    public sealed class ProtocolSerializerTests
    {
        private const float Tolerance = 1e-4f;

        private static void StampHeader(WireMessageHeader header)
        {
            header.sessionId = "session-abc";
            header.sequence = 42;
            header.unixTimeMs = 1_700_000_000_000L;
        }

        private static SceneSnapshot BuildSnapshot()
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = "session-abc",
                revision = 7,
                scanPhase = "AddObjects",
                finalized = false,
                closureErrorM = 0.054f,
                room = RoomGeometryTests.BuildRectangularRoom()
            };
        }

        // -------------------------------------------------------------------
        // Framing
        // -------------------------------------------------------------------

        [Test]
        public void Serialize_NeverEmitsRawNewline()
        {
            var hello = new HelloMessage { appVersion = "1.0.0", deviceName = "iPhone\nEvil" };
            StampHeader(hello);

            string json = ProtocolSerializer.Serialize(hello);

            Assert.IsFalse(json.Contains("\n"),
                "A raw newline would break newline-delimited framing.");
            Assert.IsFalse(json.Contains("\r"));
        }

        [Test]
        public void SerializeLine_AppendsSingleTerminator()
        {
            var heartbeat = new HeartbeatMessage();
            StampHeader(heartbeat);

            string line = ProtocolSerializer.SerializeLine(heartbeat);

            Assert.IsTrue(line.EndsWith("\n"));
            Assert.AreEqual(1, line.Split('\n').Length - 1, "Exactly one terminator.");
        }

        [Test]
        public void MaxLineLength_MatchesSpecifiedValue()
        {
            Assert.AreEqual(262144, ProtocolConstants.MaxLineLengthBytes);
        }

        [Test]
        public void Port_MatchesSpecifiedValue()
        {
            Assert.AreEqual(47831, ProtocolConstants.Port);
        }

        [Test]
        public void RejectsOversizedLine()
        {
            string oversized = "{\"protocolVersion\":1,\"type\":\"heartbeat\",\"pad\":\""
                + new string('x', ProtocolConstants.MaxLineLengthBytes)
                + "\"}";

            bool ok = ProtocolSerializer.TryDeserialize(oversized, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("length", error.ToLowerInvariant());
        }

        // -------------------------------------------------------------------
        // Round trips, one per message type
        // -------------------------------------------------------------------

        [Test]
        public void Hello_RoundTrips()
        {
            var source = new HelloMessage { appVersion = "1.2.3", deviceName = "iPhone 15" };
            StampHeader(source);

            bool ok = ProtocolSerializer.TryDeserialize(
                ProtocolSerializer.Serialize(source), out object message, out string error);

            Assert.IsTrue(ok, error);
            var restored = message as HelloMessage;
            Assert.IsNotNull(restored, "hello must deserialize to HelloMessage.");
            Assert.AreEqual(ProtocolConstants.TypeHello, restored.type);
            Assert.AreEqual(1, restored.protocolVersion);
            Assert.AreEqual("session-abc", restored.sessionId);
            Assert.AreEqual(42, restored.sequence);
            Assert.AreEqual(1_700_000_000_000L, restored.unixTimeMs);
            Assert.AreEqual("1.2.3", restored.appVersion);
            Assert.AreEqual("iPhone 15", restored.deviceName);
        }

        [Test]
        public void Heartbeat_RoundTrips()
        {
            var source = new HeartbeatMessage();
            StampHeader(source);

            bool ok = ProtocolSerializer.TryDeserialize(
                ProtocolSerializer.Serialize(source), out object message, out string error);

            Assert.IsTrue(ok, error);
            var restored = message as HeartbeatMessage;
            Assert.IsNotNull(restored);
            Assert.AreEqual(ProtocolConstants.TypeHeartbeat, restored.type);
            Assert.AreEqual("session-abc", restored.sessionId);
        }

        [Test]
        public void PhonePose_RoundTrips()
        {
            var source = new PhonePoseMessage
            {
                position = new Vec3Dto(1.5f, 1.6f, -2.25f),
                yawDeg = 123.5f,
                trackingState = "Tracking",
                notTrackingReason = "None"
            };
            StampHeader(source);

            bool ok = ProtocolSerializer.TryDeserialize(
                ProtocolSerializer.Serialize(source), out object message, out string error);

            Assert.IsTrue(ok, error);
            var restored = message as PhonePoseMessage;
            Assert.IsNotNull(restored);
            Assert.AreEqual(ProtocolConstants.TypePhonePose, restored.type);
            Assert.AreEqual(1.5f, restored.position.x, Tolerance);
            Assert.AreEqual(-2.25f, restored.position.z, Tolerance);
            Assert.AreEqual(123.5f, restored.yawDeg, Tolerance);
            Assert.AreEqual("Tracking", restored.trackingState);
            Assert.AreEqual("None", restored.notTrackingReason);
        }

        [Test]
        public void SceneSnapshot_RoundTripsEntireRoom()
        {
            var source = new SceneSnapshotMessage { snapshot = BuildSnapshot() };
            StampHeader(source);

            bool ok = ProtocolSerializer.TryDeserialize(
                ProtocolSerializer.Serialize(source), out object message, out string error);

            Assert.IsTrue(ok, error);
            var restored = message as SceneSnapshotMessage;
            Assert.IsNotNull(restored);
            Assert.AreEqual(ProtocolConstants.TypeSceneSnapshot, restored.type);

            SceneSnapshot snapshot = restored.snapshot;
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot.schemaVersion);
            Assert.AreEqual(7, snapshot.revision);
            Assert.AreEqual("AddObjects", snapshot.scanPhase);
            Assert.AreEqual(0.054f, snapshot.closureErrorM, Tolerance);
            Assert.AreEqual(4, snapshot.room.corners.Length);
            Assert.AreEqual(2.5f, snapshot.room.heightM, Tolerance);
        }

        [Test]
        public void ScanFinalized_RoundTrips()
        {
            var source = new ScanFinalizedMessage { finalRevision = 19 };
            StampHeader(source);

            bool ok = ProtocolSerializer.TryDeserialize(
                ProtocolSerializer.Serialize(source), out object message, out string error);

            Assert.IsTrue(ok, error);
            var restored = message as ScanFinalizedMessage;
            Assert.IsNotNull(restored);
            Assert.AreEqual(ProtocolConstants.TypeScanFinalized, restored.type);
            Assert.AreEqual(19, restored.finalRevision);
        }

        // -------------------------------------------------------------------
        // Rejection
        // -------------------------------------------------------------------

        [Test]
        public void RejectsUnknownProtocolVersion()
        {
            string json = "{\"protocolVersion\":2,\"type\":\"heartbeat\",\"sessionId\":\"s\"}";

            bool ok = ProtocolSerializer.TryDeserialize(json, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("version", error.ToLowerInvariant());
        }

        [Test]
        public void RejectsUnknownMessageType()
        {
            string json = "{\"protocolVersion\":1,\"type\":\"teleport\",\"sessionId\":\"s\"}";

            bool ok = ProtocolSerializer.TryDeserialize(json, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("type", error.ToLowerInvariant());
        }

        [Test]
        public void RejectsMissingType()
        {
            string json = "{\"protocolVersion\":1,\"sessionId\":\"s\"}";

            Assert.IsFalse(ProtocolSerializer.TryDeserialize(json, out _, out _));
        }

        [Test]
        public void RejectsMalformedJson()
        {
            bool ok = ProtocolSerializer.TryDeserialize("{not json at all", out _, out string error);

            Assert.IsFalse(ok);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RejectsEmptyInput()
        {
            Assert.IsFalse(ProtocolSerializer.TryDeserialize("", out _, out _));
            Assert.IsFalse(ProtocolSerializer.TryDeserialize(null, out _, out _));
            Assert.IsFalse(ProtocolSerializer.TryDeserialize("   ", out _, out _));
        }

        [Test]
        public void RejectsSnapshotMessageWithoutSnapshot()
        {
            string json = "{\"protocolVersion\":1,\"type\":\"scene.snapshot\",\"sessionId\":\"s\"}";

            bool ok = ProtocolSerializer.TryDeserialize(json, out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("snapshot", error.ToLowerInvariant());
        }

        [Test]
        public void RejectsSnapshotWithUnknownSchemaVersion()
        {
            SceneSnapshot snapshot = BuildSnapshot();
            snapshot.schemaVersion = 99;

            var source = new SceneSnapshotMessage { snapshot = snapshot };
            StampHeader(source);

            bool ok = ProtocolSerializer.TryDeserialize(
                ProtocolSerializer.Serialize(source), out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("schema", error.ToLowerInvariant());
        }

        [Test]
        public void TolerantOfUnknownFields()
        {
            string json = "{\"protocolVersion\":1,\"type\":\"heartbeat\"," +
                          "\"sessionId\":\"s\",\"futureField\":\"ignored\"}";

            Assert.IsTrue(ProtocolSerializer.TryDeserialize(json, out _, out string error), error);
        }

        // -------------------------------------------------------------------
        // Revision arbitration
        // -------------------------------------------------------------------

        [Test]
        public void Revision_AcceptsFirstSnapshotWhenNoneHeld()
        {
            SceneSnapshot incoming = BuildSnapshot();

            Assert.AreEqual(
                SnapshotAcceptance.AcceptNewSession,
                SnapshotRevisionPolicy.Evaluate(null, incoming));
        }

        [Test]
        public void Revision_AcceptsNewSessionEvenAtLowerRevision()
        {
            SceneSnapshot current = BuildSnapshot();
            current.revision = 50;

            SceneSnapshot incoming = BuildSnapshot();
            incoming.sessionId = "session-xyz";
            incoming.revision = 1;

            Assert.AreEqual(
                SnapshotAcceptance.AcceptNewSession,
                SnapshotRevisionPolicy.Evaluate(current, incoming));
        }

        [Test]
        public void Revision_AcceptsHigherRevisionInSameSession()
        {
            SceneSnapshot current = BuildSnapshot();
            SceneSnapshot incoming = BuildSnapshot();
            incoming.revision = current.revision + 1;

            Assert.AreEqual(
                SnapshotAcceptance.AcceptNewerRevision,
                SnapshotRevisionPolicy.Evaluate(current, incoming));
        }

        [Test]
        public void Revision_IgnoresEqualRevisionAsDuplicate()
        {
            SceneSnapshot current = BuildSnapshot();
            SceneSnapshot incoming = BuildSnapshot();

            Assert.AreEqual(
                SnapshotAcceptance.IgnoreDuplicateRevision,
                SnapshotRevisionPolicy.Evaluate(current, incoming));
        }

        [Test]
        public void Revision_IgnoresStaleRevision()
        {
            SceneSnapshot current = BuildSnapshot();
            SceneSnapshot incoming = BuildSnapshot();
            incoming.revision = current.revision - 1;

            Assert.AreEqual(
                SnapshotAcceptance.IgnoreStaleRevision,
                SnapshotRevisionPolicy.Evaluate(current, incoming));
        }

        [Test]
        public void Revision_ShouldApplyOnlyForAcceptingOutcomes()
        {
            Assert.IsTrue(SnapshotRevisionPolicy.ShouldApply(SnapshotAcceptance.AcceptNewSession));
            Assert.IsTrue(SnapshotRevisionPolicy.ShouldApply(SnapshotAcceptance.AcceptNewerRevision));
            Assert.IsFalse(SnapshotRevisionPolicy.ShouldApply(SnapshotAcceptance.IgnoreDuplicateRevision));
            Assert.IsFalse(SnapshotRevisionPolicy.ShouldApply(SnapshotAcceptance.IgnoreStaleRevision));
        }

        // -------------------------------------------------------------------
        // Fixtures
        // -------------------------------------------------------------------

        private static string FixturePath(string fileName)
        {
            // Application.dataPath is <repo>/shared/TestProject/Assets
            return Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "..", "fixtures", fileName));
        }

        private static SceneSnapshot LoadFixture(string fileName)
        {
            string path = FixturePath(fileName);
            Assert.IsTrue(File.Exists(path), $"Fixture not found: {path}");

            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonUtility.FromJson<SceneSnapshot>(json);
        }

        [Test]
        public void Fixture_ValidRoomParsesAndValidates()
        {
            SceneSnapshot snapshot = LoadFixture("valid-room-v1.json");

            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot.schemaVersion);
            Assert.AreEqual(4, snapshot.room.corners.Length);
            Assert.AreEqual(2.5f, snapshot.room.heightM, Tolerance);
            Assert.AreEqual(1, snapshot.room.openings.Length, "Fixture has one door.");
            Assert.AreEqual(3, snapshot.room.objects.Length, "Fixture has bed, desk and chair.");

            ValidationResult result = RoomValidator.ValidateRoom(snapshot.room);
            Assert.IsTrue(result.IsValid, result.Error);

            foreach (OpeningModel opening in snapshot.room.openings)
            {
                ValidationResult o = OpeningValidator.Validate(opening, snapshot.room);
                Assert.IsTrue(o.IsValid, o.Error);
            }

            foreach (SceneObjectModel obj in snapshot.room.objects)
            {
                ValidationResult f = FurnitureValidator.Validate(obj);
                Assert.IsTrue(f.IsValid, f.Error);
            }
        }

        [Test]
        public void Fixture_ValidRoomHasExpectedFootprint()
        {
            SceneSnapshot snapshot = LoadFixture("valid-room-v1.json");

            Assert.AreEqual(12f, GhostMap.Shared.Geometry.MeasurementMath.RoomAreaM2(snapshot.room),
                0.01f, "Fixture room is 4.0 m x 3.0 m.");
        }

        [Test]
        public void Fixture_DoorWindowRoomParsesAndValidates()
        {
            SceneSnapshot snapshot = LoadFixture("room-with-door-window-v1.json");

            Assert.IsNotNull(snapshot);
            Assert.AreEqual(2, snapshot.room.openings.Length, "Door plus window.");

            ValidationResult result = RoomValidator.ValidateRoom(snapshot.room);
            Assert.IsTrue(result.IsValid, result.Error);

            bool hasDoor = false;
            bool hasWindow = false;

            foreach (OpeningModel opening in snapshot.room.openings)
            {
                ValidationResult o = OpeningValidator.Validate(opening, snapshot.room);
                Assert.IsTrue(o.IsValid, o.Error);

                if (opening.type == "door") hasDoor = true;
                if (opening.type == "window") hasWindow = true;
            }

            Assert.IsTrue(hasDoor, "Fixture must contain a door.");
            Assert.IsTrue(hasWindow, "Fixture must contain a window.");
        }

        [Test]
        public void Fixture_MalformedRoomParsesButFailsValidation()
        {
            SceneSnapshot snapshot = LoadFixture("malformed-room-v1.json");

            Assert.IsNotNull(snapshot, "Malformed fixture must still be well-formed JSON.");

            ValidationResult result = RoomValidator.ValidateRoom(snapshot.room);

            Assert.IsFalse(result.IsValid,
                "The malformed fixture must be rejected by validation.");
            StringAssert.Contains("height", result.Error.ToLowerInvariant());
        }

        [Test]
        public void Fixture_ValidRoomSurvivesWireRoundTrip()
        {
            SceneSnapshot snapshot = LoadFixture("valid-room-v1.json");

            var message = new SceneSnapshotMessage { snapshot = snapshot };
            StampHeader(message);

            bool ok = ProtocolSerializer.TryDeserialize(
                ProtocolSerializer.Serialize(message), out object restored, out string error);

            Assert.IsTrue(ok, error);

            var typed = restored as SceneSnapshotMessage;
            Assert.IsNotNull(typed);
            Assert.AreEqual(snapshot.room.corners.Length, typed.snapshot.room.corners.Length);
            Assert.AreEqual(snapshot.room.objects.Length, typed.snapshot.room.objects.Length);
        }

        [Test]
        public void AbsentProtocolVersion_IsReadAsVersionOne()
        {
            // WireMessageHeader.protocolVersion is initialised to 1 so senders
            // cannot forget it, and JsonUtility runs field initialisers before
            // overwriting from JSON. A line that omits the field therefore parses
            // as v1 rather than as v0. This is deliberate and documented in
            // protocol-v1.md section 4; the test exists so a change to the
            // default is a visible wire-behaviour change, not a silent one.
            const string json = "{\"type\":\"heartbeat\",\"sessionId\":\"s\"}";

            bool ok = ProtocolSerializer.TryDeserialize(json, out object message, out string error);

            Assert.IsTrue(ok, error);
            Assert.IsInstanceOf<HeartbeatMessage>(message);
            Assert.AreEqual(ProtocolConstants.ProtocolVersion,
                ((WireMessageHeader)message).protocolVersion);
        }

        [Test]
        public void Fixture_MalformedRoomFailsOnExactlyOneRule()
        {
            // The malformed fixture exists to pinpoint a regression, which only
            // works while height is the single rule it breaks.
            SceneSnapshot snapshot = LoadFixture("malformed-room-v1.json");

            Assert.IsFalse(RoomValidator.ValidateRoom(snapshot.room).IsValid);

            RoomModel repaired = snapshot.room;
            repaired.heightM = 2.5f;

            ValidationResult result = RoomValidator.ValidateRoom(repaired);
            Assert.IsTrue(result.IsValid,
                "With its height corrected the malformed fixture must be fully valid: "
                + result.Error);

            foreach (OpeningModel opening in repaired.openings)
            {
                ValidationResult o = OpeningValidator.Validate(opening, repaired);
                Assert.IsTrue(o.IsValid, o.Error);
            }

            foreach (SceneObjectModel obj in repaired.objects)
            {
                ValidationResult f = FurnitureValidator.Validate(obj);
                Assert.IsTrue(f.IsValid, f.Error);
            }
        }

    }
}
