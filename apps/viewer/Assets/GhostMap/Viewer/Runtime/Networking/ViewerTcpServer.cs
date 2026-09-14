using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GhostMap.Shared.Protocol;

namespace GhostMap.Viewer.Networking
{
    /// <summary>
    /// The viewer's connection state to whichever scanner is currently talking
    /// to it, shown verbatim on the Task V1 diagnostics screen.
    /// </summary>
    public enum ServerConnectionState
    {
        /// <summary><see cref="ViewerTcpServer.Start"/> has not been called yet.</summary>
        NotListening,

        /// <summary>Listening for a scanner; none is connected yet.</summary>
        Listening,

        /// <summary>A scanner is connected.</summary>
        Connected,

        /// <summary>
        /// A scanner was connected and disconnected. Protocol v1 section 6:
        /// the viewer must never erase the displayed scene because the
        /// network dropped, so <see cref="Scene.ViewerSceneStore.Current"/> is
        /// untouched by this transition.
        /// </summary>
        Disconnected
    }

    /// <summary>
    /// Task V1's TCP server: listens on protocol v1's port, accepts scanner
    /// connections, and parses each newline-delimited message with the shared
    /// <see cref="ProtocolSerializer"/> — never a viewer-side reimplementation
    /// of the wire format (<c>AGENTS.md</c> rule 3).
    ///
    /// <para><b>One active connection.</b> Protocol v1 section 1: the viewer
    /// accepts one active scanner connection; a new connection replaces the
    /// old, already-disconnected session. This class closes any previous
    /// socket the instant a new one is accepted.</para>
    ///
    /// <para><b>Threading.</b> A background thread accepts connections; each
    /// accepted connection gets its own background read thread. Parsed
    /// messages are pushed onto a thread-safe queue and must be drained on the
    /// main thread via <see cref="TryDequeueMessage"/> — mirroring
    /// <c>ScannerNetworkClient</c>'s own separation of a plain-C# network
    /// state machine from Unity's main thread.</para>
    ///
    /// <para><b>Bounded lines.</b> Reading uses <see cref="LineReader"/>,
    /// never <see cref="System.IO.StreamReader.ReadLine"/>, so a peer that
    /// never sends a newline cannot exhaust memory.</para>
    /// </summary>
    public sealed class ViewerTcpServer : IDisposable
    {
        private readonly int port;
        private readonly object stateLock = new object();
        private readonly Queue<object> incoming = new Queue<object>();

        private TcpListener listener;
        private Thread acceptThread;
        private volatile bool running;
        private long connectionEpoch;
        private TcpClient currentClient;

        public ViewerTcpServer(int port)
        {
            this.port = port;
        }

        public ServerConnectionState State { get; private set; } = ServerConnectionState.NotListening;

        /// <summary>The remote endpoint of the current or most recent connection.</summary>
        public string RemoteEndpoint { get; private set; } = string.Empty;

        /// <summary>The most recent protocol-level rejection (malformed JSON, oversized line), or empty.</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>Starts listening. Safe to call once; a second call is a no-op while already running.</summary>
        public void Start()
        {
            if (running)
            {
                return;
            }

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            running = true;
            State = ServerConnectionState.Listening;

            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "ViewerTcpServer.Accept" };
            acceptThread.Start();
        }

        public void Stop()
        {
            running = false;

            try
            {
                listener?.Stop();
            }
            catch
            {
                // Already stopped.
            }

            try
            {
                currentClient?.Close();
            }
            catch
            {
                // Already closed.
            }

            if (State == ServerConnectionState.Connected || State == ServerConnectionState.Listening)
            {
                State = ServerConnectionState.NotListening;
            }
        }

        /// <summary>
        /// Dequeues one parsed message (a concrete protocol v1 message type)
        /// for main-thread dispatch. Call in a loop until it returns false.
        /// </summary>
        public bool TryDequeueMessage(out object message)
        {
            lock (stateLock)
            {
                if (incoming.Count == 0)
                {
                    message = null;
                    return false;
                }

                message = incoming.Dequeue();
                return true;
            }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;

                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch
                {
                    // Listener was stopped.
                    return;
                }

                // Protocol v1 section 1: one active scanner connection. Close
                // any previous socket outright rather than trusting it to
                // already be dead — a new connection always wins.
                try
                {
                    currentClient?.Close();
                }
                catch
                {
                    // Already closed.
                }

                currentClient = client;
                long epoch = Interlocked.Increment(ref connectionEpoch);
                RemoteEndpoint = SafeRemoteEndpoint(client);
                State = ServerConnectionState.Connected;

                var readThread = new Thread(() => ReadLoop(client, epoch))
                {
                    IsBackground = true,
                    Name = "ViewerTcpServer.Read"
                };
                readThread.Start();
            }
        }

        private void ReadLoop(TcpClient client, long epoch)
        {
            try
            {
                var reader = new LineReader(client.GetStream());

                while (running)
                {
                    LineReadResult result = reader.TryReadLine(out string line);

                    if (result == LineReadResult.Closed)
                    {
                        break;
                    }

                    if (result == LineReadResult.Oversized)
                    {
                        LastError = $"Rejected a line exceeding {ProtocolConstants.MaxLineLengthBytes} bytes.";
                        continue;
                    }

                    // A malformed line is a normal event on a local network
                    // (protocol v1 section 4): log it and keep the connection,
                    // rather than tearing it down over one bad message.
                    if (!ProtocolSerializer.TryDeserialize(line, out object message, out string error))
                    {
                        LastError = error;
                        continue;
                    }

                    lock (stateLock)
                    {
                        incoming.Enqueue(message);
                    }
                }
            }
            catch
            {
                // The socket broke; fall through to the disconnect handling below.
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch
                {
                    // Already closed.
                }

                // Only the read thread for the still-current connection may
                // report a disconnect: a superseded connection's dying read
                // thread must not clobber the new connection's Connected state.
                if (Interlocked.Read(ref connectionEpoch) == epoch && running)
                {
                    State = ServerConnectionState.Disconnected;
                }
            }
        }

        private static string SafeRemoteEndpoint(TcpClient client)
        {
            try
            {
                return client.Client.RemoteEndPoint?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
