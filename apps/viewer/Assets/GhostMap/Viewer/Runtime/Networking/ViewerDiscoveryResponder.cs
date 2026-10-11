using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GhostMap.Shared.Protocol;

namespace GhostMap.Viewer.Networking
{
    /// <summary>
    /// Answers the Scanner's small UDP discovery request while the Viewer is
    /// listening for its normal TCP connection. It never receives or sends
    /// scene data; it only returns the already-running TCP endpoint.
    /// </summary>
    public sealed class ViewerDiscoveryResponder : IDisposable
    {
        private readonly int discoveryPort;
        private readonly int tcpPort;
        private readonly string computerName;
        private UdpClient client;
        private Thread thread;
        private volatile bool running;

        public ViewerDiscoveryResponder(int discoveryPort, int tcpPort, string computerName)
        {
            this.discoveryPort = discoveryPort;
            this.tcpPort = tcpPort;
            this.computerName = computerName ?? string.Empty;
        }

        public string LastError { get; private set; } = string.Empty;

        public void Start()
        {
            if (running)
            {
                return;
            }

            try
            {
                client = new UdpClient(discoveryPort);
                running = true;
                LastError = string.Empty;
                thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "GhostMapViewerDiscovery" };
                thread.Start();
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                CloseClient();
            }
        }

        private void ReceiveLoop()
        {
            UdpClient socket = client;
            try
            {
                while (running)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] request = socket.Receive(ref remote);
                    if (!string.Equals(Encoding.UTF8.GetString(request), PeerDiscoveryProtocol.Request, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    byte[] response = Encoding.UTF8.GetBytes(PeerDiscoveryProtocol.CreateResponse(tcpPort, computerName));
                    socket.Send(response, response.Length, remote);
                }
            }
            catch (SocketException)
            {
                // Closing the socket is the normal way Stop wakes this thread.
            }
            catch (ObjectDisposedException)
            {
                // Closing the socket is the normal way Stop wakes this thread.
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
            }
        }

        public void Stop()
        {
            running = false;
            CloseClient();
            thread?.Join(500);
            thread = null;
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
