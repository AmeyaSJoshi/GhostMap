using System;
using System.Collections.Generic;
using GhostMap.Scanner.Capture;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Protocol;
using GhostMap.Shared.Validation;

namespace GhostMap.Scanner.Workflow
{
    /// <summary>
    /// Why finalization was refused. <see cref="None"/> means it succeeded.
    /// </summary>
    public enum FinalizeRejection
    {
        None = 0,

        /// <summary>The scan phase does not permit finalization.</summary>
        WrongPhase
    }

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
        private readonly CornerCaptureController corners;
        private readonly HeightCaptureController height;
        private readonly OpeningCaptureController openingCapture;
        private readonly ObjectPlacementController objectPlacement;

        private bool isFinalized;

        public ScanWorkflowController(
            FloorLockController floorLock,
            CornerCaptureController corners,
            HeightCaptureController height,
            OpeningCaptureController openingCapture,
            ObjectPlacementController objectPlacement)
        {
            this.floorLock = floorLock;
            this.corners = corners;
            this.height = height;
            this.openingCapture = openingCapture;
            this.objectPlacement = objectPlacement;
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

        /// <summary>Task S3 corner capture and closure. Read-only from outside the workflow.</summary>
        public CornerCaptureController Corners => corners;

        /// <summary>Task S4 height capture. Read-only from outside the workflow.</summary>
        public HeightCaptureController Height => height;

        /// <summary>Task S5 opening capture. Read-only from outside the workflow.</summary>
        public OpeningCaptureController Openings => openingCapture;

        /// <summary>Task S5 object placement. Read-only from outside the workflow.</summary>
        public ObjectPlacementController Objects => objectPlacement;

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
            Publish();
            return true;
        }

        // -------------------------------------------------------------------
        // Task S3 — corner capture and closure verification
        // -------------------------------------------------------------------

        /// <summary>
        /// Leaves <see cref="ScanPhase.FloorLocked"/> for
        /// <see cref="ScanPhase.CaptureCorners"/>.
        ///
        /// <para>Explicit rather than automatic on lock, so that FloorLocked is
        /// a state the user actually sees and the S2 device readout stays
        /// observable.</para>
        /// </summary>
        public bool BeginCornerCapture()
        {
            if (Phase != ScanPhase.FloorLocked)
            {
                return false;
            }

            TransitionTo(ScanPhase.CaptureCorners);
            Publish();
            return true;
        }

        /// <summary>
        /// Captures the corner under the crosshair. The fourth accepted corner
        /// closes the footprint and moves the scan into closure verification.
        /// </summary>
        public bool TryCaptureCorner(out CornerCaptureRejection rejection)
        {
            if (Phase != ScanPhase.CaptureCorners)
            {
                rejection = CornerCaptureRejection.WrongPhase;
                return false;
            }

            if (!corners.TryCaptureCorner(out rejection))
            {
                return false;
            }

            if (corners.IsComplete)
            {
                TransitionTo(ScanPhase.VerifyClosure);
            }

            Publish();
            return true;
        }

        /// <summary>
        /// Removes the most recent corner. Undoing from closure verification
        /// steps the scan back to corner capture, because the footprint the
        /// closure was measured against no longer exists.
        /// </summary>
        public bool TryUndoCorner(out CornerCaptureRejection rejection)
        {
            if (Phase != ScanPhase.CaptureCorners && Phase != ScanPhase.VerifyClosure)
            {
                rejection = CornerCaptureRejection.WrongPhase;
                return false;
            }

            if (!corners.TryUndoLastCorner(out rejection))
            {
                return false;
            }

            if (Phase == ScanPhase.VerifyClosure)
            {
                TransitionTo(ScanPhase.CaptureCorners);
            }

            Publish();
            return true;
        }

