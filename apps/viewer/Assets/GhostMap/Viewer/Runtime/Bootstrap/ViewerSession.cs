using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Networking;
using GhostMap.Viewer.Scene;

namespace GhostMap.Viewer.Bootstrap
{
    /// <summary>
    /// Task V1's session state machine: owns the <see cref="ViewerTcpServer"/>
    /// and the <see cref="ViewerSceneStore"/>, and is the only place that
    /// moves a parsed message from the server's background-thread queue onto
    /// the caller's thread via <see cref="Pump"/>.
    ///
    /// <para><b>Deliberately not a MonoBehaviour</b> — exactly the reasoning
    /// <c>GhostMap.Scanner.Networking.ScannerNetworkClient</c> gives for
    /// itself: every rule here is plain C#, so it can be driven by Unity's
    /// <c>Update()</c> in the real app and by a test calling
    /// <see cref="Pump"/> directly, with no Unity dependency either way.
    /// <see cref="ViewerBootstrap"/> is the thin MonoBehaviour wrapper that
    /// owns one of these and ticks it.</para>
    /// </summary>
    public sealed class ViewerSession : IDisposable
    {
        public ViewerSession(int port)
        {
            Server = new ViewerTcpServer(port);
        }

        public ViewerTcpServer Server { get; }

        public ViewerSceneStore SceneStore { get; } = new ViewerSceneStore();

        /// <summary>The most recent <c>hello</c>'s session id, for diagnostics.</summary>
        public string LastSessionId { get; private set; } = string.Empty;

        /// <summary>The most recent <c>hello</c>'s device name, for diagnostics.</summary>
        public string LastDeviceName { get; private set; } = string.Empty;

        /// <summary>The most recent <c>hello</c>'s app version, for diagnostics.</summary>
        public string LastAppVersion { get; private set; } = string.Empty;

        /// <summary>The most recent snapshot-apply rejection (stale/duplicate revision or invalid room), or empty.</summary>
        public string LastSceneRejection { get; private set; } = string.Empty;

        /// <summary>The most recent fixture-load rejection, or empty.</summary>
        public string LastFixtureError { get; private set; } = string.Empty;

        public void Start() => Server.Start();

        /// <summary>Drains every message currently queued by the server and dispatches each in order.</summary>
        public void Pump()
        {
            while (Server.TryDequeueMessage(out object message))
            {
                Dispatch(message);
            }
        }

        private void Dispatch(object message)
        {
            switch (message)
            {
                case HelloMessage hello:
                    LastSessionId = hello.sessionId;
                    LastDeviceName = hello.deviceName;
                    LastAppVersion = hello.appVersion;
                    break;

                case SceneSnapshotMessage snapshotMessage:
                    if (!SceneStore.TryApplyScannerSnapshot(snapshotMessage.snapshot, out string error))
                    {
                        LastSceneRejection = error;
                    }
                    break;

                case ScanFinalizedMessage:
                    // Diagnostics only in V1: the finalized snapshot already
                    // carries `finalized = true` and is applied above, per
                    // protocol v1 section 3.5 ("the scanner must send the
                    // final snapshot immediately before this message").
                    break;

                case HeartbeatMessage:
                    // Liveness only; nothing to apply.
                    break;
            }
        }

        /// <summary>
        /// Loads a fixture file directly into <see cref="SceneStore"/>,
        /// bypassing the network entirely, per Task V1's "Load fixture"
        /// developer button so viewer development never waits on a scanner.
        /// </summary>
        public bool LoadFixture(string path)
        {
            if (!FixtureLoader.TryLoadFromFile(path, out SceneSnapshot snapshot, out string loadError))
            {
                LastFixtureError = loadError;
                return false;
            }

            if (!SceneStore.TryApplyScannerSnapshot(snapshot, out string applyError))
            {
                LastFixtureError = applyError;
                return false;
            }

            LastFixtureError = string.Empty;
            return true;
        }

        public void Dispose() => Server.Dispose();
    }
}
