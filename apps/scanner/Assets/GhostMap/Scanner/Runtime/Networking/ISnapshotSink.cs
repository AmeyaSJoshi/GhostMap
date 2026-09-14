namespace GhostMap.Scanner.Networking
{
    /// <summary>
    /// What <see cref="ScannerSnapshotPublisher"/> needs from a network client:
    /// somewhere to hand off wire messages, in order. Exists so the publisher's
    /// revision-watching logic can be tested against a fake sink without a real
    /// socket, and so it never depends on any other member of
    /// <see cref="ScannerNetworkClient"/>.
    /// </summary>
    public interface ISnapshotSink
    {
        /// <summary>
        /// Enqueues one or more messages to be sent, in order. Passing several
        /// in one call keeps them adjacent even under concurrent draining —
        /// see <see cref="ScannerNetworkClient.Enqueue"/>.
        /// </summary>
        void Enqueue(params object[] messages);
    }
}
