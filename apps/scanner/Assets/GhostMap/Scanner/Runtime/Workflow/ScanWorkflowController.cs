using System;
using System.Collections.Generic;
using GhostMap.Scanner.Capture;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Protocol;

namespace GhostMap.Scanner.Workflow
{
    /// <summary>
    /// Owns the scan phase, the session identity, the revision counter and the
    /// current <see cref="SceneSnapshot"/>. It is the only thing permitted to
    /// change <see cref="Phase"/>.
    ///
    /// <para>Task S2 drives this as far as <see cref="ScanPhase.FloorLocked"/>.
    /// The full transition table from implementation plan section 10 is encoded
    /// here anyway, so that later tasks extend the workflow rather than
    /// re-deciding what a legal transition is, and so an illegal one fails
    /// immediately instead of producing a snapshot with an impossible
    /// <c>scanPhase</c>.</para>
    ///
    /// <para>Plain C#, not a MonoBehaviour, for the same reason as
    /// <see cref="FloorLockController"/>: every rule stays testable off-device.</para>
    /// </summary>
    public sealed class ScanWorkflowController
    {
        /// <summary>
        /// Legal transitions, forward and back, exactly as the implementation
        /// plan lists them. Anything absent here is a bug, not a shortcut.
        /// </summary>
        private static readonly Dictionary<ScanPhase, ScanPhase[]> AllowedTransitions =
            new Dictionary<ScanPhase, ScanPhase[]>
            {
                { ScanPhase.Boot, new[] { ScanPhase.WaitingForTracking } },
                { ScanPhase.WaitingForTracking, new[] { ScanPhase.FindFloor } },
                { ScanPhase.FindFloor, new[] { ScanPhase.FloorLocked } },
                { ScanPhase.FloorLocked, new[] { ScanPhase.CaptureCorners } },
                { ScanPhase.CaptureCorners, new[] { ScanPhase.VerifyClosure } },
                { ScanPhase.VerifyClosure, new[] { ScanPhase.CaptureHeight, ScanPhase.CaptureCorners } },
                { ScanPhase.CaptureHeight, new[] { ScanPhase.AddOpenings, ScanPhase.CaptureCorners } },
                { ScanPhase.AddOpenings, new[] { ScanPhase.AddObjects, ScanPhase.CaptureHeight } },
                { ScanPhase.AddObjects, new[] { ScanPhase.ReadyToFinalize, ScanPhase.AddOpenings } },
                { ScanPhase.ReadyToFinalize, new[] { ScanPhase.Finalized } },
                { ScanPhase.Finalized, Array.Empty<ScanPhase>() }
            };

        private readonly FloorLockController floorLock;

        public ScanWorkflowController(FloorLockController floorLock)
        {
            this.floorLock = floorLock;
            SessionId = Guid.NewGuid().ToString();
            RoomId = Guid.NewGuid().ToString();
            Phase = ScanPhase.Boot;
            Revision = 0;
            Snapshot = BuildSnapshot();
        }

        public ScanPhase Phase { get; private set; }

        public string SessionId { get; }

        public string RoomId { get; }

        /// <summary>Monotonic within the session. Incremented by every structural mutation.</summary>
        public int Revision { get; private set; }

        /// <summary>The scene state at <see cref="Revision"/>. Rebuilt, never mutated in place.</summary>
        public SceneSnapshot Snapshot { get; private set; }

        /// <summary>The locked GhostMap frame, or null before floor lock.</summary>
        public GhostCoordinateFrame Frame => floorLock.Frame;

        /// <summary>
        /// Advances the pre-floor-lock phases from tracking quality. Boot
        /// becomes WaitingForTracking immediately; WaitingForTracking becomes
        /// FindFloor as soon as the session tracks well.
        ///
        /// Degraded tracking after that does not walk the phase backwards —
        /// the plan defines no such transition. Capture is gated by
        /// <see cref="FloorLockController.CanLock"/> instead.
        /// </summary>
        public void Tick(bool isTrackingGood)
        {
            if (Phase == ScanPhase.Boot)
            {
                TransitionTo(ScanPhase.WaitingForTracking);
            }

            if (Phase == ScanPhase.WaitingForTracking && isTrackingGood)
            {
                TransitionTo(ScanPhase.FindFloor);
            }
        }

        /// <summary>
        /// Attempts the floor lock. On success the GhostMap frame exists, the
        /// phase becomes <see cref="ScanPhase.FloorLocked"/>, the revision
        /// increments, and the snapshot carries an empty room with height 0.
        /// </summary>
        public bool TryLockFloor(out FloorLockRejection rejection)
        {
            rejection = FloorLockRejection.None;

            if (Phase != ScanPhase.FindFloor)
            {
                rejection = floorLock.IsLocked
                    ? FloorLockRejection.AlreadyLocked
                    : FloorLockRejection.TrackingNotGood;
                return false;
            }

            if (!floorLock.TryLockFloor(out rejection))
            {
                return false;
            }

            TransitionTo(ScanPhase.FloorLocked);
            Revision++;
            Snapshot = BuildSnapshot();
            return true;
        }

        /// <summary>
        /// Moves to <paramref name="next"/>, or throws when the plan does not
        /// allow that transition.
        /// </summary>
        public void TransitionTo(ScanPhase next)
        {
            if (!CanTransitionTo(next))
            {
                throw new InvalidOperationException(
                    $"Illegal scan phase transition {Phase} -> {next}.");
            }

            Phase = next;
        }

        public bool CanTransitionTo(ScanPhase next)
        {
            return Array.IndexOf(AllowedTransitions[Phase], next) >= 0;
        }

        /// <summary>
        /// Builds the snapshot for the current phase and revision. Collections
        /// are zero-length rather than null, which scene schema v1 requires on
        /// the wire.
        /// </summary>
        private SceneSnapshot BuildSnapshot()
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = SessionId,
                revision = Revision,
                scanPhase = Phase.ToString(),
                finalized = false,
                closureErrorM = 0f,
                room = new RoomModel
                {
                    id = RoomId,
                    name = "Room",
                    heightM = 0f,
                    corners = Array.Empty<CornerModel>(),
                    openings = Array.Empty<OpeningModel>(),
                    objects = Array.Empty<SceneObjectModel>()
                }
            };
        }
    }
}
