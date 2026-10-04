using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GhostMap.Shared.Protocol;

namespace GhostMap.Scanner.Networking
{
    public enum PeerDiscoveryState
    {
        Idle,
        Searching,
        Found,
        TimedOut,
        Failed
    }

    /// <summary>
    /// Finds a Viewer on the active local network for the one-tap Send to
    /// Computer path. Discovery sends no room data; the existing TCP client
    /// remains the only sender of protocol-v1 snapshots.
    /// </summary>
    public sealed class PeerDiscoveryClient : IDisposable
    {
        private readonly IPEndPoint broadcastEndpoint;
        private readonly int timeoutMs;
        private readonly object stateLock = new object();
        private int state = (int)PeerDiscoveryState.Idle;
        private UdpClient client;
        private Thread thread;
        private volatile bool running;
        private string host = string.Empty;
        private int port;
        private string computerName = string.Empty;

        public PeerDiscoveryClient(
            IPEndPoint broadcastEndpoint = null,
            int timeoutMs = 5000)
        {
            this.broadcastEndpoint = broadcastEndpoint ?? new IPEndPoint(IPAddress.Broadcast, PeerDiscoveryProtocol.Port);
            this.timeoutMs = timeoutMs;
        }

        public PeerDiscoveryState State
        {
            get => (PeerDiscoveryState)Volatile.Read(ref state);
            private set => Volatile.Write(ref state, (int)value);
        }

        public string LastError { get; private set; } = string.Empty;

        public void Start()
        {
            Stop();

            try
            {
                client = new UdpClient(0);
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = Math.Min(250, timeoutMs);
                byte[] request = Encoding.UTF8.GetBytes(PeerDiscoveryProtocol.Request);
                client.Send(request, request.Length, broadcastEndpoint);
                running = true;
                State = PeerDiscoveryState.Searching;
                LastError = string.Empty;
                thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "GhostMapScannerDiscovery" };
                thread.Start();
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                State = PeerDiscoveryState.Failed;
                CloseClient();
            }
        }

        public bool TryTakeResult(out string discoveredHost, out int discoveredPort, out string discoveredComputerName)
        {
            lock (stateLock)
            {
                if (State != PeerDiscoveryState.Found)
                {
                    discoveredHost = string.Empty;
                    discoveredPort = 0;
                    discoveredComputerName = string.Empty;
                    return false;
                }

                discoveredHost = host;
                discoveredPort = port;
                discoveredComputerName = computerName;
                State = PeerDiscoveryState.Idle;
                return true;
            }
        }

        private void ReceiveLoop()
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            UdpClient socket = client;

            try
            {
                while (running && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] response = socket.Receive(ref remote);
                        if (!PeerDiscoveryProtocol.TryParseResponse(Encoding.UTF8.GetString(response), out int discoveredPort, out string name))
                        {
                            continue;
                        }

                        lock (stateLock)
                        {
                            host = remote.Address.ToString();
                            port = discoveredPort;
                            computerName = name;
                            State = PeerDiscoveryState.Found;
                        }
                        return;
                    }
                    catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
                    {
                        // Poll until the short discovery window expires.
                    }
                }

                if (running)
                {
                    State = PeerDiscoveryState.TimedOut;
                }
            }
            catch (ObjectDisposedException)
            {
                // Stop closed the socket.
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                State = PeerDiscoveryState.Failed;
            }
            finally
            {
                CloseClient();
            }
        }

        public void Stop()
        {
            running = false;
            CloseClient();
            thread?.Join(500);
            thread = null;

            if (State == PeerDiscoveryState.Searching)
            {
                State = PeerDiscoveryState.Idle;
            }
        }

        private void CloseClient()
        {
            try
            {
                client?.Close();
            }
            catch
            {
                // Best-effort shutdown of a socket that may already be closed.
            }

            client = null;
        }

        public void Dispose() => Stop();
    }
}
