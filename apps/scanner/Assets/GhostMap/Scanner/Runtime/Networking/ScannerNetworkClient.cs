using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;

namespace GhostMap.Scanner.Networking
{
    /// <summary>
    /// The state of <see cref="ScannerNetworkClient"/>'s connection to the
    /// viewer, shown verbatim on the Task S6 connection screen.
    /// </summary>
    public enum NetworkConnectionState
    {
        /// <summary>No host has been requested yet, or a connect attempt has
        /// not started. Distinct from <see cref="Retrying"/>, which implies a
        /// previous attempt failed.</summary>
        Disconnected,

        /// <summary>A connect attempt is due on the next <see cref="ScannerNetworkClient.PumpOnce"/>.</summary>
        Connecting,

        /// <summary>The socket is open and the hello/snapshot handshake has completed.</summary>
        Connected,

        /// <summary>A previous attempt or an established connection failed; a
        /// reconnect is scheduled per protocol v1's 2-second interval.</summary>
        Retrying
    }

    /// <summary>
    /// Task S6's scanner-side TCP client for protocol v1
    /// (<c>docs/contracts/protocol-v1.md</c>): connect, send <c>hello</c> then
    /// the current snapshot, heartbeat every 2 seconds, and reconnect every 2
    /// seconds on failure, resending the latest snapshot once reconnected.
    ///
    /// <para><b>Scanner -&gt; Viewer only.</b> Protocol v1 defines no
    /// viewer-&gt;scanner scene channel (<c>AGENTS.md</c> rule 5,
    /// <c>docs/decisions/ADR-0002-snapshot-protocol.md</c>), so this class only
    /// ever writes to the socket. It never blocks on a read, and a malformed or
    /// hostile peer has nothing to send it in the first place.</para>
    ///
    /// <para><b>Deliberately not a MonoBehaviour.</b> Every rule here — when to
    /// attempt a connection, when a heartbeat is due, how a failure is handled —
    /// is plain C# so it can be driven by a real background thread on device
    /// and by a test calling <see cref="PumpOnce"/> directly with a fake clock,
    /// with no Unity dependency either way. <see cref="PumpOnce"/> touches only
    /// sockets, the outgoing queue and <see cref="ProtocolSerializer"/> — never
    /// a Unity API — so a caller may safely run it on a background thread, which
    /// implementation plan section 7 (Task S6) requires.</para>
    ///
    /// <para><b>Scanner-state isolation.</b> This class never reads or mutates
    /// <c>GhostMap.Scanner.Workflow.ScanWorkflowController</c>. It only reads
    /// the latest snapshot through <see cref="latestSnapshotProvider"/> at the
    /// moments protocol v1 requires (initial connect, reconnect). A connection
    /// failure, a write failure, or an unreachable host therefore cannot corrupt
    /// or lose the captured room — implementation plan section 1.4's "snapshot
    /// synchronization, not event replay" already makes the current snapshot the
    /// only thing that ever needs to be resent.</para>
    /// </summary>
    public sealed class ScannerNetworkClient : ISnapshotSink, IDisposable
    {
        private readonly Func<SceneSnapshot> latestSnapshotProvider;
        private readonly string appVersion;
        private readonly string deviceName;
        private readonly Func<float> nowSeconds;
        private readonly object queueLock = new object();
        private readonly System.Collections.Generic.Queue<object> outgoing =
            new System.Collections.Generic.Queue<object>();

        private TcpClient tcpClient;
        private StreamWriter writer;
        private string host;
        private int port;
        private long sequence;
        private float nextAttemptAtSeconds;
        private float lastHeartbeatSentAtSeconds;

        public ScannerNetworkClient(
            Func<SceneSnapshot> latestSnapshotProvider,
            string appVersion,
            string deviceName,
            Func<float> nowSecondsProvider = null)
        {
            this.latestSnapshotProvider = latestSnapshotProvider
                ?? throw new ArgumentNullException(nameof(latestSnapshotProvider));
            this.appVersion = appVersion ?? string.Empty;
            this.deviceName = deviceName ?? string.Empty;
            nowSeconds = nowSecondsProvider ?? DefaultNowSeconds;

            State = NetworkConnectionState.Disconnected;
            LastError = string.Empty;
        }

        public NetworkConnectionState State { get; private set; }

        /// <summary>The most recent failure message, or empty when the last attempt succeeded.</summary>
        public string LastError { get; private set; }

        public string Host => host;

        public int Port => port;

        /// <summary>
        /// Requests a connection to <paramref name="targetHost"/>:<paramref name="targetPort"/>.
        /// Safe to call again with a new target at any time — including while
        /// already connected, which closes the old socket first. The actual
        /// attempt happens on the next <see cref="PumpOnce"/>.
        /// </summary>
        public void RequestConnect(string targetHost, int targetPort)
        {
            CloseSocketQuietly();

            host = targetHost;
            port = targetPort;
            State = NetworkConnectionState.Connecting;
            nextAttemptAtSeconds = nowSeconds();
        }

