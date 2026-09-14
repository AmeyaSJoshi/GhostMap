using System;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Networking;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V1's <see cref="ViewerTcpServer"/>, proven against a real
    /// <see cref="FakeScannerClient"/> over loopback TCP: actual sockets,
    /// actual NDJSON framing, actual reconnect and oversized/malformed
    /// rejection — never a mocked stream.
    /// </summary>
    public sealed class ViewerTcpServerTests
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

        /// <summary>Binds an ephemeral loopback port, then frees it immediately for <see cref="ViewerTcpServer"/> to bind.</summary>
        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        [Test]
        public void AcceptsARealTcpConnectionAndParsesHello()
        {
            int port = FreePort();
            var server = new ViewerTcpServer(port);
            server.Start();

            try
            {
                using var scanner = new FakeScannerClient(port);
                scanner.SendMessage(new HelloMessage
                {
                    sessionId = "session-1",
                    appVersion = "1.0",
                    deviceName = "test-phone"
                });

                object message = DequeueUntil(server, m => m is HelloMessage);
                var hello = (HelloMessage)message;
                Assert.AreEqual("session-1", hello.sessionId);
                Assert.AreEqual("test-phone", hello.deviceName);
                Assert.AreEqual(ServerConnectionState.Connected, server.State);
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void ParsesSnapshotHeartbeatAndFinalizedMessages()
        {
            int port = FreePort();
            var server = new ViewerTcpServer(port);
            server.Start();

            try
            {
                using var scanner = new FakeScannerClient(port);
                SceneSnapshot snapshot = BuildSnapshot("session-2", 4, "AddOpenings", false);
                scanner.SendMessage(new SceneSnapshotMessage { sessionId = "session-2", snapshot = snapshot });
                scanner.SendMessage(new HeartbeatMessage { sessionId = "session-2" });
                scanner.SendMessage(new ScanFinalizedMessage { sessionId = "session-2", finalRevision = 4 });

                object snapshotMessage = DequeueUntil(server, m => m is SceneSnapshotMessage);
                Assert.AreEqual(4, ((SceneSnapshotMessage)snapshotMessage).snapshot.revision);

                object heartbeat = DequeueUntil(server, m => m is HeartbeatMessage);
                Assert.IsInstanceOf<HeartbeatMessage>(heartbeat);

                object finalized = DequeueUntil(server, m => m is ScanFinalizedMessage);
                Assert.AreEqual(4, ((ScanFinalizedMessage)finalized).finalRevision);
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void MalformedJsonIsRejectedButTheConnectionAndSubsequentLinesSurvive()
        {
            int port = FreePort();
            var server = new ViewerTcpServer(port);
            server.Start();

            try
            {
                using var scanner = new FakeScannerClient(port);
                scanner.SendLine("{ this is not valid json");
                scanner.SendMessage(new HeartbeatMessage { sessionId = "session-3" });

                Assert.IsTrue(FakeScannerClient.WaitUntil(() => !string.IsNullOrEmpty(server.LastError)));

                object heartbeat = DequeueUntil(server, m => m is HeartbeatMessage);
                Assert.IsInstanceOf<HeartbeatMessage>(heartbeat);
                Assert.AreEqual(ServerConnectionState.Connected, server.State);
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void OversizedLineIsRejectedAndFramingResyncsForTheNextLine()
        {
            int port = FreePort();
            var server = new ViewerTcpServer(port);
            server.Start();

            try
            {
                using var scanner = new FakeScannerClient(port);

                // One line with no newline, larger than the protocol cap.
                string oversized = new string('a', ProtocolConstants.MaxLineLengthBytes + 1000);
                scanner.SendRawNoNewline(oversized);
                scanner.SendRaw(Encoding.UTF8.GetBytes("\n"));
                scanner.SendMessage(new HeartbeatMessage { sessionId = "session-4" });

                Assert.IsTrue(FakeScannerClient.WaitUntil(() => !string.IsNullOrEmpty(server.LastError)));
                StringAssert.Contains("exceeding", server.LastError);

                object heartbeat = DequeueUntil(server, m => m is HeartbeatMessage);
                Assert.IsInstanceOf<HeartbeatMessage>(heartbeat, "Framing must resync after the oversized line.");
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void ANewConnectionReplacesTheOldOne()
        {
            int port = FreePort();
            var server = new ViewerTcpServer(port);
            server.Start();

            try
            {
                var first = new FakeScannerClient(port);
                first.SendMessage(new HelloMessage { sessionId = "session-a", appVersion = "1.0", deviceName = "phone-a" });
                DequeueUntil(server, m => m is HelloMessage);
                Assert.AreEqual(ServerConnectionState.Connected, server.State);

                first.CloseAbortively();

                using var second = new FakeScannerClient(port);
                second.SendMessage(new HelloMessage { sessionId = "session-b", appVersion = "1.0", deviceName = "phone-b" });

                object hello = DequeueUntil(server, m => m is HelloMessage && ((HelloMessage)m).sessionId == "session-b");
                Assert.AreEqual("session-b", ((HelloMessage)hello).sessionId);
                Assert.AreEqual(ServerConnectionState.Connected, server.State);

                first.Dispose();
            }
            finally
            {
                server.Dispose();
            }
        }

        [Test]
        public void DisconnectMovesStateToDisconnected()
        {
            int port = FreePort();
            var server = new ViewerTcpServer(port);
            server.Start();

            try
            {
                var scanner = new FakeScannerClient(port);
                scanner.SendMessage(new HelloMessage { sessionId = "session-5", appVersion = "1.0", deviceName = "phone" });
                DequeueUntil(server, m => m is HelloMessage);

                scanner.CloseAbortively();
                scanner.Dispose();

                Assert.IsTrue(
                    FakeScannerClient.WaitUntil(() => server.State == ServerConnectionState.Disconnected),
                    $"Expected Disconnected, got {server.State}.");
            }
            finally
            {
                server.Dispose();
            }
        }

        private static object DequeueUntil(ViewerTcpServer server, Func<object, bool> predicate, int timeoutMs = 3000)
        {
            object found = null;
            bool ok = FakeScannerClient.WaitUntil(
                () =>
                {
                    while (server.TryDequeueMessage(out object message))
                    {
                        if (predicate(message))
                        {
                            found = message;
                            return true;
                        }
                    }

                    return false;
                },
                timeoutMs);

            Assert.IsTrue(ok, "Expected message was not received within the timeout.");
            return found;
        }
    }
}
