using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GhostMap.Scanner.Networking;
using GhostMap.Shared.Protocol;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    public sealed class PeerDiscoveryClientTests
    {
        [Test]
        public void DiscoveryFindsAViewerAndReturnsItsTcpEndpoint()
        {
            using var portProbe = new UdpClient(0);
            int discoveryPort = ((IPEndPoint)portProbe.Client.LocalEndPoint).Port;
            portProbe.Close();

            using var responder = new UdpClient(discoveryPort);
            var responderThread = new Thread(() =>
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] request = responder.Receive(ref remote);
                Assert.AreEqual(PeerDiscoveryProtocol.Request, Encoding.UTF8.GetString(request));
                byte[] response = Encoding.UTF8.GetBytes(PeerDiscoveryProtocol.CreateResponse(47831, "Test Mac"));
                responder.Send(response, response.Length, remote);
            });
            responderThread.Start();

            using var client = new PeerDiscoveryClient(new IPEndPoint(IPAddress.Loopback, discoveryPort), 1500);
            client.Start();

            Assert.IsTrue(WaitUntil(() => client.State == PeerDiscoveryState.Found), "The local Viewer should answer discovery.");
            Assert.IsTrue(client.TryTakeResult(out string host, out int port, out string name));
            Assert.AreEqual("127.0.0.1", host);
            Assert.AreEqual(47831, port);
            Assert.AreEqual("Test Mac", name);
            Assert.IsTrue(responderThread.Join(1000));
        }

        private static bool WaitUntil(System.Func<bool> predicate)
        {
            for (int attempt = 0; attempt < 75; attempt++)
            {
                if (predicate())
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return false;
        }
    }
}
