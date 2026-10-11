using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Networking;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    public sealed class ViewerDiscoveryResponderTests
    {
        [Test]
        public void ValidDiscoveryRequestReceivesTheRunningViewersTcpEndpoint()
        {
            using var portProbe = new UdpClient(0);
            int discoveryPort = ((IPEndPoint)portProbe.Client.LocalEndPoint).Port;
            portProbe.Close();

            using var responder = new ViewerDiscoveryResponder(discoveryPort, 47831, "Test Mac");
            responder.Start();

            using var scanner = new UdpClient(0);
            scanner.Client.ReceiveTimeout = 1500;
            byte[] request = Encoding.UTF8.GetBytes(PeerDiscoveryProtocol.Request);
            scanner.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));

            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] bytes = scanner.Receive(ref remote);

            Assert.IsTrue(PeerDiscoveryProtocol.TryParseResponse(Encoding.UTF8.GetString(bytes), out int tcpPort, out string name));
            Assert.AreEqual(47831, tcpPort);
            Assert.AreEqual("Test Mac", name);
        }
    }
}
