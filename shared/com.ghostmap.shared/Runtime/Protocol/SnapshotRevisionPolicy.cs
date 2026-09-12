using GhostMap.Shared.Domain;

namespace GhostMap.Shared.Protocol
{
    /// <summary>The outcome of evaluating an incoming snapshot.</summary>
    public enum SnapshotAcceptance
    {
        /// <summary>A different session; accept its first snapshot.</summary>
        AcceptNewSession,

        /// <summary>Same session, higher revision; apply it.</summary>
        AcceptNewerRevision,

        /// <summary>Same session and revision; a harmless resend. Ignore.</summary>
        IgnoreDuplicateRevision,

        /// <summary>Same session, lower revision; out of order. Ignore.</summary>
        IgnoreStaleRevision
    }

    /// <summary>
    /// Revision arbitration for received snapshots.
    ///
    /// This lives in the shared package rather than inside the viewer so that
    /// the rule is defined and tested exactly once. Reconnection resends the
    /// current snapshot, so duplicates are expected and must be harmless.
    /// </summary>
    public static class SnapshotRevisionPolicy
    {
        public static SnapshotAcceptance Evaluate(SceneSnapshot current, SceneSnapshot incoming)
        {
            if (current == null)
            {
                return SnapshotAcceptance.AcceptNewSession;
            }

            if (incoming == null)
            {
                return SnapshotAcceptance.IgnoreStaleRevision;
            }

            if (current.sessionId != incoming.sessionId)
            {
                return SnapshotAcceptance.AcceptNewSession;
            }

            if (incoming.revision > current.revision)
            {
                return SnapshotAcceptance.AcceptNewerRevision;
            }

            if (incoming.revision == current.revision)
            {
                return SnapshotAcceptance.IgnoreDuplicateRevision;
            }

            return SnapshotAcceptance.IgnoreStaleRevision;
        }

        public static bool ShouldApply(SnapshotAcceptance acceptance)
            => acceptance == SnapshotAcceptance.AcceptNewSession
            || acceptance == SnapshotAcceptance.AcceptNewerRevision;
    }
}
