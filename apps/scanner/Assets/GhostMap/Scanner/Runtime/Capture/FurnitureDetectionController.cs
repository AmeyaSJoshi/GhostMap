using System;
using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// Why a detection operation was refused. <see cref="None"/> means it
    /// succeeded.
    /// </summary>
    public enum FurnitureDetectionRejection
    {
        None = 0,

        /// <summary>There is no GhostMap frame yet. Lock the floor first.</summary>
        FloorNotLocked,

        /// <summary>The session is not tracking, or reports a not-tracking reason.</summary>
        TrackingNotGood,

        /// <summary>The room footprint is not complete, so "inside the room" cannot be decided.</summary>
        NoRoomFootprint,

        /// <summary>Plane detection is unavailable.</summary>
        DetectionUnavailable,

        /// <summary>No candidate is selected, or the selection no longer exists.</summary>
        NoCandidateSelected,

        /// <summary>The selected type is not one of the MVP's supported furniture types.</summary>
        UnsupportedType,

        /// <summary>
        /// The shared <see cref="FurnitureValidator"/> refused the candidate.
        /// See <see cref="FurnitureDetectionController.LastError"/>.
        /// </summary>
        ValidationFailed
    }

    /// <summary>
    /// Why a detected surface is not offered as furniture.
    /// <see cref="Accepted"/> means it is.
    /// </summary>
    public enum SurfaceVerdict
    {
        Accepted = 0,

        /// <summary>Not a horizontal, upward-facing plane.</summary>
        NotHorizontalUp,

        /// <summary>At or near floor level: the floor itself, a rug, a threshold.</summary>
        TooLow,

        /// <summary>Above furniture height: a ceiling, a shelf, a counter out of MVP scope.</summary>
        TooHigh,

        /// <summary>Smaller than the smallest thing the MVP calls furniture.</summary>
        TooSmall,

        /// <summary>Larger than the room it is supposed to sit in.</summary>
        TooLarge,

        /// <summary>Its footprint centre lies outside the room polygon.</summary>
        OutsideRoom,

        /// <summary>Already turned into an object, or dismissed by the user.</summary>
        AlreadyResolved
    }

    /// <summary>
    /// One detected surface that passed every gate, measured and ready for the
    /// user to name.
    /// </summary>
    public readonly struct FurnitureCandidate
    {
        public FurnitureCandidate(
            TrackableId surfaceId,
            Vector3 centerGhost,
            float widthM,
            float depthM,
            float heightM,
            float yawDeg)
        {
            SurfaceId = surfaceId;
            CenterGhost = centerGhost;
            WidthM = widthM;
            DepthM = depthM;
            HeightM = heightM;
            YawDeg = yawDeg;
        }

        public TrackableId SurfaceId { get; }

        /// <summary>Footprint centre, Ghost space, <c>y</c> forced to 0.</summary>
        public Vector3 CenterGhost { get; }

        public float WidthM { get; }

        public float DepthM { get; }

        /// <summary>
        /// Height of the detected top surface above the floor, in meters.
        ///
        /// <para>This is the number the manual S5 path cannot measure, and the
        /// reason detection is worth having. It is correct for a desk, table,
        /// dresser, bed or TV stand. It is <b>wrong</b> for a chair or couch,
        /// where the detected plane is the seat and the back rises above it —
        /// the user corrects those with S5's existing controls. Recorded in
        /// <c>ADR-0006</c>'s Consequences.</para>
        /// </summary>
        public float HeightM { get; }

        public float YawDeg { get; }
    }

    /// <summary>
    /// ADR-0006: finds furniture by reading detected horizontal planes, and
    /// measures it, so the user names the object instead of typing its
    /// dimensions.
    ///
    /// <para><b>The geometric idea.</b> A horizontal plane at Ghost height
    /// <c>y</c> is the top surface of something <c>y</c> tall. Height, footprint
    /// and yaw therefore all fall out of one detected plane, and height is
    /// exactly the number the S5 manual path had to guess from a per-type
    /// default.</para>
    ///
    /// <para><b>This recognizes nothing.</b> It finds a horizontal rectangle and
    /// measures it. The user picks the type. <c>AGENTS.md</c> rule 6, which
    /// prohibits automatic object recognition and cloud inference until the MVP
    /// acceptance test passes, is not touched — see <c>ADR-0006</c>.</para>
    ///
    /// <para><b>This does not weaken <c>ADR-0004</c>.</b> That rejected
    /// depending on detection of blank <i>vertical</i> walls. Horizontal
    /// furniture surfaces are the favourable case, and this is the same
    /// detection the device-verified floor lock already relies on. Detection
    /// failure is also not fatal: S5 manual placement remains.</para>
    ///
    /// <para><b>The frame, the footprint and the object list are read, never
    /// written.</b> An accepted candidate is handed to
    /// <see cref="ObjectPlacementController.TryAdoptDetectedObject"/>, so there
    /// stays exactly one object store feeding the snapshot.</para>
    ///
    /// <para>Plain C# rather than a MonoBehaviour, so every gate below is
    /// testable without a device.</para>
    /// </summary>
    public sealed class FurnitureDetectionController
    {
        /// <summary>
        /// Minimum top-surface height above the floor, in meters. Below this is
        /// the floor plane itself, a rug, or a door threshold.
        /// </summary>
        public const float MinSurfaceHeightM = 0.20f;

        /// <summary>
        /// Maximum top-surface height, in meters. A dresser is about 0.9 m and a
        /// tall chest about 1.2 m; above 1.4 m is a wall shelf, a kitchen
        /// counter or a ceiling, none of which the MVP's eight furniture types
        /// describe.
        /// </summary>
        public const float MaxSurfaceHeightM = 1.40f;

        /// <summary>
        /// Minimum extent on both axes, in meters. Matches the opening-width
        /// floor; below it ARKit is reporting clutter.
        /// </summary>
        public const float MinExtentM = 0.30f;

        /// <summary>
        /// Maximum extent on either axis, in meters. Sourced from the shared
        /// furniture rules rather than chosen here.
        /// </summary>
        public const float MaxExtentM = FurnitureValidator.MaxDimensionM;

        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;
        private readonly CornerCaptureController corners;

        private readonly List<DetectedSurface> surfaceBuffer = new List<DetectedSurface>(32);
        private readonly List<FurnitureCandidate> candidates = new List<FurnitureCandidate>(16);
        private readonly HashSet<TrackableId> resolved = new HashSet<TrackableId>();
        private readonly List<Vector3> footprint = new List<Vector3>(4);

        public FurnitureDetectionController(
            ISpatialProvider provider,
            FloorLockController floorLock,
            CornerCaptureController corners)
        {
            this.provider = provider;
            this.floorLock = floorLock;
            this.corners = corners;
        }

        /// <summary>The frame Task S2 locked, or null before the floor is locked.</summary>
        public GhostCoordinateFrame Frame => floorLock.Frame;

        /// <summary>Candidates from the most recent <see cref="Refresh"/>, measured and gated.</summary>
        public IReadOnlyList<FurnitureCandidate> Candidates => candidates;

        public int CandidateCount => candidates.Count;

        /// <summary>
        /// Index into <see cref="Candidates"/>, or -1. Survives a
        /// <see cref="Refresh"/> by tracking the selected surface's id rather
        /// than its position, because a plane's position in the list is not
        /// stable between frames.
        /// </summary>
        public int SelectedIndex { get; private set; } = -1;

        public bool HasSelection => SelectedIndex >= 0 && SelectedIndex < candidates.Count;

        /// <summary>The type the user has chosen for the next accepted candidate.</summary>
        public string SelectedType { get; private set; } = "generic";

        public FurnitureDetectionRejection LastRejection { get; private set; }

        /// <summary>A user-facing explanation for the most recent failure. Empty on success.</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>How many detected surfaces the last refresh looked at, before gating.</summary>
        public int LastSurfaceCount { get; private set; }

        /// <summary>
        /// Whether detection can run at all: floor locked, tracking good, and a
        /// complete footprint to test containment against.
        /// </summary>
        public bool CanDetect =>
            Frame != null && provider.IsTrackingGood && corners.IsComplete;

        // -------------------------------------------------------------------
        // Type selection
        // -------------------------------------------------------------------

        /// <summary>
        /// Chooses the type the next accepted candidate becomes. Validated
        /// against the shared supported-type list, never a scanner copy.
        /// </summary>
        public bool SetType(string type)
        {
            LastError = string.Empty;

            if (!FurnitureValidator.IsSupportedType(type))
            {
                LastRejection = FurnitureDetectionRejection.UnsupportedType;
                return false;
            }

            SelectedType = type;
            LastRejection = FurnitureDetectionRejection.None;
            return true;
        }

        // -------------------------------------------------------------------
        // Detection
        // -------------------------------------------------------------------

        /// <summary>
        /// Re-reads detected planes and rebuilds the candidate list.
        ///
        /// <para>Publishes nothing and mutates no scene state — the HUD calls
        /// this while the user looks around, and a candidate is not furniture
        /// until it is accepted.</para>
        /// </summary>
        public bool Refresh(out FurnitureDetectionRejection rejection)
        {
            LastError = string.Empty;

            TrackableId previouslySelected = default;
            bool hadSelection = HasSelection;

            if (hadSelection)
            {
                previouslySelected = candidates[SelectedIndex].SurfaceId;
            }

            candidates.Clear();
            SelectedIndex = -1;
            LastSurfaceCount = 0;

            GhostCoordinateFrame frame = Frame;

            if (frame == null)
            {
                rejection = Fail(FurnitureDetectionRejection.FloorNotLocked);
                return false;
            }

            if (!provider.IsTrackingGood)
            {
                rejection = Fail(FurnitureDetectionRejection.TrackingNotGood);
                return false;
            }

            if (!corners.IsComplete)
            {
                rejection = Fail(FurnitureDetectionRejection.NoRoomFootprint);
                return false;
            }

            if (!provider.TryGetDetectedSurfaces(surfaceBuffer))
            {
                rejection = Fail(FurnitureDetectionRejection.DetectionUnavailable);
                return false;
            }

            LastSurfaceCount = surfaceBuffer.Count;
            RebuildFootprint();

            for (int i = 0; i < surfaceBuffer.Count; i++)
            {
                if (TryEvaluate(surfaceBuffer[i], frame, out FurnitureCandidate candidate)
                    == SurfaceVerdict.Accepted)
                {
                    candidates.Add(candidate);
                }
            }

            // Restore the selection by identity, not by index.
            if (hadSelection)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].SurfaceId.Equals(previouslySelected))
                    {
                        SelectedIndex = i;
                        break;
                    }
                }
            }

            if (SelectedIndex < 0 && candidates.Count > 0)
            {
                SelectedIndex = 0;
            }

            rejection = Succeed();
            return true;
        }

        /// <summary>
        /// Applies every gate to one detected surface and, when it passes,
        /// produces the measured candidate.
        ///
        /// <para>Public and side-effect free so each rule can be tested in
        /// isolation and the HUD can explain why a surface the user is looking
        /// at was not offered.</para>
        ///
        /// <para>The in-room gate reads the footprint cached by the last
        /// <see cref="Refresh"/>, and is skipped when no refresh has run. That
        /// is deliberate rather than a hole: keeping this method free of side
        /// effects is what makes every other gate testable in isolation, and
        /// both real callers — <see cref="Refresh"/> itself and the HUD — always
        /// refresh first.</para>
        /// </summary>
        public SurfaceVerdict TryEvaluate(
            DetectedSurface surface,
            GhostCoordinateFrame frame,
            out FurnitureCandidate candidate)
        {
            candidate = default;

            if (surface.Alignment != PlaneAlignment.HorizontalUp)
            {
                return SurfaceVerdict.NotHorizontalUp;
            }

            if (resolved.Contains(surface.Id))
            {
                return SurfaceVerdict.AlreadyResolved;
            }

            Vector3 centerGhost = frame.WorldToGhost(surface.WorldCenter);

            if (centerGhost.y < MinSurfaceHeightM)
            {
                return SurfaceVerdict.TooLow;
            }

            if (centerGhost.y > MaxSurfaceHeightM)
            {
                return SurfaceVerdict.TooHigh;
            }

            float widthM = surface.ExtentsM.x;
            float depthM = surface.ExtentsM.y;

            if (widthM < MinExtentM || depthM < MinExtentM)
            {
                return SurfaceVerdict.TooSmall;
            }

            if (widthM > MaxExtentM || depthM > MaxExtentM)
            {
                return SurfaceVerdict.TooLarge;
            }

            var footprintCenter = new Vector3(centerGhost.x, 0f, centerGhost.z);

            if (footprint.Count >= 3 &&
                !RoomGeometry.ContainsPointXZ(footprint, footprintCenter))
            {
                return SurfaceVerdict.OutsideRoom;
            }

            candidate = new FurnitureCandidate(
                surface.Id,
                footprintCenter,
                widthM,
                depthM,
                centerGhost.y,
                YawFromSurface(surface, frame));

            return SurfaceVerdict.Accepted;
        }

        /// <summary>
        /// The surface's own orientation, as a Ghost-space yaw in degrees.
        ///
        /// <para>The plane's local +X spans the surface. Taken through the Ghost
        /// frame and projected into XZ, its bearing is the object's yaw — which
        /// is why a detected desk comes out square to the desk rather than
        /// square to the room.</para>
        ///
        /// <para>Normalized into <c>[0, 360)</c>. The sign of the plane's axis is
        /// arbitrary, so a detected box's yaw is only defined modulo 180
        /// degrees; width and depth may therefore come out swapped relative to
        /// how a person would name them. Harmless for an axis-aligned box, and
        /// the user can correct it.</para>
        /// </summary>
        private static float YawFromSurface(DetectedSurface surface, GhostCoordinateFrame frame)
        {
            Vector3 worldAxis = surface.WorldRotation * Vector3.right;
            Vector3 ghostAxis = frame.WorldDirectionToGhost(worldAxis);

            var flat = new Vector2(ghostAxis.x, ghostAxis.z);

            if (flat.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }

            // Ghost +Z is the zero bearing, matching how yaw is applied as a
            // rotation about +Y elsewhere.
            float yawDeg = Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg;

            return Mathf.Repeat(yawDeg, 360f);
        }

        // -------------------------------------------------------------------
        // Selection
        // -------------------------------------------------------------------

        public bool SelectCandidate(int index)
        {
            LastError = string.Empty;

            if (index < 0 || index >= candidates.Count)
            {
                LastRejection = FurnitureDetectionRejection.NoCandidateSelected;
                return false;
            }

            SelectedIndex = index;
            LastRejection = FurnitureDetectionRejection.None;
            return true;
        }

        /// <summary>Cycles to the next candidate, wrapping. Convenient for a one-button HUD.</summary>
        public bool SelectNextCandidate()
        {
            if (candidates.Count == 0)
            {
                LastRejection = FurnitureDetectionRejection.NoCandidateSelected;
                return false;
            }

            SelectedIndex = (SelectedIndex + 1) % candidates.Count;
            LastRejection = FurnitureDetectionRejection.None;
            return true;
        }

        // -------------------------------------------------------------------
        // Accept and dismiss
        // -------------------------------------------------------------------

        /// <summary>
        /// Turns the selected candidate into a furniture object of the
        /// currently selected type, with the <b>measured</b> dimensions rather
        /// than the type's MVP defaults.
        ///
        /// <para>Validated by the shared <see cref="FurnitureValidator"/>, the
        /// same rules a hand-placed object faces. The surface is marked resolved
        /// only once validation passes, so a refused candidate can be retried
        /// after the user picks a more suitable type.</para>
        /// </summary>
        public bool TryBuildSelected(
            out SceneObjectModel model,
            out FurnitureDetectionRejection rejection)
        {
            model = null;
            LastError = string.Empty;

            if (Frame == null)
            {
                rejection = Fail(FurnitureDetectionRejection.FloorNotLocked);
                return false;
            }

            if (!HasSelection)
            {
                rejection = Fail(FurnitureDetectionRejection.NoCandidateSelected);
                return false;
            }

            if (!FurnitureValidator.IsSupportedType(SelectedType))
            {
                rejection = Fail(FurnitureDetectionRejection.UnsupportedType);
                return false;
            }

            FurnitureCandidate candidate = candidates[SelectedIndex];

            var built = new SceneObjectModel
            {
                id = Guid.NewGuid().ToString(),
                type = SelectedType,
                center = Vec3Dto.FromVector3(candidate.CenterGhost),
                yawDeg = candidate.YawDeg,
                widthM = candidate.WidthM,
                depthM = candidate.DepthM,
                heightM = candidate.HeightM
            };

            ValidationResult result = FurnitureValidator.Validate(built);

            if (!result.IsValid)
            {
                LastError = result.Error;
                rejection = Fail(FurnitureDetectionRejection.ValidationFailed);
                return false;
            }

            model = built;
            rejection = Succeed();
            return true;
        }

        /// <summary>
        /// Records that a surface has become an object, so a plane that keeps
        /// growing as ARKit observes more of it is not offered a second time.
        /// Called by the workflow once the object is actually in the store.
        /// </summary>
        public void MarkResolved(TrackableId surfaceId)
        {
            resolved.Add(surfaceId);
            RemoveCandidate(surfaceId);
        }

        /// <summary>
        /// Drops the selected candidate without creating anything — a detected
        /// windowsill, a rug edge, a surface the user does not want. It stops
        /// being offered.
        /// </summary>
        public bool DismissSelected()
        {
            LastError = string.Empty;

            if (!HasSelection)
            {
                LastRejection = FurnitureDetectionRejection.NoCandidateSelected;
                return false;
            }

            MarkResolved(candidates[SelectedIndex].SurfaceId);
            LastRejection = FurnitureDetectionRejection.None;
            return true;
        }

        /// <summary>
        /// Forgets every accept/dismiss decision, so dismissed surfaces are
        /// offered again. Does not remove objects already in the store.
        /// </summary>
        public void ClearResolved()
        {
            resolved.Clear();
            LastRejection = FurnitureDetectionRejection.None;
            LastError = string.Empty;
        }

        public bool IsResolved(TrackableId surfaceId) => resolved.Contains(surfaceId);

        // -------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------

        private void RemoveCandidate(TrackableId surfaceId)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!candidates[i].SurfaceId.Equals(surfaceId))
                {
                    continue;
                }

                candidates.RemoveAt(i);

                if (SelectedIndex > i)
                {
                    SelectedIndex--;
                }
                else if (SelectedIndex == i)
                {
                    SelectedIndex = candidates.Count > 0 ? 0 : -1;
                }

                return;
            }
        }

        private void RebuildFootprint()
        {
            footprint.Clear();

            IReadOnlyList<CornerModel> roomCorners = corners.Corners;

            for (int i = 0; i < roomCorners.Count; i++)
            {
                footprint.Add(roomCorners[i].position.ToVector3());
            }
        }

        private FurnitureDetectionRejection Fail(FurnitureDetectionRejection rejection)
        {
            LastRejection = rejection;
            return rejection;
        }

        private FurnitureDetectionRejection Succeed()
        {
            LastRejection = FurnitureDetectionRejection.None;
            return FurnitureDetectionRejection.None;
        }
    }
}
