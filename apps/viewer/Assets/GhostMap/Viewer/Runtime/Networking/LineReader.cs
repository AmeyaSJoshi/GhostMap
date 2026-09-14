using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GhostMap.Shared.Protocol;

namespace GhostMap.Viewer.Networking
{
    internal enum LineReadResult
    {
        /// <summary>A complete line was read; see the reader's out parameter.</summary>
        Ok,

        /// <summary>
        /// A line exceeded <see cref="ProtocolConstants.MaxLineLengthBytes"/>.
        /// It was discarded, not buffered, and the reader has resynced to the
        /// next newline so the connection can continue.
        /// </summary>
        Oversized,

        /// <summary>The stream ended or broke. No more lines are coming.</summary>
        Closed
    }

    /// <summary>
    /// Reads newline-delimited protocol v1 messages from a raw
    /// <see cref="Stream"/> with a bounded buffer, so a peer that never sends
    /// a newline cannot exhaust memory (protocol v1 section 1: "a line longer
    /// than the maximum is rejected, not buffered").
    ///
    /// Deliberately not <see cref="StreamReader.ReadLine"/>: that call buffers
    /// an unterminated line without limit. This type caps the buffer at
    /// <see cref="ProtocolConstants.MaxLineLengthBytes"/>, and once exceeded,
    /// discards bytes (without accumulating them) until the next newline is
    /// found, so a single oversized line does not corrupt the framing of
    /// every line that follows it.
    /// </summary>
    internal sealed class LineReader
    {
        private readonly Stream stream;
        private readonly byte[] readBuffer = new byte[8192];
        private readonly List<byte> pending = new List<byte>();

        public LineReader(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public LineReadResult TryReadLine(out string line)
        {
            line = null;
            bool discarding = false;

            while (true)
            {
                int newlineIndex = pending.IndexOf((byte)'\n');

                if (newlineIndex >= 0)
                {
                    // Two ways a line can be oversized: either "discarding"
                    // was already set by a previous iteration's bounds check,
                    // or the newline arrived in the very same read as the
                    // bytes that pushed pending past the cap — in which case
                    // this is the first and only chance to notice, since the
                    // loop returns as soon as a newline is found.
                    if (discarding || newlineIndex > ProtocolConstants.MaxLineLengthBytes)
                    {
                        pending.RemoveRange(0, newlineIndex + 1);
                        return LineReadResult.Oversized;
                    }

                    var bytes = pending.GetRange(0, newlineIndex);
                    pending.RemoveRange(0, newlineIndex + 1);
                    line = Encoding.UTF8.GetString(bytes.ToArray());
                    return LineReadResult.Ok;
                }

                if (!discarding && pending.Count > ProtocolConstants.MaxLineLengthBytes)
                {
                    discarding = true;
                }

                if (discarding)
                {
                    // Bound memory while resyncing: nothing in the current,
                    // still-unterminated line is worth keeping.
                    pending.Clear();
                }

                int read;
                try
                {
                    read = stream.Read(readBuffer, 0, readBuffer.Length);
                }
                catch
                {
                    return LineReadResult.Closed;
                }

                if (read <= 0)
                {
                    return LineReadResult.Closed;
                }

                for (int i = 0; i < read; i++)
                {
                    pending.Add(readBuffer[i]);
                }
            }
        }
    }
}