        /// <summary>
        /// Enqueues one or more messages to be sent, in order, the next time
        /// the client is connected. Thread-safe: the producer (the scan
        /// workflow, ticked from the main thread) and the consumer
        /// (<see cref="PumpOnce"/>, ticked from a background thread) run
        /// concurrently.
        ///
        /// <para>Passing multiple messages in one call keeps them adjacent in
        /// the queue even if another thread's <see cref="PumpOnce"/> is running
        /// concurrently — used to keep a final <c>scene.snapshot</c> and the
        /// <c>scan.finalized</c> that must follow it together
        /// (<see cref="Workflow.ScanWorkflowController.TryFinalize"/> /
        /// protocol v1 section 3.5).</para>
        /// </summary>
        public void Enqueue(params object[] messages)
        {
            if (messages == null || messages.Length == 0)
            {
                return;
            }

            lock (queueLock)
            {
                foreach (object message in messages)
                {
                    outgoing.Enqueue(message);
                }
            }
        }

        /// <summary>
        /// One non-blocking-by-design step of the client's state machine:
        /// attempt a connection if one is due, otherwise drain the outgoing
        /// queue and send a heartbeat if one is due. Call this in a loop from a
        /// background thread on device, or directly from a test with a fake
        /// clock for deterministic control.
        /// </summary>
        public void PumpOnce()
        {
            if (string.IsNullOrEmpty(host))
            {
                return;
            }

            float now = nowSeconds();

            if (State == NetworkConnectionState.Connecting || State == NetworkConnectionState.Retrying)
            {
                if (now < nextAttemptAtSeconds)
                {
                    return;
                }

                AttemptConnect(now);
                return;
            }

            if (State == NetworkConnectionState.Connected)
            {
                try
                {
                    DrainQueue();
                    MaybeSendHeartbeat(now);
                }
                catch (Exception exception)
                {
                    HandleFailure(exception.Message, now);
                }
            }
        }

        private void AttemptConnect(float now)
        {
            try
            {
                CloseSocketQuietly();

                tcpClient = new TcpClient();
                tcpClient.Connect(host, port);

                writer = new StreamWriter(tcpClient.GetStream(), new UTF8Encoding(false))
                {
                    NewLine = ProtocolConstants.LineTerminator,
                    AutoFlush = true
                };

                sequence = 0;
                State = NetworkConnectionState.Connected;
                LastError = string.Empty;

                // A fresh connection makes any snapshot still sitting in the
                // queue from before this connect stale: protocol v1's "resend
                // the current snapshot" always means the latest one, not a
                // replay of everything that happened while disconnected.
                lock (queueLock)
                {
                    outgoing.Clear();
                }

                SendHandshake(now);
            }
            catch (Exception exception)
            {
                HandleFailure(exception.Message, now);
            }
        }

        /// <summary>
        /// Protocol v1 section 6: on connect and on every reconnect, send
        /// <c>hello</c> then immediately the current snapshot, then resume
        /// heartbeats.
        /// </summary>
        private void SendHandshake(float now)
        {
            var hello = new HelloMessage
            {
                sessionId = CurrentSessionId(),
                appVersion = appVersion,
                deviceName = deviceName
            };
            StampAndWrite(hello);

            SceneSnapshot snapshot = latestSnapshotProvider();
            if (snapshot != null)
            {
                var snapshotMessage = new SceneSnapshotMessage
                {
                    sessionId = snapshot.sessionId,
                    snapshot = snapshot
                };
                StampAndWrite(snapshotMessage);
            }

            lastHeartbeatSentAtSeconds = now;
        }

        private void DrainQueue()
        {
            while (true)
            {
                object message;

                lock (queueLock)
                {
                    if (outgoing.Count == 0)
                    {
                        return;
                    }

                    message = outgoing.Dequeue();
                }

                StampAndWrite(message);
            }
        }

        private void MaybeSendHeartbeat(float now)
        {
            if (now - lastHeartbeatSentAtSeconds < ProtocolConstants.HeartbeatIntervalSeconds)
            {
                return;
            }

            StampAndWrite(new HeartbeatMessage { sessionId = CurrentSessionId() });
            lastHeartbeatSentAtSeconds = now;
        }

        private void StampAndWrite(object message)
        {
            if (message is WireMessageHeader header)
            {
                header.sequence = sequence++;
                header.unixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (string.IsNullOrEmpty(header.sessionId))
                {
                    header.sessionId = CurrentSessionId();
                }
            }

            string line = ProtocolSerializer.Serialize(message);
            writer.WriteLine(line);
        }

        private string CurrentSessionId() => latestSnapshotProvider()?.sessionId ?? string.Empty;

        private void HandleFailure(string message, float now)
        {
            CloseSocketQuietly();
            State = NetworkConnectionState.Retrying;
            LastError = message ?? string.Empty;
            nextAttemptAtSeconds = now + ProtocolConstants.ReconnectIntervalSeconds;
        }

        private void CloseSocketQuietly()
        {
            try
            {
                writer?.Dispose();
            }
            catch
            {
                // Best-effort teardown of a socket that may already be broken.
            }

            try
            {
                tcpClient?.Close();
            }
            catch
            {
                // Best-effort teardown of a socket that may already be broken.
            }

            writer = null;
            tcpClient = null;
        }

        private static float DefaultNowSeconds()
            => (float)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

        public void Dispose()
        {
            CloseSocketQuietly();
            host = null;
            State = NetworkConnectionState.Disconnected;
        }
    }
}
