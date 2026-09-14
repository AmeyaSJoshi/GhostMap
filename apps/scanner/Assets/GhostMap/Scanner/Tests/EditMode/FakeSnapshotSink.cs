using System.Collections.Generic;
using GhostMap.Scanner.Networking;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Records every batch handed to <see cref="Enqueue"/>, in order, so
    /// <see cref="ScannerSnapshotPublisher"/> can be tested without a real
    /// socket.
    /// </summary>
    public sealed class FakeSnapshotSink : ISnapshotSink
    {
        /// <summary>Every message ever enqueued, flattened, in call order.</summary>
        public readonly List<object> Messages = new List<object>();

        /// <summary>Each call to <see cref="Enqueue"/> as its own batch, preserving which messages arrived together.</summary>
        public readonly List<object[]> Batches = new List<object[]>();

        public void Enqueue(params object[] messages)
        {
            Batches.Add(messages);
            Messages.AddRange(messages);
        }
    }
}
