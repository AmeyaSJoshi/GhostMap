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
    /// Why a height capture, wall selection or manual entry was refused.
    /// <see cref="None"/> means it succeeded.
    /// </summary>
    public enum HeightCaptureRejection
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

        /// <summary>The candidate height failed the shared 2.0-4.0 m range rule.</summary>
        ValidationFailed
    }

    /// <summary>
    /// Task S4: derives the room's vertical wall planes from the four corners
    /// Task S3 captured, intersects the center-screen ray with the wall the
    /// user selected, and validates the resulting room height.
    ///
    /// <para><b>Walls are generated mathematically, never detected.</b> The
    /// floor corners are already exact — that is what closure verification
    /// bought — so a vertical plane through two consecutive corners is exactly
    /// where the real wall is, without waiting on ARKit to find a vertical
    /// plane or depending on LiDAR. <see cref="WallGeometry.PlaneFor"/> builds
    /// it; this class never raycasts against a detected plane.</para>
    ///
    /// <para><b>The frame and the footprint are read, never written.</b>
    /// Nothing here can move the S2 frame or the S3 corners: both are reached
    /// through <see cref="FloorLockController"/> and
    /// <see cref="CornerCaptureController"/>, neither of which this class
    /// holds a mutating reference to.</para>
    ///
    /// <para><b>No manual camera-offset compensation.</b> The camera ray is
    /// converted to Ghost space with <see cref="GhostCoordinateFrame.WorldRayToGhost"/>
    /// exactly as S2 established, and intersected with the wall plane
    /// directly. Height is read off the intersection's Ghost Y; nothing here
    /// adds or subtracts a camera offset by hand, because doing so would
    /// double-count the correction <see cref="GhostCoordinateFrame"/> already
    /// makes by construction.</para>
    ///
    /// <para>Plain C# rather than a MonoBehaviour, for the same reason as
    /// <see cref="CornerCaptureController"/>: every rule below is testable
    /// without a device. <see cref="UI.HeightCaptureHud"/> is the scene-facing
    /// wrapper.</para>
    /// </summary>
    public sealed class HeightCaptureController
    {
        /// <summary>
        /// How far outside a wall's [0, length] span an intersection may fall
        /// and still be reported as "on" the wall. Purely informational — it
        /// drives the HUD's aim readout, not whether a capture is accepted.
        /// </summary>
        public const float WallSpanToleranceM = 0.20f;

        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;
        private readonly CornerCaptureController corners;

        public HeightCaptureController(
            ISpatialProvider provider,
            FloorLockController floorLock,
            CornerCaptureController corners)
        {
            this.provider = provider;
            this.floorLock = floorLock;
            this.corners = corners;
            SelectedWallIndex = -1;
        }

        /// <summary>The frame Task S2 locked, or null before the floor is locked.</summary>
        public GhostCoordinateFrame Frame => floorLock.Frame;

        /// <summary>
        /// The room's walls, derived on demand from the captured corners.
        /// Empty until all four corners exist.
        /// </summary>
        public IReadOnlyList<WallDefinition> Walls => BuildWalls();

        public int WallCount => corners.IsComplete ? corners.CornerCount : 0;

        /// <summary>Index into <see cref="Walls"/>, or -1 when nothing is selected.</summary>
        public int SelectedWallIndex { get; private set; }

        public bool HasWallSelected => SelectedWallIndex >= 0 && SelectedWallIndex < WallCount;

        /// <summary>Why the most recent operation was refused.</summary>
        public HeightCaptureRejection LastRejection { get; private set; }

        /// <summary>
        /// The shared validator's message for the most recent
        /// <see cref="HeightCaptureRejection.ValidationFailed"/>, shown to the
        /// user verbatim. Empty when the last operation did not fail the rule.
        /// </summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>Whether a height has been confirmed, automatically or manually.</summary>
        public bool HasCapturedHeight { get; private set; }

        /// <summary>The confirmed room height, or 0 before one is captured.</summary>
        public float HeightM { get; private set; }

        /// <summary>True when the confirmed height came from the manual fallback.</summary>
        public bool IsManualEntry { get; private set; }

        /// <summary>
        /// Whether the capture control should be enabled. Whether the
        /// crosshair actually yields a legal height is only known once the
        /// attempt runs.
        /// </summary>
        public bool CanAimAtWall =>
            Frame != null && provider.IsTrackingGood && corners.IsComplete && HasWallSelected;

        // -------------------------------------------------------------------
        // Wall selection
        // -------------------------------------------------------------------

        /// <summary>
        /// Selects a wall by index into <see cref="Walls"/>, derived from
        /// consecutive corner pairs exactly as <see cref="RoomGeometry.BuildWalls"/>
        /// orders them: wall 0 is corner 0 -&gt; corner 1, and so on.
        /// </summary>
        public bool SelectWall(int index)
        {
            if (!corners.IsComplete || index < 0 || index >= WallCount)
            {
                return false;
            }

            SelectedWallIndex = index;
            return true;
        }

        /// <summary>
        /// Clears the current wall selection. Used when the footprint the
        /// selection was derived from is about to change (a corners redo), so
        /// a stale index cannot silently resolve against a different wall.
        /// </summary>
        public void ResetWallSelection()
        {
            SelectedWallIndex = -1;
        }

        private IReadOnlyList<WallDefinition> BuildWalls()
        {
            if (!corners.IsComplete)
            {
                return Array.Empty<WallDefinition>();
            }

            var room = new RoomModel
            {
                id = "candidate",
                name = "Room",
                heightM = 0f,
                corners = corners.CopyCorners(),
                openings = Array.Empty<OpeningModel>(),
                objects = Array.Empty<SceneObjectModel>()
            };

            return RoomGeometry.BuildWalls(room);
        }

        // -------------------------------------------------------------------
        // The wall ray
        // -------------------------------------------------------------------

        /// <summary>
        /// Projects the center-screen camera ray onto the selected wall's
        /// vertical plane and returns the result in GhostMap coordinates.
        /// </summary>
        public bool TryProjectCrosshairToWall(out Vector3 ghostPoint)
            => TryProjectCrosshairToWall(out ghostPoint, out _);

        /// <summary>
        /// As <see cref="TryProjectCrosshairToWall(out Vector3)"/>, and also
        /// reports whether the intersection falls within (or close to) the
        /// selected wall's horizontal span. This is informational only, for
        /// the HUD: it does not gate <see cref="TryCaptureHeight"/>, because a
        /// user aiming slightly past a wall's endpoint at the ceiling line is
        /// still aiming at that wall, not a different one.
        /// </summary>
        public bool TryProjectCrosshairToWall(out Vector3 ghostPoint, out bool withinWallSpan)
        {
            ghostPoint = Vector3.zero;
            withinWallSpan = false;

            GhostCoordinateFrame frame = Frame;

            if (frame == null || !corners.IsComplete || !HasWallSelected)
            {
                return false;
            }

            WallDefinition wall = Walls[SelectedWallIndex];
            Ray worldRay = provider.GetScreenRay(provider.CenterScreenPoint);
            Ray ghostRay = frame.WorldRayToGhost(worldRay);

            if (!TryIntersectWallPlane(ghostRay, wall, out Vector3 point, out _))
            {
                return false;
            }

            ghostPoint = point;

            WallGeometry.ToWallLocal(wall, point, out float u, out _);
            withinWallSpan = u >= -WallSpanToleranceM && u <= wall.LengthM + WallSpanToleranceM;

            return true;
        }

        /// <summary>
        /// Intersects a Ghost-space ray with a wall's vertical plane,
        /// distinguishing a parallel ray from one whose intersection lies
        /// behind the camera. <see cref="RayPlaneMath.TryIntersectPlane"/>
        /// collapses both into a single failure; height capture needs to tell
        /// them apart so the HUD can say which is wrong.
        /// </summary>
        private static bool TryIntersectWallPlane(
            Ray ghostRay,
            WallDefinition wall,
            out Vector3 point,
            out HeightCaptureRejection rejection)
        {
            point = Vector3.zero;

            Plane plane = WallGeometry.PlaneFor(wall);
            float denom = Vector3.Dot(plane.normal, ghostRay.direction);

            if (Mathf.Abs(denom) < RayPlaneMath.ParallelEpsilon)
            {
                rejection = HeightCaptureRejection.RayParallelToWall;
                return false;
            }

            if (!plane.Raycast(ghostRay, out float distance) || distance <= 0f)
            {
                rejection = HeightCaptureRejection.RayBehindCamera;
                return false;
            }

            point = ghostRay.GetPoint(distance);
            rejection = HeightCaptureRejection.None;
            return true;
        }

        // -------------------------------------------------------------------
        // Capture
        // -------------------------------------------------------------------

        /// <summary>
        /// Captures the height under the crosshair against the selected wall.
        /// A rejected candidate leaves <see cref="HeightM"/> and
        /// <see cref="HasCapturedHeight"/> exactly as they were.
        /// </summary>
        public bool TryCaptureHeight(out HeightCaptureRejection rejection)
        {
            LastError = string.Empty;

            rejection = EvaluateCapture(out float candidate);
            LastRejection = rejection;

            if (rejection != HeightCaptureRejection.None)
            {
                return false;
            }

            HeightM = candidate;
            HasCapturedHeight = true;
            IsManualEntry = false;
            return true;
        }

        private HeightCaptureRejection EvaluateCapture(out float candidateHeightM)
        {
            candidateHeightM = 0f;

            GhostCoordinateFrame frame = Frame;

            if (frame == null)
            {
                return HeightCaptureRejection.FloorNotLocked;
            }

            if (!provider.IsTrackingGood)
            {
                return HeightCaptureRejection.TrackingNotGood;
            }

            if (!corners.IsComplete)
            {
                return HeightCaptureRejection.NotAllCornersCaptured;
            }

            if (!HasWallSelected)
            {
                return HeightCaptureRejection.NoWallSelected;
            }

            WallDefinition wall = Walls[SelectedWallIndex];
            Ray worldRay = provider.GetScreenRay(provider.CenterScreenPoint);
            Ray ghostRay = frame.WorldRayToGhost(worldRay);

            if (!TryIntersectWallPlane(ghostRay, wall, out Vector3 point, out HeightCaptureRejection rayRejection))
            {
                return rayRejection;
            }

            ValidationResult validation = ValidateHeight(point.y);

            if (!validation.IsValid)
            {
                LastError = validation.Error;
                return HeightCaptureRejection.ValidationFailed;
            }

            candidateHeightM = point.y;
            return HeightCaptureRejection.None;
        }

        /// <summary>
        /// The manual fallback: a typed height, validated against the same
        /// range as an automatic capture. Implementation plan section 8.7 —
        /// "a failed automatic height capture must never block the demo".
        /// </summary>
        public bool TrySetManualHeight(float heightM, out HeightCaptureRejection rejection)
        {
            LastError = string.Empty;

            ValidationResult validation = ValidateHeight(heightM);

            if (!validation.IsValid)
            {
                LastError = validation.Error;
                rejection = HeightCaptureRejection.ValidationFailed;
                LastRejection = rejection;
                return false;
            }

            HeightM = heightM;
            HasCapturedHeight = true;
            IsManualEntry = true;

            rejection = HeightCaptureRejection.None;
            LastRejection = rejection;
            return true;
        }

        /// <summary>
        /// The shared 2.0-4.0 m room-height range, sourced from
        /// <see cref="RoomValidator"/>'s own constants so the two can never
        /// drift apart.
        /// </summary>
        public static ValidationResult ValidateHeight(float candidateHeightM)
        {
            if (float.IsNaN(candidateHeightM) || float.IsInfinity(candidateHeightM))
            {
                return ValidationResult.Invalid("Height must be finite.");
            }

            if (candidateHeightM < RoomValidator.MinRoomHeightM ||
                candidateHeightM > RoomValidator.MaxRoomHeightM)
            {
                return ValidationResult.Invalid(
                    $"Height {candidateHeightM:F2} m is outside the supported range " +
                    $"{RoomValidator.MinRoomHeightM:F1}-{RoomValidator.MaxRoomHeightM:F1} m.");
            }

            return ValidationResult.Valid();
        }
    }
}
