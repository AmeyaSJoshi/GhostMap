using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GhostMap.Shared.Protocol;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// A real <see cref="TcpClient"/> standing in for the scanner, for Task
    /// V1's <see cref="ViewerTcpServerTests"/>. Connects to a real
    /// <c>ViewerTcpServer</c> over loopback and writes raw lines, so tests
    /// prove actual TCP and actual NDJSON framing rather than a mock.
    /// </summary>
    internal sealed class FakeScannerClient : IDisposable
    {
        private readonly TcpClient client;
        private readonly StreamWriter writer;

        public FakeScannerClient(int port)
        {
            client = new TcpClient();
            client.Connect("127.0.0.1", port);
            writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false))
            {
                NewLine = ProtocolConstants.LineTerminator,
                AutoFlush = true
            };
        }

        public void SendLine(string line) => writer.WriteLine(line);

        public void SendMessage(object message) => writer.WriteLine(ProtocolSerializer.Serialize(message));

        /// <summary>Writes raw bytes with no trailing newline, for oversized-line tests.</summary>
        public void SendRawNoNewline(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            client.GetStream().Write(bytes, 0, bytes.Length);
            client.GetStream().Flush();
        }

        public void SendRaw(byte[] bytes)
        {
            client.GetStream().Write(bytes, 0, bytes.Length);
            client.GetStream().Flush();
        }

        /// <summary>Abortively closes the socket so the peer sees an immediate RST, simulating a network drop.</summary>
        public void CloseAbortively()
        {
            try
            {
                client.Client.LingerState = new LingerOption(true, 0);
                client.Close();
            }
            catch
            {
                // Best-effort.
            }
        }

        public void Dispose()
        {
            try
            {
                writer?.Dispose();
            }
            catch
            {
                // Already closed.
            }

            try
            {
                client?.Close();
            }
            catch
            {
                // Already closed.
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
    }
}
