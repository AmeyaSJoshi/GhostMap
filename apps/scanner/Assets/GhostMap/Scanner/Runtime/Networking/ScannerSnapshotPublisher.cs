using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;

namespace GhostMap.Scanner.Networking
{
    /// <summary>
    /// Watches <see cref="ScanWorkflowController"/> for the two events protocol
    /// v1 must react to — a new revision, and finalization — and turns each
    /// into the wire message(s) <c>docs/contracts/protocol-v1.md</c> requires,
    /// handed to an <see cref="ISnapshotSink"/> (in production,
    /// <see cref="ScannerNetworkClient"/>).
    ///
    /// <para><b>Poll-based, like every other HUD in this codebase.</b>
    /// <see cref="ScanWorkflowController"/> raises no change event, so
    /// <see cref="Tick"/> is meant to be called once per frame from the main
    /// thread and compares against what it last saw — the same pattern
    /// <c>CornerCaptureHud</c> and its siblings already use for
    /// <c>workflow.Phase</c> and <c>workflow.Revision</c>.</para>
    ///
    /// <para><b>Why finalization is not just "another revision change."</b>
    /// <see cref="ScanWorkflowController.TryFinalize"/> increments the revision
    /// like any other mutation, so a plain revision-changed check already
    /// queues the final <c>scene.snapshot</c>. But protocol v1 section 3.5
    /// requires the scanner to also send <c>scan.finalized</c>, and to send it
    /// only once. Both messages are enqueued together in the same
    /// <see cref="ISnapshotSink.Enqueue"/> call precisely when both conditions
    /// are true in the same tick, so nothing draining the sink concurrently can
    /// ever observe the finalized message arrive before the snapshot that
    /// preceded it.</para>
    /// </summary>
    public sealed class ScannerSnapshotPublisher
    {
        private readonly ISnapshotSink sink;

        private ScanWorkflowController workflow;
        private int lastSeenRevision;
        private bool hasSentFinalized;

        public ScannerSnapshotPublisher(ISnapshotSink sink)
        {
            this.sink = sink;
        }

        /// <summary>The workflow this publisher is currently watching, or null before the first <see cref="Rebind"/>.</summary>
        public ScanWorkflowController BoundWorkflow => workflow;

        /// <summary>
        /// Starts watching a (possibly new) workflow. Used both for the initial
        /// binding and after Task S6's Reset replaces the whole controller
        /// graph with a fresh session: resetting <see cref="lastSeenRevision"/>
        /// to an impossible value forces the very next <see cref="Tick"/> to
        /// publish the new session's snapshot (revision 0), exactly as any
        /// other structural mutation would be.
        /// </summary>
        public void Rebind(ScanWorkflowController newWorkflow)
        {
            workflow = newWorkflow;
            lastSeenRevision = -1;
            hasSentFinalized = false;
        }

        /// <summary>
        /// Call once per frame. Enqueues nothing when neither the revision nor
        /// the finalized state has changed since the last call.
        /// </summary>
        public void Tick()
        {
            if (workflow == null)
            {
                return;
            }

            bool revisionChanged = workflow.Revision != lastSeenRevision;
            bool justFinalized = workflow.Phase == ScanPhase.Finalized && !hasSentFinalized;

            if (!revisionChanged && !justFinalized)
            {
                return;
            }

            var messages = new System.Collections.Generic.List<object>(2);

            if (revisionChanged)
            {
                lastSeenRevision = workflow.Revision;

                SceneSnapshot snapshot = workflow.Snapshot;
                messages.Add(new SceneSnapshotMessage
                {
                    sessionId = snapshot.sessionId,
                    snapshot = snapshot
                });
            }

            if (justFinalized)
            {
                hasSentFinalized = true;
                messages.Add(new ScanFinalizedMessage
                {
                    sessionId = workflow.Snapshot.sessionId,
                    finalRevision = workflow.Revision
                });
            }

            sink.Enqueue(messages.ToArray());
        }
    }
}
