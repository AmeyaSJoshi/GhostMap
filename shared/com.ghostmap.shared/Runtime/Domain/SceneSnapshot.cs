using System;

namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// The entire scene state at one revision. This is the unit of
    /// synchronization: protocol v1 sends a complete snapshot after every
    /// structural mutation rather than replaying deltas, which makes transfer
    /// idempotent and reconnection trivial.
    ///
    /// Rules:
    /// <list type="bullet">
    /// <item><c>schemaVersion</c> is 1;</item>
    /// <item><c>revision</c> increments for every structural mutation;</item>
    /// <item>the viewer ignores a snapshot whose revision is lower than the
    /// latest accepted revision for the same session, and treats an equal
    /// revision as a harmless duplicate;</item>
    /// <item>a finalized snapshot cannot be mutated by scanner-side UI.</item>
    /// </list>
    ///
    /// Authority moves exactly once per session: the scanner owns this state
    /// during scanning, and the viewer owns it after finalization.
    /// </summary>
    [Serializable]
    public sealed class SceneSnapshot
    {
        /// <summary>Always 1 for schema v1.</summary>
        public int schemaVersion;

        public string sessionId;

        /// <summary>Monotonic within a session. Increments on every structural mutation.</summary>
        public int revision;

        /// <summary>The scanner's <c>ScanPhase</c> at the time of capture, as a string.</summary>
        public string scanPhase;

        public bool finalized;

        /// <summary>Corner-closure error in meters, measured during VerifyClosure.</summary>
        public float closureErrorM;

        public RoomModel room;
    }
}
