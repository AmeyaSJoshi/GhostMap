using System;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;

namespace GhostMap.Viewer.Scene
{
    /// <summary>
    /// Task V1's authoritative viewer-side scene state: the single place that
    /// decides whether an incoming <see cref="SceneSnapshot"/> — from the
    /// scanner over the wire, or from a developer-loaded fixture — replaces
    /// what is currently displayed.
    ///
    /// <para><b>Revision arbitration is the shared package's, not a viewer
    /// reimplementation</b> (<c>AGENTS.md</c> rule 3): every accept/ignore
    /// decision runs through <see cref="SnapshotRevisionPolicy"/>.</para>
    ///
    /// <para><b>Validation runs before apply.</b> A snapshot that deserializes
    /// correctly is only well-formed, not necessarily a legal room (protocol
    /// v1 section 4). <see cref="RoomValidator.ValidateRoom"/> plus
    /// <see cref="OpeningValidator.Validate"/> per opening and
    /// <see cref="FurnitureValidator.Validate"/> per object must all pass
    /// before <see cref="Current"/> changes.</para>
    ///
    /// <para><b>Disconnects never clear the scene.</b> This type has no path
    /// that erases <see cref="Current"/> other than accepting a newer, valid
    /// snapshot. A network drop is entirely the network layer's concern.</para>
    /// </summary>
    public sealed class ViewerSceneStore : IViewerSceneSource
    {
        public SceneSnapshot Current { get; private set; }

        public event Action<SceneSnapshot> Changed;

        /// <summary>
        /// Attempts to apply an incoming snapshot. Returns false, leaving
        /// <see cref="Current"/> untouched, for a stale/duplicate revision or
        /// an invalid room — both are ordinary, expected events, not errors
        /// that should be surfaced to the user as failures.
        /// </summary>
        public bool TryApplyScannerSnapshot(SceneSnapshot snapshot, out string error)
        {
            if (snapshot == null)
            {
                error = "Snapshot is null.";
                return false;
            }

            SnapshotAcceptance acceptance = SnapshotRevisionPolicy.Evaluate(Current, snapshot);

            if (!SnapshotRevisionPolicy.ShouldApply(acceptance))
            {
                error = acceptance == SnapshotAcceptance.IgnoreDuplicateRevision
                    ? $"Duplicate revision {snapshot.revision} for session '{snapshot.sessionId}'; ignored."
                    : $"Stale revision {snapshot.revision} for session '{snapshot.sessionId}'; ignored.";
                return false;
            }

            if (!SceneSnapshotValidator.TryValidate(snapshot, out string validationError))
            {
                error = validationError;
                return false;
            }

            Current = snapshot;
            error = string.Empty;
            Changed?.Invoke(Current);
            return true;
        }
    }
}
