using GhostMap.Shared.Protocol;
using NUnit.Framework;

namespace GhostMap.Shared.Tests.Editor
{
    public sealed class PeerDiscoveryProtocolTests
    {
        [Test]
        public void ResponseRoundTripsTheViewerPortAndSafeComputerName()
        {
            string response = PeerDiscoveryProtocol.CreateResponse(47831, "Ameya's Mac");

            Assert.IsTrue(PeerDiscoveryProtocol.TryParseResponse(response, out int port, out string name));
            Assert.AreEqual(47831, port);
            Assert.AreEqual("Ameya's Mac", name);
        }

        [TestCase("")]
        [TestCase("not-a-ghostmap-response")]
        [TestCase("ghostmap.discovery.v1.response|0|Mac")]
        [TestCase("ghostmap.discovery.v1.response|47831|")]
        public void InvalidResponsesAreRejected(string response)
        {
            Assert.IsFalse(PeerDiscoveryProtocol.TryParseResponse(response, out _, out _));
        }
    }
}