        /// <summary>
        /// Measures closure against the first corner and publishes the result.
        ///
        /// <para>A measurement in the reject band is still published — the user
        /// needs the exact number — but the scan does not proceed. The phase
        /// stays at <see cref="ScanPhase.VerifyClosure"/> so the only way
        /// forward is <see cref="RedoCorners"/>.</para>
        /// </summary>
        public bool TryVerifyClosure(
            out ClosureQuality quality,
            out CornerCaptureRejection rejection)
        {
            quality = ClosureQuality.Rejected;

            if (Phase != ScanPhase.VerifyClosure)
            {
                rejection = CornerCaptureRejection.WrongPhase;
                return false;
            }

            if (!corners.TryMeasureClosure(out quality, out rejection))
            {
                return false;
            }

            if (corners.IsClosureAccepted)
            {
                TransitionTo(ScanPhase.CaptureHeight);
            }

            Publish();
            return true;
        }

        /// <summary>
        /// Discards the footprint and returns to corner capture. The locked
        /// frame is deliberately left alone: a rescan re-measures the room, it
        /// does not re-anchor it.
        ///
        /// <para>Any in-progress wall selection is cleared with the corners:
        /// the new footprint will derive different walls, and a stale index
        /// must not silently resolve against one of them.</para>
        /// </summary>
        public bool RedoCorners()
        {
            if (Phase != ScanPhase.VerifyClosure && Phase != ScanPhase.CaptureHeight)
            {
                return false;
            }

            corners.ClearCorners();
            height.ResetWallSelection();
            TransitionTo(ScanPhase.CaptureCorners);
            Publish();
            return true;
        }

        // -------------------------------------------------------------------
        // Task S4 — height capture
        // -------------------------------------------------------------------

        /// <summary>Selects a wall, by index into <see cref="HeightCaptureController.Walls"/>, to aim height capture at.</summary>
        public bool SelectHeightWall(int index)
        {
            if (Phase != ScanPhase.CaptureHeight)
            {
                return false;
            }

            return height.SelectWall(index);
        }

        /// <summary>
        /// Captures the room height under the crosshair against the selected
        /// wall. A valid, confirmed height moves the scan to
        /// <see cref="ScanPhase.AddOpenings"/>; an invalid one leaves the
        /// phase and the previously captured height untouched.
        /// </summary>
        public bool TryCaptureHeight(out HeightCaptureRejection rejection)
        {
            if (Phase != ScanPhase.CaptureHeight)
            {
                rejection = HeightCaptureRejection.WrongPhase;
                return false;
            }

            if (!height.TryCaptureHeight(out rejection))
            {
                return false;
            }

            TransitionTo(ScanPhase.AddOpenings);
            Publish();
            return true;
        }

        /// <summary>
        /// The manual fallback: a typed height, validated the same way as an
        /// automatic capture. A failed automatic capture must never block the
        /// scan (implementation plan section 8.7), so this is always available
        /// in <see cref="ScanPhase.CaptureHeight"/>, independent of wall
        /// selection or aim.
        /// </summary>
        public bool TrySetManualHeight(float heightM, out HeightCaptureRejection rejection)
        {
            if (Phase != ScanPhase.CaptureHeight)
            {
                rejection = HeightCaptureRejection.WrongPhase;
                return false;
            }

            if (!height.TrySetManualHeight(heightM, out rejection))
            {
                return false;
            }

            TransitionTo(ScanPhase.AddOpenings);
            Publish();
            return true;
        }

        // -------------------------------------------------------------------
        // Task S5 Part 1 — openings
        // -------------------------------------------------------------------

        /// <summary>Selects a wall, by index into <see cref="OpeningCaptureController.Walls"/>, to place an opening on.</summary>
        public bool SelectOpeningWall(int index)
        {
            if (Phase != ScanPhase.AddOpenings)
            {
                return false;
            }

            return openingCapture.SelectWall(index);
        }

        /// <summary>Chooses door or window for the next opening capture.</summary>
        public bool SetOpeningType(string type)
        {
            if (Phase != ScanPhase.AddOpenings)
            {
                return false;
            }

            return openingCapture.SetType(type);
        }

