using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// A real <see cref="TcpListener"/> standing in for the viewer, for Task
    /// S6's <see cref="ScannerNetworkClientTests"/>. Bound to
    /// <see cref="IPAddress.Loopback"/> on an OS-assigned port, so tests prove
    /// an actual TCP connection and actual newline-delimited framing rather
    /// than a mock.
    ///
    /// <para>Accepts any number of connections in sequence (protocol v1's
    /// reconnect story), recording each one's received lines separately so a
    /// test can tell the first connection's handshake apart from the second's
    /// after a forced drop.</para>
    /// </summary>
    internal sealed class FakeViewerListener : IDisposable
    {
        private sealed class Connection
        {
            public TcpClient Client;
            public readonly List<string> Lines = new List<string>();
        }

        private readonly TcpListener listener;
        private readonly Thread acceptThread;
        private readonly object stateLock = new object();
        private readonly List<Connection> connections = new List<Connection>();
        private volatile bool running;

        public FakeViewerListener()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;

            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "FakeViewerListener.Accept" };
            acceptThread.Start();
        }

        public int Port { get; }

        public int ConnectionCount
        {
            get
            {
                lock (stateLock)
                {
                    return connections.Count;
                }
            }
        }

        public IReadOnlyList<string> LinesForConnection(int index)
        {
            lock (stateLock)
            {
                return index < connections.Count ? new List<string>(connections[index].Lines) : new List<string>();
            }
        }

        /// <summary>
        /// Force-closes one accepted connection's socket, simulating a mid-
        /// session network drop. Uses an abortive close (linger timeout 0) so
        /// the peer gets an immediate RST rather than a graceful FIN — a
        /// half-closed socket can silently accept one more write before the
        /// scanner side notices anything is wrong, which is exactly the
        /// non-determinism this test helper exists to avoid.
        /// </summary>
        public void CloseConnection(int index)
        {
            TcpClient client;

            lock (stateLock)
            {
                if (index >= connections.Count)
                {
                    return;
                }

                client = connections[index].Client;
            }

            try
            {
                client.Client.LingerState = new LingerOption(true, 0);
                client.Close();
            }
            catch
            {
                // Best-effort: the point is that the client-side write fails next.
            }
        }

        /// <summary>Polls <paramref name="condition"/> until it is true or the timeout elapses, returning the final result.</summary>
        public static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000, int pollMs = 10)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(pollMs);
            }

            return condition();
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
                    return;
                }

                var connection = new Connection { Client = client };

                lock (stateLock)
                {
                    connections.Add(connection);
                }

                var readThread = new Thread(() => ReadLoop(connection)) { IsBackground = true, Name = "FakeViewerListener.Read" };
                readThread.Start();
            }
        }

        private void ReadLoop(Connection connection)
        {
            try
            {
                using (connection.Client)
                using (var reader = new StreamReader(connection.Client.GetStream(), Encoding.UTF8))
                {
                    string line;

                    while ((line = reader.ReadLine()) != null)
                    {
                        lock (stateLock)
                        {
                            connection.Lines.Add(line);
                        }
                    }
                }
            }
            catch
            {
                // The socket was closed or broke; nothing more to read.
            }
        }

        public void Dispose()
        {
            running = false;

            try
            {
                listener.Stop();
            }
            catch
            {
                // Already stopped.
            }

            lock (stateLock)
            {
                foreach (Connection connection in connections)
                {
                    try
                    {
                        connection.Client.Close();
                    }
                    catch
                    {
                        // Already closed.
                    }
                }
            }
        }
    }
}
