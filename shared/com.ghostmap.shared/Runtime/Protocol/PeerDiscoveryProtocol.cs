using System;

namespace GhostMap.Shared.Protocol
{
    /// <summary>
    /// The tiny UDP discovery exchange used to find a nearby Viewer before the
    /// existing protocol-v1 TCP connection is opened. This is deliberately
    /// connection setup, not another scene protocol: no scene data, revisions,
    /// or authority information is ever carried here.
    /// </summary>
    public static class PeerDiscoveryProtocol
    {
        public const int Port = 47832;
        public const string Request = "ghostmap.discovery.v1.request";
        public const string ResponsePrefix = "ghostmap.discovery.v1.response|";

        public static string CreateResponse(int tcpPort, string computerName)
        {
            if (tcpPort < 1 || tcpPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(tcpPort));
            }

            string safeName = (computerName ?? string.Empty).Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            return ResponsePrefix + tcpPort + "|" + safeName;
        }

        public static bool TryParseResponse(string value, out int tcpPort, out string computerName)
        {
            tcpPort = 0;
            computerName = string.Empty;

            if (string.IsNullOrEmpty(value) || !value.StartsWith(ResponsePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = value.Substring(ResponsePrefix.Length).Split(new[] { '|' }, 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out tcpPort) || tcpPort < 1 || tcpPort > 65535)
            {
                tcpPort = 0;
                return false;
            }

            computerName = parts[1].Trim();
            return !string.IsNullOrEmpty(computerName);
        }
    }
}
