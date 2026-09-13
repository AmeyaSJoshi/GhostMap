using System;
using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// Why a corner capture, undo or closure measurement was refused.
    /// <see cref="None"/> means it succeeded.
    /// </summary>
    public enum CornerCaptureRejection
    {
        None = 0,

        /// <summary>There is no GhostMap frame yet. Lock the floor first.</summary>
        FloorNotLocked,

        /// <summary>The session is not tracking, or reports a not-tracking reason.</summary>
        TrackingNotGood,

        /// <summary>The scan phase does not permit this operation.</summary>
        WrongPhase,

        /// <summary>All four MVP corners are already captured.</summary>
        AllCornersCaptured,

        /// <summary>The center-screen ray is parallel to the floor, or aimed away from it.</summary>
        RayMissedFloor,

        /// <summary>A shared validation rule refused the corner. See <see cref="CornerCaptureController.LastError"/>.</summary>
        ValidationFailed,

        /// <summary>Undo was asked for with no corners captured.</summary>
        NoCornersToUndo,

        /// <summary>Closure verification needs all four corners.</summary>
        NotFourCorners
    }

    /// <summary>
    /// Task S3: captures the room's four floor corners against the frame Task
    /// S2 locked, then measures closure error when the user re-aims at the
    /// first physical corner.
    ///
    /// <para><b>A corner is found by arithmetic, not by an AR raycast.</b> The
    /// center-screen camera ray is intersected with the horizontal plane at the
    /// locked frame's <see cref="GhostCoordinateFrame.FloorWorldY"/>. Requiring
    /// a plane hit per corner would make real rooms uncapturable: ARKit's plane
    /// extents lag the room, stop at skirting boards and furniture, and are
    /// routinely absent at exactly the corner the user is aiming at. The floor
    /// plane, by contrast, is already known exactly — that is what locking it
    /// bought.</para>
    ///
    /// <para><b>The frame is read, never written.</b> Nothing here can re-lock
    /// or move it; <see cref="FloorLockController"/> refuses a second lock and
    /// <see cref="GhostCoordinateFrame"/> has no setters. A frame that shifted
    /// mid-capture would leave corners taken before the shift describing a
    /// different room from those taken after, and would turn closure error into
    /// a measurement of the drift rather than of the user's aim.</para>
    ///
    /// <para>Validation is the shared package's, not a scanner copy. Every
    /// candidate runs <see cref="RoomValidator.ValidateNewCorner"/> for the
    /// incremental spacing and self-intersection rules, and then
    /// <see cref="RoomValidator.ValidateRoom"/> on the room the candidate would
    /// produce. The second is not redundant: maximum wall length, footprint
    /// area and interior angles are properties of the chain rather than of one
    /// corner, and the per-corner check cannot see them.</para>
    ///
    /// <para>Plain C# rather than a MonoBehaviour, for the same reason as
    /// <see cref="FloorLockController"/>: every rule below is testable without
    /// a device. <see cref="UI.CornerCaptureHud"/> is the scene-facing wrapper.</para>
    /// </summary>
    public sealed class CornerCaptureController
    {
        /// <summary>Exactly four, per scene schema v1. Sourced from the shared validator.</summary>
        public const int RequiredCornerCount = RoomValidator.RequiredCornerCount;

        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;
        private readonly List<CornerModel> corners = new List<CornerModel>(RequiredCornerCount);

        public CornerCaptureController(ISpatialProvider provider, FloorLockController floorLock)
        {
            this.provider = provider;
            this.floorLock = floorLock;
        }

        /// <summary>The frame Task S2 locked, or null before the floor is locked.</summary>
        public GhostCoordinateFrame Frame => floorLock.Frame;

        /// <summary>Captured corners, in capture order. Order is load bearing: walls derive from it.</summary>
        public IReadOnlyList<CornerModel> Corners => corners;

        public int CornerCount => corners.Count;

        public bool IsComplete => corners.Count == RequiredCornerCount;

        /// <summary>Why the most recent operation was refused.</summary>
        public CornerCaptureRejection LastRejection { get; private set; }

        /// <summary>
        /// The shared validator's message for the most recent
        /// <see cref="CornerCaptureRejection.ValidationFailed"/>, shown to the
        /// user verbatim. Empty when the last operation did not fail a rule.
        /// </summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>Whether a closure measurement has been taken and not since invalidated.</summary>
        public bool HasClosureMeasurement { get; private set; }

        /// <summary>Horizontal distance from the stored first corner to the verification point.</summary>
        public float ClosureErrorM { get; private set; }

        public ClosureQuality LastClosureQuality { get; private set; }

        /// <summary>The Ghost-space point the user re-aimed at during verification.</summary>
        public Vector3 ClosurePointGhost { get; private set; }

        /// <summary>A measurement exists and is not in the reject band.</summary>
        public bool IsClosureAccepted =>
            HasClosureMeasurement && LastClosureQuality != ClosureQuality.Rejected;

        /// <summary>
        /// Whether the capture control should be enabled. Whether the crosshair
        /// actually yields a legal corner is only known once the attempt runs.
        /// </summary>
        public bool CanCapture =>
            Frame != null && provider.IsTrackingGood && corners.Count < RequiredCornerCount;

        public bool CanVerifyClosure =>
            Frame != null && provider.IsTrackingGood && IsComplete;

        // -------------------------------------------------------------------
        // The floor ray
        // -------------------------------------------------------------------

        /// <summary>
        /// Projects the center-screen camera ray onto the locked floor plane
        /// and returns the result in GhostMap coordinates, with Y forced to
        /// exactly zero.
        ///
        /// <para>The intersection already lands within float noise of the floor,
        /// but "within noise" is not what scene schema v1 asks for, and a
        /// residue of 1e-7 would ride along into every derived wall. Forcing it
        /// costs nothing and makes the invariant exact.</para>
        ///
        /// <para>Also used by the HUD each frame to preview where the crosshair
        /// would land, which is why it is public and side-effect free.</para>
        /// </summary>
        public bool TryProjectCrosshairToFloor(out Vector3 ghostPoint)
        {
            ghostPoint = Vector3.zero;

            GhostCoordinateFrame frame = Frame;

            if (frame == null)
            {
                return false;
            }

            Ray worldRay = provider.GetScreenRay(provider.CenterScreenPoint);

            if (!RayPlaneMath.TryIntersectHorizontalPlane(
                    worldRay, frame.FloorWorldY, out Vector3 worldPoint))
            {
                return false;
            }

            Vector3 ghost = frame.WorldToGhost(worldPoint);
            ghostPoint = new Vector3(ghost.x, 0f, ghost.z);
            return true;
        }

        // -------------------------------------------------------------------
        // Capture
        // -------------------------------------------------------------------

        /// <summary>
        /// Captures the corner under the crosshair and appends it.
        /// </summary>
        /// <returns>True when a corner was appended by this call.</returns>
        public bool TryCaptureCorner(out CornerCaptureRejection rejection)
        {
            LastError = string.Empty;

            rejection = EvaluateCapture(out CornerModel corner);
            LastRejection = rejection;

            if (rejection != CornerCaptureRejection.None)
            {
                return false;
            }

            corners.Add(corner);
            return true;
        }

        private CornerCaptureRejection EvaluateCapture(out CornerModel corner)
        {
            corner = null;

            if (Frame == null)
            {
                return CornerCaptureRejection.FloorNotLocked;
            }

            if (!provider.IsTrackingGood)
            {
                return CornerCaptureRejection.TrackingNotGood;
            }

            if (corners.Count >= RequiredCornerCount)
            {
                return CornerCaptureRejection.AllCornersCaptured;
            }

            if (!TryProjectCrosshairToFloor(out Vector3 ghost))
            {
                return CornerCaptureRejection.RayMissedFloor;
            }

            // Incremental rules: spacing from the previous corner, separation
            // from non-neighbors, and self-intersection once this corner closes
            // the polygon.
            ValidationResult perCorner = RoomValidator.ValidateNewCorner(corners, ghost);

            if (!perCorner.IsValid)
            {
                LastError = perCorner.Error;
                return CornerCaptureRejection.ValidationFailed;
            }

            var candidate = new CornerModel
            {
                id = Guid.NewGuid().ToString(),
                position = Vec3Dto.FromVector3(ghost)
            };

            // Whole-chain rules the per-corner check cannot see: maximum wall
            // length on the partial chain, and footprint area plus interior
            // angles once all four exist.
            ValidationResult wholeRoom = RoomValidator.ValidateRoom(BuildCandidateRoom(candidate));

            if (!wholeRoom.IsValid)
            {
                LastError = wholeRoom.Error;
                return CornerCaptureRejection.ValidationFailed;
            }

            corner = candidate;
            return CornerCaptureRejection.None;
        }

        /// <summary>
        /// Removes the most recent corner. Any closure measurement is discarded
        /// with it: it was measured against a room that no longer exists.
        /// </summary>
        public bool TryUndoLastCorner(out CornerCaptureRejection rejection)
        {
            LastError = string.Empty;

            if (corners.Count == 0)
            {
                rejection = CornerCaptureRejection.NoCornersToUndo;
                LastRejection = rejection;
                return false;
            }

            corners.RemoveAt(corners.Count - 1);
            ForgetClosure();

            rejection = CornerCaptureRejection.None;
            LastRejection = rejection;
            return true;
        }

        /// <summary>
        /// Discards every corner and any closure measurement, so the user can
        /// rescan the room. The locked frame is untouched — the room is being
        /// re-measured, not re-anchored.
        /// </summary>
        public void ClearCorners()
        {
            corners.Clear();
            ForgetClosure();

            LastError = string.Empty;
            LastRejection = CornerCaptureRejection.None;
        }

        // -------------------------------------------------------------------
        // Closure verification
        // -------------------------------------------------------------------

        /// <summary>
        /// Measures closure error: the user re-aims at the first physical
        /// corner, and the same floor-ray arithmetic produces a second Ghost
        /// point for it. The horizontal distance between that and the stored
        /// first corner is the accumulated error of the whole scan.
        ///
        /// <para>A measurement in the reject band is still a successful
        /// measurement — the number is exactly what the user needs to see — so
        /// this returns true and reports the band in
        /// <paramref name="quality"/>. Whether the scan may continue is
        /// <see cref="IsClosureAccepted"/>, and acting on it is the workflow's
        /// decision, not this method's.</para>
        /// </summary>
        public bool TryMeasureClosure(
            out ClosureQuality quality,
            out CornerCaptureRejection rejection)
        {
            quality = ClosureQuality.Rejected;
            LastError = string.Empty;

            if (Frame == null)
            {
                rejection = CornerCaptureRejection.FloorNotLocked;
                LastRejection = rejection;
                return false;
            }

            if (!IsComplete)
            {
                rejection = CornerCaptureRejection.NotFourCorners;
                LastRejection = rejection;
                return false;
            }

            if (!provider.IsTrackingGood)
            {
                rejection = CornerCaptureRejection.TrackingNotGood;
                LastRejection = rejection;
                return false;
            }

            if (!TryProjectCrosshairToFloor(out Vector3 verification))
            {
                rejection = CornerCaptureRejection.RayMissedFloor;
                LastRejection = rejection;
                return false;
            }

            Vector3 first = corners[0].position.ToVector3();

            float error = Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(verification.x, verification.z));

            ClosurePointGhost = verification;
            ClosureErrorM = error;
            LastClosureQuality = RoomValidator.ClassifyClosure(error);
            HasClosureMeasurement = true;

            quality = LastClosureQuality;
            rejection = CornerCaptureRejection.None;
            LastRejection = rejection;
            return true;
        }

        // -------------------------------------------------------------------
        // Snapshot support
        // -------------------------------------------------------------------

        /// <summary>
        /// Corners as a fresh array of fresh <see cref="CornerModel"/> values.
        ///
        /// <para>A published snapshot is a value, not a window onto live state.
        /// Handing out the live objects would let a later undo retroactively
        /// rewrite a revision the viewer had already accepted.</para>
        /// </summary>
        public CornerModel[] CopyCorners()
        {
            var copy = new CornerModel[corners.Count];

            for (int i = 0; i < corners.Count; i++)
            {
                copy[i] = new CornerModel
                {
                    id = corners[i].id,
                    position = corners[i].position
                };
            }

            return copy;
        }

        private RoomModel BuildCandidateRoom(CornerModel appended)
        {
            var all = new CornerModel[corners.Count + 1];

            for (int i = 0; i < corners.Count; i++)
            {
                all[i] = corners[i];
            }

            all[corners.Count] = appended;

            return new RoomModel
            {
                id = "candidate",
                name = "Room",
                heightM = 0f,
                corners = all,
                openings = Array.Empty<OpeningModel>(),
                objects = Array.Empty<SceneObjectModel>()
            };
        }

        private void ForgetClosure()
        {
            HasClosureMeasurement = false;
            ClosureErrorM = 0f;
            LastClosureQuality = ClosureQuality.Excellent;
            ClosurePointGhost = Vector3.zero;
        }
    }
}
