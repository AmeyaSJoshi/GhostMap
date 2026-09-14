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
    /// Why an opening wall selection, point capture or undo was refused.
    /// <see cref="None"/> means it succeeded.
    /// </summary>
    public enum OpeningCaptureRejection
    {
        None = 0,

        /// <summary>There is no GhostMap frame yet. Lock the floor first.</summary>
        FloorNotLocked,

        /// <summary>The session is not tracking, or reports a not-tracking reason.</summary>
        TrackingNotGood,

        /// <summary>The scan phase does not permit this operation.</summary>
        WrongPhase,

        /// <summary>All four corners must exist before a wall can be derived.</summary>
        NotAllCornersCaptured,

        /// <summary>No wall has been selected yet.</summary>
        NoWallSelected,

        /// <summary>The center-screen ray is parallel to the selected wall's plane.</summary>
        RayParallelToWall,

        /// <summary>The selected wall's plane lies behind the camera along the ray.</summary>
        RayBehindCamera,

        /// <summary>The end point was captured before a start point existed.</summary>
        NoStartPointCaptured,

        /// <summary>The shared <see cref="OpeningValidator"/> refused the candidate opening.</summary>
        ValidationFailed,

        /// <summary>Undo was asked for with no opening captured.</summary>
        NoOpeningsToUndo
    }

    /// <summary>
    /// Task S5 Part 1: captures a door or window on one of the four walls
    /// Task S3/S4 already derived, by intersecting the center-screen ray with
    /// the selected wall's plane at two points — a lower-left and an
    /// upper-right — exactly as implementation plan section 16, Task S5
    /// specifies.
    ///
    /// <para><b>Walls are re-derived, never stored.</b> Exactly like
    /// <see cref="HeightCaptureController"/>, this class calls
    /// <see cref="RoomGeometry.BuildWalls"/> against the live corners on every
    /// access rather than caching a wall list, so an opening's wall can never
    /// silently disagree with the footprint it came from.</para>
    ///
    /// <para><b>The frame, corners and height are read, never written.</b>
    /// Nothing here can move the S2 frame, mutate an S3 corner or change the
    /// S4 height: all three are reached through read-only references.</para>
    ///
    /// <para>Validation is the shared package's. Every candidate opening runs
    /// <see cref="OpeningValidator.Validate"/> against a room built from the
    /// real corners, the real captured height and the openings already
    /// accepted, so wall containment, the ceiling rule and overlap with
    /// existing openings are enforced in exactly one place
    /// (<c>AGENTS.md</c> rules 2 and 3).</para>
    ///
    /// <para>Plain C# rather than a MonoBehaviour, for the same reason as
    /// every other Task S2-S4 controller: every rule below is testable
    /// without a device. <see cref="UI.OpeningCaptureHud"/> is the
    /// scene-facing wrapper.</para>
    /// </summary>
    public sealed class OpeningCaptureController
    {
        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;
        private readonly CornerCaptureController corners;
        private readonly HeightCaptureController height;
        private readonly List<OpeningModel> openings = new List<OpeningModel>();

        public OpeningCaptureController(
            ISpatialProvider provider,
            FloorLockController floorLock,
            CornerCaptureController corners,
            HeightCaptureController height)
        {
            this.provider = provider;
            this.floorLock = floorLock;
            this.corners = corners;
            this.height = height;
            SelectedWallIndex = -1;
            SelectedType = OpeningValidator.TypeDoor;
        }

        /// <summary>The frame Task S2 locked, or null before the floor is locked.</summary>
        public GhostCoordinateFrame Frame => floorLock.Frame;

        /// <summary>
        /// The room's walls, derived on demand from the captured corners —
        /// the same four Task S4 derives its wall planes from.
        /// </summary>
        public IReadOnlyList<WallDefinition> Walls => BuildWalls();

        public int WallCount => corners.IsComplete ? corners.CornerCount : 0;

        /// <summary>Index into <see cref="Walls"/>, or -1 when nothing is selected.</summary>
        public int SelectedWallIndex { get; private set; }

        public bool HasWallSelected => SelectedWallIndex >= 0 && SelectedWallIndex < WallCount;

        /// <summary><see cref="OpeningValidator.TypeDoor"/> or <see cref="OpeningValidator.TypeWindow"/>.</summary>
        public string SelectedType { get; private set; }

        /// <summary>Openings captured so far, in capture order.</summary>
        public IReadOnlyList<OpeningModel> Openings => openings;

        public int OpeningCount => openings.Count;

        /// <summary>Whether a lower-left point has been captured and is waiting for an upper-right point.</summary>
        public bool HasPendingStartPoint { get; private set; }

        /// <summary>The pending start point, in Ghost coordinates. Meaningless unless <see cref="HasPendingStartPoint"/>.</summary>
        public Vector3 PendingStartGhost { get; private set; }

        /// <summary>Why the most recent operation was refused.</summary>
        public OpeningCaptureRejection LastRejection { get; private set; }

        /// <summary>
        /// The shared validator's message for the most recent
        /// <see cref="OpeningCaptureRejection.ValidationFailed"/>, shown to the
        /// user verbatim. Empty when the last operation did not fail the rule.
        /// </summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// Whether the capture control should be enabled. Whether the
        /// crosshair actually yields a legal point is only known once the
        /// attempt runs.
        /// </summary>
        public bool CanAimAtWall =>
            Frame != null && provider.IsTrackingGood && corners.IsComplete && HasWallSelected;

        // -------------------------------------------------------------------
        // Wall selection and type
        // -------------------------------------------------------------------

        /// <summary>
        /// Selects a wall by index into <see cref="Walls"/>. Any pending start
        /// point is discarded: it was measured against whichever wall was
        /// selected when it was captured, and a wall switch mid-opening would
        /// silently mix two different wall-local coordinate systems.
        /// </summary>
        public bool SelectWall(int index)
        {
            if (!corners.IsComplete || index < 0 || index >= WallCount)
            {
                return false;
            }

            SelectedWallIndex = index;
            CancelPendingCapture();
            return true;
        }

        /// <summary>
        /// Clears the current wall selection and any pending start point. Used
        /// when the footprint the selection was derived from is about to
        /// change, so a stale index cannot silently resolve against a
        /// different wall.
        /// </summary>
        public void ResetWallSelection()
        {
            SelectedWallIndex = -1;
            CancelPendingCapture();
        }

        /// <summary>Chooses door or window. Rejects any other string.</summary>
        public bool SetType(string type)
        {
            if (type != OpeningValidator.TypeDoor && type != OpeningValidator.TypeWindow)
            {
                return false;
            }

            SelectedType = type;
            return true;
        }

        /// <summary>Discards a captured start point without touching the accepted openings.</summary>
        public void CancelPendingCapture()
        {
            HasPendingStartPoint = false;
            PendingStartGhost = Vector3.zero;
        }

        private IReadOnlyList<WallDefinition> BuildWalls()
        {
            if (!corners.IsComplete)
            {
                return Array.Empty<WallDefinition>();
            }

            return RoomGeometry.BuildWalls(BuildWallShellRoom());
        }

        // -------------------------------------------------------------------
        // The wall ray
        // -------------------------------------------------------------------

        /// <summary>
        /// Projects the center-screen camera ray onto the selected wall's
        /// vertical plane and returns the result in Ghost coordinates.
        /// </summary>
        public bool TryProjectCrosshairToWall(out Vector3 ghostPoint)
        {
            OpeningCaptureRejection rejection = EvaluateAim(out ghostPoint);
            return rejection == OpeningCaptureRejection.None;
        }

        /// <summary>
        /// Intersects a Ghost-space ray with a wall's vertical plane,
        /// distinguishing a parallel ray from one whose intersection lies
        /// behind the camera — the same distinction Task S4's
        /// <c>HeightCaptureController</c> exposes, needed here for the same
        /// reason: the plan lists them as separate cases.
        /// </summary>
        private static bool TryIntersectWallPlane(
            Ray ghostRay,
            WallDefinition wall,
            out Vector3 point,
            out OpeningCaptureRejection rejection)
        {
            point = Vector3.zero;

            Plane plane = WallGeometry.PlaneFor(wall);
            float denom = Vector3.Dot(plane.normal, ghostRay.direction);

            if (Mathf.Abs(denom) < RayPlaneMath.ParallelEpsilon)
            {
                rejection = OpeningCaptureRejection.RayParallelToWall;
                return false;
            }

            if (!plane.Raycast(ghostRay, out float distance) || distance <= 0f)
            {
                rejection = OpeningCaptureRejection.RayBehindCamera;
                return false;
            }

            point = ghostRay.GetPoint(distance);
            rejection = OpeningCaptureRejection.None;
            return true;
        }

        private OpeningCaptureRejection EvaluateAim(out Vector3 ghostPoint)
        {
            ghostPoint = Vector3.zero;

            GhostCoordinateFrame frame = Frame;

            if (frame == null)
            {
                return OpeningCaptureRejection.FloorNotLocked;
            }

            if (!provider.IsTrackingGood)
            {
                return OpeningCaptureRejection.TrackingNotGood;
            }

            if (!corners.IsComplete)
            {
                return OpeningCaptureRejection.NotAllCornersCaptured;
            }

            if (!HasWallSelected)
            {
                return OpeningCaptureRejection.NoWallSelected;
            }

            WallDefinition wall = Walls[SelectedWallIndex];
            Ray worldRay = provider.GetScreenRay(provider.CenterScreenPoint);
            Ray ghostRay = frame.WorldRayToGhost(worldRay);

            if (!TryIntersectWallPlane(ghostRay, wall, out Vector3 point, out OpeningCaptureRejection rayRejection))
            {
                return rayRejection;
            }

            ghostPoint = point;
            return OpeningCaptureRejection.None;
        }

        // -------------------------------------------------------------------
        // Two-point capture
        // -------------------------------------------------------------------

        /// <summary>Captures the lower-left point under the crosshair.</summary>
        public bool TryCaptureStartPoint(out OpeningCaptureRejection rejection)
        {
            LastError = string.Empty;

            rejection = EvaluateAim(out Vector3 point);
            LastRejection = rejection;

            if (rejection != OpeningCaptureRejection.None)
            {
                return false;
            }

            PendingStartGhost = point;
            HasPendingStartPoint = true;
            return true;
        }

        /// <summary>
        /// Captures the upper-right point under the crosshair, converts both
        /// points to the selected wall's local <c>u/v</c>, derives the
        /// opening's offset/width/sill/height per implementation plan
        /// section 16, validates it against the shared
        /// <see cref="OpeningValidator"/>, and appends it on success.
        ///
        /// <para>A rejected candidate leaves <see cref="Openings"/> exactly as
        /// it was; the pending start point is cleared either way, so a failed
        /// attempt always restarts from a clean two-point capture rather than
        /// silently retrying against a stale first point.</para>
        /// </summary>
        public bool TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection)
        {
            LastError = string.Empty;

            if (!HasPendingStartPoint)
            {
                rejection = OpeningCaptureRejection.NoStartPointCaptured;
                LastRejection = rejection;
                return false;
            }

            rejection = EvaluateAim(out Vector3 endPoint);

            if (rejection != OpeningCaptureRejection.None)
            {
                LastRejection = rejection;
                CancelPendingCapture();
                return false;
            }

            WallDefinition wall = Walls[SelectedWallIndex];

            WallGeometry.ToWallLocal(wall, PendingStartGhost, out float u1, out float v1);
            WallGeometry.ToWallLocal(wall, endPoint, out float u2, out float v2);

            OpeningModel candidate = BuildOpeningModel(wall, u1, v1, u2, v2);
            RoomModel candidateRoom = BuildCandidateRoom(candidate);

            ValidationResult validation = OpeningValidator.Validate(candidate, candidateRoom);

            CancelPendingCapture();

            if (!validation.IsValid)
            {
                LastError = validation.Error;
                rejection = OpeningCaptureRejection.ValidationFailed;
                LastRejection = rejection;
                return false;
            }

            openings.Add(candidate);
            rejection = OpeningCaptureRejection.None;
            LastRejection = rejection;
            return true;
        }

        private OpeningModel BuildOpeningModel(WallDefinition wall, float u1, float v1, float u2, float v2)
        {
            float offset = Mathf.Min(u1, u2);
            float width = Mathf.Abs(u2 - u1);
            float sill;
            float openingHeight;

            if (SelectedType == OpeningValidator.TypeWindow)
            {
                sill = Mathf.Min(v1, v2);
                openingHeight = Mathf.Abs(v2 - v1);
            }
            else
            {
                sill = 0f;
                openingHeight = Mathf.Max(v1, v2);
            }

            return new OpeningModel
            {
                id = Guid.NewGuid().ToString(),
                type = SelectedType,
                wallStartCornerId = wall.StartCornerId,
                wallEndCornerId = wall.EndCornerId,
                offsetM = offset,
                widthM = width,
                sillHeightM = sill,
                heightM = openingHeight
            };
        }

        // -------------------------------------------------------------------
        // Undo
        // -------------------------------------------------------------------

        /// <summary>Removes the most recently captured opening.</summary>
        public bool TryUndoLastOpening(out OpeningCaptureRejection rejection)
        {
            LastError = string.Empty;

            if (openings.Count == 0)
            {
                rejection = OpeningCaptureRejection.NoOpeningsToUndo;
                LastRejection = rejection;
                return false;
            }

            openings.RemoveAt(openings.Count - 1);

            rejection = OpeningCaptureRejection.None;
            LastRejection = rejection;
            return true;
        }

        // -------------------------------------------------------------------
        // Snapshot support
        // -------------------------------------------------------------------

        /// <summary>
        /// Openings as a fresh array of fresh <see cref="OpeningModel"/>
        /// values, for the same reason <c>CornerCaptureController.CopyCorners</c>
        /// copies: a published snapshot is a value, not a window onto live
        /// state.
        /// </summary>
        public OpeningModel[] CopyOpenings()
        {
            var copy = new OpeningModel[openings.Count];

            for (int i = 0; i < openings.Count; i++)
            {
                copy[i] = new OpeningModel
                {
                    id = openings[i].id,
                    type = openings[i].type,
                    wallStartCornerId = openings[i].wallStartCornerId,
                    wallEndCornerId = openings[i].wallEndCornerId,
                    offsetM = openings[i].offsetM,
                    widthM = openings[i].widthM,
                    sillHeightM = openings[i].sillHeightM,
                    heightM = openings[i].heightM
                };
            }

            return copy;
        }

        private RoomModel BuildWallShellRoom()
        {
            return new RoomModel
            {
                id = "candidate",
                name = "Room",
                heightM = 0f,
                corners = corners.CopyCorners(),
                openings = Array.Empty<OpeningModel>(),
                objects = Array.Empty<SceneObjectModel>()
            };
        }

        private RoomModel BuildCandidateRoom(OpeningModel appended)
        {
            var allOpenings = new OpeningModel[openings.Count + 1];

            for (int i = 0; i < openings.Count; i++)
            {
                allOpenings[i] = openings[i];
            }

            allOpenings[openings.Count] = appended;

            return new RoomModel
            {
                id = "candidate",
                name = "Room",
                heightM = height.HasCapturedHeight ? height.HeightM : 0f,
                corners = corners.CopyCorners(),
                openings = allOpenings,
                objects = Array.Empty<SceneObjectModel>()
            };
        }
    }
}
