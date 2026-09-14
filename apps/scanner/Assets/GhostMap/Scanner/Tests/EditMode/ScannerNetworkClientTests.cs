using System;
using GhostMap.Scanner.Networking;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task S6's <see cref="ScannerNetworkClient"/>, proven against a real
    /// loopback <see cref="FakeViewerListener"/> rather than a mock socket:
    /// actual TCP, actual NDJSON framing, actual reconnect and resend.
    ///
    /// <para>Every test drives <see cref="ScannerNetworkClient.PumpOnce"/>
    /// directly with a hand-controlled clock, exactly as the real background
    /// thread would call it in a loop — see <see cref="ScannerNetworkClient"/>'s
    /// own remarks on why its state machine takes no Unity dependency.</para>
    /// </summary>
    public sealed class ScannerNetworkClientTests
    {
        private static SceneSnapshot BuildSnapshot(string sessionId, int revision, string phase, bool finalized)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = sessionId,
                revision = revision,
                scanPhase = phase,
                finalized = finalized,
                closureErrorM = 0.05f,
                room = new RoomModel
                {
                    id = "room-1",
                    name = "Room",
                    heightM = 2.5f,
                    corners = Array.Empty<CornerModel>(),
                    openings = Array.Empty<OpeningModel>(),
                    objects = Array.Empty<SceneObjectModel>()
                }
            };
        }

        /// <summary>A 4.0 x 3.0 m room, one door, bed + desk + chair — the same scale as <c>fixtures/valid-room-v1.json</c>.</summary>
        private static SceneSnapshot BuildTypicalMvpSnapshot()
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = "session-typical",
                revision = 12,
                scanPhase = "ReadyToFinalize",
                finalized = false,
                closureErrorM = 0.054f,
                room = new RoomModel
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
                            id = "bed-1", type = "bed", center = new Vec3Dto(1f, 0f, 2.2f),
                            yawDeg = 90f, widthM = 1.52f, depthM = 2.03f, heightM = 0.6f
                        },
                        new SceneObjectModel
                        {
                            id = "desk-1", type = "desk", center = new Vec3Dto(3.2f, 0f, 0.5f),
                            yawDeg = 0f, widthM = 1.4f, depthM = 0.7f, heightM = 0.75f
                        },
                        new SceneObjectModel
                        {
                            id = "chair-1", type = "chair", center = new Vec3Dto(3.2f, 0f, 1.3f),
                            yawDeg = 180f, widthM = 0.5f, depthM = 0.5f, heightM = 0.9f
                        }
                    }
                }
            };
        }

        private sealed class FakeClock
        {
            public float NowSeconds;

            public float Get() => NowSeconds;
        }

        [Test]
        public void ConnectingSendsHelloThenTheCurrentSnapshotOverARealTcpConnection()
        {
            using var listener = new FakeViewerListener();
            SceneSnapshot snapshot = BuildSnapshot("session-1", 3, "FloorLocked", false);
            var client = new ScannerNetworkClient(() => snapshot, "1.0", "test-device");

            client.RequestConnect("127.0.0.1", listener.Port);
            client.PumpOnce();

            Assert.IsTrue(
                FakeViewerListener.WaitUntil(() => listener.LinesForConnection(0).Count >= 2),
                "The listener should have received hello + snapshot.");

            Assert.AreEqual(NetworkConnectionState.Connected, client.State);

            var lines = listener.LinesForConnection(0);
            Assert.AreEqual(2, lines.Count, "Exactly one hello line and one snapshot line, each terminated once.");

            Assert.IsTrue(ProtocolSerializer.TryDeserialize(lines[0], out object helloObject, out string helloError), helloError);
            Assert.IsInstanceOf<HelloMessage>(helloObject);
            var hello = (HelloMessage)helloObject;
            Assert.AreEqual("session-1", hello.sessionId);
            Assert.AreEqual(ProtocolConstants.ProtocolVersion, hello.protocolVersion);

            Assert.IsTrue(ProtocolSerializer.TryDeserialize(lines[1], out object snapshotObject, out string snapshotError), snapshotError);
            Assert.IsInstanceOf<SceneSnapshotMessage>(snapshotObject);
            var snapshotMessage = (SceneSnapshotMessage)snapshotObject;
            Assert.AreEqual(3, snapshotMessage.snapshot.revision);
            Assert.AreEqual("session-1", snapshotMessage.snapshot.sessionId);
        }

        [Test]
        public void HeartbeatIsSentAfterTheIntervalElapsesWhileIdle()
        {
            using var listener = new FakeViewerListener();
            SceneSnapshot snapshot = BuildSnapshot("session-2", 0, "Boot", false);
            var clock = new FakeClock();
            var client = new ScannerNetworkClient(() => snapshot, "1.0", "test-device", clock.Get);

            client.RequestConnect("127.0.0.1", listener.Port);
            client.PumpOnce();
            Assert.IsTrue(FakeViewerListener.WaitUntil(() => listener.LinesForConnection(0).Count >= 2));

            // Below the interval: no heartbeat yet.
            clock.NowSeconds = ProtocolConstants.HeartbeatIntervalSeconds - 0.5f;
            client.PumpOnce();
            System.Threading.Thread.Sleep(50);
            Assert.AreEqual(2, listener.LinesForConnection(0).Count, "No heartbeat is due yet.");

            // Past the interval: exactly one heartbeat.
            clock.NowSeconds = ProtocolConstants.HeartbeatIntervalSeconds + 0.1f;
            client.PumpOnce();

            Assert.IsTrue(FakeViewerListener.WaitUntil(() => listener.LinesForConnection(0).Count >= 3));

            var lines = listener.LinesForConnection(0);
            Assert.AreEqual(3, lines.Count, "Exactly one heartbeat line, one JSON object per line.");
            Assert.IsTrue(ProtocolSerializer.TryDeserialize(lines[2], out object heartbeatObject, out string error), error);
            Assert.IsInstanceOf<HeartbeatMessage>(heartbeatObject);
        }

        [Test]
        public void AConnectionFailureLeavesTheClientRetryingAndTouchesNothingElse()
        {
            SceneSnapshot snapshot = BuildSnapshot("session-3", 7, "AddObjects", false);
            var clock = new FakeClock();
            var client = new ScannerNetworkClient(() => snapshot, "1.0", "test-device", clock.Get);

            // Nothing listens on this loopback port.
            using var throwaway = new FakeViewerListener();
            int deadPort = throwaway.Port;
            throwaway.Dispose();

            client.RequestConnect("127.0.0.1", deadPort);
            client.PumpOnce();

            Assert.AreEqual(NetworkConnectionState.Retrying, client.State);
            Assert.IsNotEmpty(client.LastError);

            // The "local scan state" this class must never touch is whatever
            // latestSnapshotProvider returns; a failed connect must not have
            // mutated it.
            Assert.AreEqual(7, snapshot.revision);
            Assert.AreEqual("AddObjects", snapshot.scanPhase);
        }

        [Test]
        public void ANetworkSendFailureLeavesTheClientRetryingAndTouchesNothingElse()
        {
            using var listener = new FakeViewerListener();
            SceneSnapshot snapshot = BuildSnapshot("session-4", 2, "CaptureHeight", false);
            var clock = new FakeClock();
            var client = new ScannerNetworkClient(() => snapshot, "1.0", "test-device", clock.Get);

            client.RequestConnect("127.0.0.1", listener.Port);
            client.PumpOnce();
            Assert.IsTrue(FakeViewerListener.WaitUntil(() => listener.LinesForConnection(0).Count >= 2));
            Assert.AreEqual(NetworkConnectionState.Connected, client.State);

            listener.CloseConnection(0);

            // Force a write: past the heartbeat interval, PumpOnce tries to
            // send one into the now-closed socket.
            clock.NowSeconds = ProtocolConstants.HeartbeatIntervalSeconds + 0.5f;

            Assert.IsTrue(
                FakeViewerListener.WaitUntil(
                    () =>
                    {
                        client.PumpOnce();
                        return client.State == NetworkConnectionState.Retrying;
                    },
                    timeoutMs: 3000,
                    pollMs: 20),
                $"Expected Retrying after a write to a closed socket, got {client.State}.");

            Assert.IsNotEmpty(client.LastError);
            Assert.AreEqual(2, snapshot.revision);
            Assert.AreEqual("CaptureHeight", snapshot.scanPhase);
        }

        [Test]
        public void ReconnectResendsTheLatestSnapshotWithItsRevisionUnchanged()
        {
            using var listener = new FakeViewerListener();
            SceneSnapshot snapshot = BuildSnapshot("session-5", 9, "AddOpenings", false);
            var clock = new FakeClock();
            var client = new ScannerNetworkClient(() => snapshot, "1.0", "test-device", clock.Get);

            client.RequestConnect("127.0.0.1", listener.Port);
            client.PumpOnce();
            Assert.IsTrue(FakeViewerListener.WaitUntil(() => listener.LinesForConnection(0).Count >= 2));

            listener.CloseConnection(0);
            clock.NowSeconds = ProtocolConstants.HeartbeatIntervalSeconds + 0.5f;
            Assert.IsTrue(
                FakeViewerListener.WaitUntil(
                    () =>
                    {
                        client.PumpOnce();
                        return client.State == NetworkConnectionState.Retrying;
                    },
                    pollMs: 20));

            // Advance past the 2-second reconnect interval and pump again.
            clock.NowSeconds += ProtocolConstants.ReconnectIntervalSeconds + 0.5f;

            Assert.IsTrue(
                FakeViewerListener.WaitUntil(
                    () =>
                    {
                        client.PumpOnce();
                        return listener.ConnectionCount >= 2 && listener.LinesForConnection(1).Count >= 2;
                    },
                    pollMs: 20));

            Assert.AreEqual(NetworkConnectionState.Connected, client.State);

            var secondConnectionLines = listener.LinesForConnection(1);
            Assert.IsTrue(ProtocolSerializer.TryDeserialize(secondConnectionLines[1], out object obj, out string error), error);
            var resent = (SceneSnapshotMessage)obj;

            Assert.AreEqual(9, resent.snapshot.revision, "Reconnect must resend the same revision, not reset it.");
            Assert.AreEqual("session-5", resent.snapshot.sessionId);
        }

        [Test]
        public void ReconnectAfterFinalizationResendsTheFinalizedSnapshot()
        {
            using var listener = new FakeViewerListener();

            // A mutable closure over "snapshot", exactly like
            // ScanWorkflowController.Snapshot: the client always reads
            // whatever this returns *at send time*, not a copy frozen at
            // construction — the same property that lets a real
            // TryFinalize() be picked up without reconstructing the client.
            SceneSnapshot snapshot = BuildSnapshot("session-6", 20, "ReadyToFinalize", false);
            var clock = new FakeClock();
            var client = new ScannerNetworkClient(() => snapshot, "1.0", "test-device", clock.Get);

            client.RequestConnect("127.0.0.1", listener.Port);
            client.PumpOnce();
            Assert.IsTrue(FakeViewerListener.WaitUntil(() => listener.LinesForConnection(0).Count >= 2));

            // Finalize between connections, exactly as
            // ScanWorkflowController.TryFinalize would: same session, higher
            // revision, finalized = true, phase = Finalized.
            snapshot = BuildSnapshot("session-6", 21, "Finalized", true);

            listener.CloseConnection(0);
            clock.NowSeconds = ProtocolConstants.HeartbeatIntervalSeconds + 0.5f;
            client.PumpOnce();
            Assert.AreEqual(NetworkConnectionState.Retrying, client.State);

            clock.NowSeconds += ProtocolConstants.ReconnectIntervalSeconds + 0.5f;
            Assert.IsTrue(
                FakeViewerListener.WaitUntil(
                    () =>
                    {
                        client.PumpOnce();
                        return listener.ConnectionCount >= 2 && listener.LinesForConnection(1).Count >= 2;
                    },
                    pollMs: 20));

            var secondConnectionLines = listener.LinesForConnection(1);
            Assert.IsTrue(ProtocolSerializer.TryDeserialize(secondConnectionLines[1], out object obj, out string error), error);
            var resent = (SceneSnapshotMessage)obj;

            Assert.IsTrue(resent.snapshot.finalized);
            Assert.AreEqual("Finalized", resent.snapshot.scanPhase);
            Assert.AreEqual(21, resent.snapshot.revision);
        }

        [Test]
        public void EveryLineIsExactlyOneJsonObjectWithNoEmbeddedNewline()
        {
            using var listener = new FakeViewerListener();
            SceneSnapshot snapshot = BuildTypicalMvpSnapshot();
            var client = new ScannerNetworkClient(() => snapshot, "1.0", "test-device");

            client.RequestConnect("127.0.0.1", listener.Port);
            client.PumpOnce();

            Assert.IsTrue(FakeViewerListener.WaitUntil(() => listener.LinesForConnection(0).Count >= 2));

            foreach (string line in listener.LinesForConnection(0))
            {
                Assert.IsFalse(string.IsNullOrEmpty(line));
                Assert.IsFalse(line.Contains('\n'), "A line read by StreamReader.ReadLine must not itself contain a newline.");
                Assert.IsTrue(ProtocolSerializer.TryDeserialize(line, out _, out string error), $"Line failed to parse: {error}");
            }
        }

        [Test]
        public void ATypicalMvpSnapshotStaysWellUnderTheProtocolLineCap()
        {
            SceneSnapshot snapshot = BuildTypicalMvpSnapshot();
            var message = new SceneSnapshotMessage { sessionId = snapshot.sessionId, snapshot = snapshot };

            string line = ProtocolSerializer.SerializeLine(message);
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(line);

            Assert.Less(byteCount, ProtocolConstants.MaxLineLengthBytes);
        }
    }
}
