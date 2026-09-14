using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Bootstrap;
using GhostMap.Viewer.Networking;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V1's <see cref="ViewerSession"/> end to end over real loopback
    /// TCP: a <see cref="FakeScannerClient"/> plays the scanner, <see cref="ViewerSession.Pump"/>
    /// plays the role Unity's <c>Update()</c> would, and assertions check
    /// exactly what <c>docs/contracts/protocol-v1.md</c> section 6 requires of
    /// the viewer — including that a disconnect never erases the displayed
    /// scene.
    /// </summary>
    public sealed class ViewerSessionTests
    {
        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

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
                    corners = new[]
                    {
                        new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                        new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                        new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                        new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
                    },
                    openings = Array.Empty<OpeningModel>(),
                    objects = Array.Empty<SceneObjectModel>()
                }
            };
        }

        private static void PumpUntil(ViewerSession session, Func<bool> condition, int timeoutMs = 3000)
        {
            bool ok = FakeScannerClient.WaitUntil(
                () =>
                {
                    session.Pump();
                    return condition();
                },
                timeoutMs,
                pollMs: 10);

            Assert.IsTrue(ok, "Condition was not met within the timeout.");
        }

        [Test]
        public void HelloThenSnapshotUpdatesDiagnosticsAndCurrentScene()
        {
            int port = FreePort();
            var session = new ViewerSession(port);
            session.Start();

            try
            {
                using var scanner = new FakeScannerClient(port);
                scanner.SendMessage(new HelloMessage { sessionId = "session-1", appVersion = "1.0", deviceName = "phone" });
                scanner.SendMessage(new SceneSnapshotMessage
                {
                    sessionId = "session-1",
                    snapshot = BuildSnapshot("session-1", 3, "AddOpenings", false)
                });

                PumpUntil(session, () => session.SceneStore.Current != null);

                Assert.AreEqual("session-1", session.LastSessionId);
                Assert.AreEqual("phone", session.LastDeviceName);
                Assert.AreEqual(3, session.SceneStore.Current.revision);
            }
            finally
            {
                session.Dispose();
            }
        }

        [Test]
        public void DisconnectPreservesTheLastAcceptedScene()
        {
            int port = FreePort();
            var session = new ViewerSession(port);
            session.Start();

            try
            {
                var scanner = new FakeScannerClient(port);
                scanner.SendMessage(new HelloMessage { sessionId = "session-2", appVersion = "1.0", deviceName = "phone" });
                scanner.SendMessage(new SceneSnapshotMessage
                {
                    sessionId = "session-2",
                    snapshot = BuildSnapshot("session-2", 5, "ReadyToFinalize", false)
                });

                PumpUntil(session, () => session.SceneStore.Current != null);
                SceneSnapshot beforeDisconnect = session.SceneStore.Current;

                scanner.CloseAbortively();
                scanner.Dispose();

                PumpUntil(session, () => session.Server.State == ServerConnectionState.Disconnected);

                Assert.AreSame(
                    beforeDisconnect, session.SceneStore.Current,
                    "Protocol v1 section 6: the viewer must never erase the displayed scene because the network dropped.");
            }
            finally
            {
                session.Dispose();
            }
        }

        [Test]
        public void ReconnectResendingTheSameFinalizedSnapshotLeavesCurrentUnchanged()
        {
            int port = FreePort();
            var session = new ViewerSession(port);
            session.Start();

            try
            {
                var first = new FakeScannerClient(port);
                first.SendMessage(new HelloMessage { sessionId = "session-3", appVersion = "1.0", deviceName = "phone" });
                first.SendMessage(new SceneSnapshotMessage
                {
                    sessionId = "session-3",
                    snapshot = BuildSnapshot("session-3", 12, "Finalized", true)
                });

                PumpUntil(session, () => session.SceneStore.Current != null && session.SceneStore.Current.finalized);
                SceneSnapshot finalized = session.SceneStore.Current;

                first.CloseAbortively();
                PumpUntil(session, () => session.Server.State == ServerConnectionState.Disconnected);

                using var second = new FakeScannerClient(port);
                second.SendMessage(new HelloMessage { sessionId = "session-3", appVersion = "1.0", deviceName = "phone" });
                second.SendMessage(new SceneSnapshotMessage
                {
                    sessionId = "session-3",
                    snapshot = BuildSnapshot("session-3", 12, "Finalized", true)
                });

                PumpUntil(session, () => session.Server.State == ServerConnectionState.Connected);
                session.Pump();

                Assert.AreSame(finalized, session.SceneStore.Current, "A resent duplicate must not replace the reference.");
                Assert.IsTrue(session.SceneStore.Current.finalized);
                Assert.AreEqual(12, session.SceneStore.Current.revision);

                first.Dispose();
            }
            finally
            {
                session.Dispose();
            }
        }

        [Test]
        public void LoadFixtureAppliesDirectlyWithoutAnyNetworkConnection()
        {
            int port = FreePort();
            var session = new ViewerSession(port);
            session.Start();

            try
            {
                string repoRoot = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "..", ".."));
                string fixturePath = System.IO.Path.Combine(repoRoot, "fixtures", "valid-room-v1.json");

                bool loaded = session.LoadFixture(fixturePath);

                Assert.IsTrue(loaded, session.LastFixtureError);
                Assert.IsNotNull(session.SceneStore.Current);
                Assert.AreEqual("fixture-valid-room-v1", session.SceneStore.Current.sessionId);
                Assert.AreEqual(ServerConnectionState.Listening, session.Server.State, "No scanner ever connected.");
            }
            finally
            {
                session.Dispose();
            }
        }
    }
}