        /// <summary>
        /// Captures the lower-left point of the opening under the crosshair.
        /// Not a structural mutation by itself — nothing is appended, so the
        /// snapshot is not republished — until <see cref="TryCaptureOpeningEndPoint"/>
        /// commits it.
        /// </summary>
        public bool TryCaptureOpeningStartPoint(out OpeningCaptureRejection rejection)
        {
            if (Phase != ScanPhase.AddOpenings)
            {
                rejection = OpeningCaptureRejection.WrongPhase;
                return false;
            }

            return openingCapture.TryCaptureStartPoint(out rejection);
        }

        /// <summary>
        /// Captures the upper-right point and, on a valid opening, appends it
        /// and republishes the snapshot.
        /// </summary>
        public bool TryCaptureOpeningEndPoint(out OpeningCaptureRejection rejection)
        {
            if (Phase != ScanPhase.AddOpenings)
            {
                rejection = OpeningCaptureRejection.WrongPhase;
                return false;
            }

            if (!openingCapture.TryCaptureEndPointAndCommit(out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>Removes the most recently captured opening and republishes the snapshot.</summary>
        public bool TryUndoLastOpening(out OpeningCaptureRejection rejection)
        {
            if (Phase != ScanPhase.AddOpenings)
            {
                rejection = OpeningCaptureRejection.WrongPhase;
                return false;
            }

            if (!openingCapture.TryUndoLastOpening(out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>
        /// Leaves <see cref="ScanPhase.AddOpenings"/> for
        /// <see cref="ScanPhase.AddObjects"/>. Legal with zero openings
        /// captured — implementation plan section 16 allows continuing
        /// without any.
        /// </summary>
        public bool FinishAddingOpenings()
        {
            if (Phase != ScanPhase.AddOpenings)
            {
                return false;
            }

            TransitionTo(ScanPhase.AddObjects);
            Publish();
            return true;
        }

        // -------------------------------------------------------------------
        // Task S5 Part 2 — furniture / objects
        // -------------------------------------------------------------------

        /// <summary>Chooses the furniture type the next placement will use.</summary>
        public bool SetObjectType(string type)
        {
            if (Phase != ScanPhase.AddObjects)
            {
                return false;
            }

            return objectPlacement.SetType(type);
        }

        /// <summary>Places an object under the crosshair and republishes the snapshot.</summary>
        public bool TryPlaceObject(out ObjectPlacementRejection rejection)
        {
            if (Phase != ScanPhase.AddObjects)
            {
                rejection = ObjectPlacementRejection.WrongPhase;
                return false;
            }

            if (!objectPlacement.TryPlaceObject(out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>Removes the most recently placed object and republishes the snapshot.</summary>
        public bool TryUndoLastObject(out ObjectPlacementRejection rejection)
        {
            if (Phase != ScanPhase.AddObjects)
            {
                rejection = ObjectPlacementRejection.WrongPhase;
                return false;
            }

            if (!objectPlacement.TryUndoLastObject(out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>Adjusts a placed object's width and republishes the snapshot on success.</summary>
        public bool TrySetObjectWidth(int index, float widthM, out ObjectPlacementRejection rejection)
        {
            if (Phase != ScanPhase.AddObjects)
            {
                rejection = ObjectPlacementRejection.WrongPhase;
                return false;
            }

            if (!objectPlacement.TrySetWidth(index, widthM, out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>Adjusts a placed object's depth and republishes the snapshot on success.</summary>
        public bool TrySetObjectDepth(int index, float depthM, out ObjectPlacementRejection rejection)
        {
            if (Phase != ScanPhase.AddObjects)
            {
                rejection = ObjectPlacementRejection.WrongPhase;
                return false;
            }

            if (!objectPlacement.TrySetDepth(index, depthM, out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>Adjusts a placed object's height and republishes the snapshot on success.</summary>
        public bool TrySetObjectHeight(int index, float heightM, out ObjectPlacementRejection rejection)
        {
            if (Phase != ScanPhase.AddObjects)
            {
                rejection = ObjectPlacementRejection.WrongPhase;
                return false;
            }

            if (!objectPlacement.TrySetHeight(index, heightM, out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>Adjusts a placed object's yaw and republishes the snapshot on success.</summary>
        public bool TrySetObjectYaw(int index, float yawDeg, out ObjectPlacementRejection rejection)
        {
            if (Phase != ScanPhase.AddObjects)
            {
                rejection = ObjectPlacementRejection.WrongPhase;
                return false;
            }

            if (!objectPlacement.TrySetYaw(index, yawDeg, out rejection))
            {
                return false;
            }

            Publish();
            return true;
        }

        /// <summary>
        /// Leaves <see cref="ScanPhase.AddObjects"/> for
        /// <see cref="ScanPhase.ReadyToFinalize"/>. Legal with zero objects
        /// placed — implementation plan section 16 allows continuing without
        /// furniture. Task S6 owns finalization itself.
        /// </summary>
        public bool FinishAddingObjects()
        {
            if (Phase != ScanPhase.AddObjects)
            {
                return false;
            }

            TransitionTo(ScanPhase.ReadyToFinalize);
            Publish();
            return true;
        }

        // -------------------------------------------------------------------
        // Task S6 — finalization
        // -------------------------------------------------------------------

        /// <summary>
        /// Hands scan authority from the scanner to the viewer
        /// (<c>docs/decisions/ADR-0003-scanner-authority.md</c>). Moves
        /// <see cref="ScanPhase.ReadyToFinalize"/> to
        /// <see cref="ScanPhase.Finalized"/>, sets
        /// <see cref="SceneSnapshot.finalized"/> on every snapshot from here on,
        /// and republishes once more so the final snapshot carries both.
        ///
        /// <para>Every scan-mutating method on this class already gates on a
        /// specific phase (<c>AddObjects</c>, <c>AddOpenings</c>, ...), none of
        /// which is <see cref="ScanPhase.Finalized"/>, so no additional guard is
        /// needed to reject structural mutations once finalized — they are
        /// already unreachable.</para>
        /// </summary>
        public bool TryFinalize(out FinalizeRejection rejection)
        {
            if (Phase != ScanPhase.ReadyToFinalize)
            {
                rejection = FinalizeRejection.WrongPhase;
                return false;
            }

            isFinalized = true;
            TransitionTo(ScanPhase.Finalized);
            Publish();

            rejection = FinalizeRejection.None;
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
        /// Advances the revision and republishes the snapshot.
        ///
        /// <para>The revision is monotonic within the session, never wound
        /// back. The viewer ignores any snapshot whose revision is not greater
        /// than the one it already holds, so an undo that decremented the
        /// counter would publish a room the viewer was contractually obliged to
        /// drop. Undo is a forward mutation like any other.</para>
        /// </summary>
        private void Publish()
        {
            Revision++;
            Snapshot = BuildSnapshot();
        }

        /// <summary>
        /// Builds the snapshot for the current phase and revision. Collections
        /// are zero-length rather than null, which scene schema v1 requires on
        /// the wire.
        ///
        /// <para>Walls are absent by construction: schema v1 has no walls
        /// field, and they are derived from consecutive corners on demand.</para>
        /// </summary>
        private SceneSnapshot BuildSnapshot()
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = SessionId,
                revision = Revision,
                scanPhase = Phase.ToString(),
                finalized = isFinalized,
                closureErrorM = corners.HasClosureMeasurement ? corners.ClosureErrorM : 0f,
                room = new RoomModel
                {
                    id = RoomId,
                    name = "Room",
                    heightM = height.HasCapturedHeight ? height.HeightM : 0f,
                    corners = corners.CopyCorners(),
                    openings = openingCapture.CopyOpenings(),
                    objects = objectPlacement.CopyObjects()
                }
            };
        }
    }
}
