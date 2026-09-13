using GhostMap.Scanner.AR;
using GhostMap.Shared.Geometry;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Capture
{
    /// <summary>Why a floor-lock attempt was refused. <see cref="None"/> means it succeeded.</summary>
    public enum FloorLockRejection
    {
        None = 0,

        /// <summary>The session is not tracking, or reports a not-tracking reason.</summary>
        TrackingNotGood,

        /// <summary>There is no AR camera to read a forward direction from.</summary>
        NoCameraPose,

        /// <summary>The center-screen raycast hit no detected plane.</summary>
        NoFloorHit,

        /// <summary>A plane was hit, but it is a wall, a ceiling, or unclassified.</summary>
        PlaneNotHorizontalUp,

        /// <summary>The camera points too close to straight up or down to yield a forward axis.</summary>
        DegenerateForward,

        /// <summary>The frame is already locked. It is immutable; reset instead.</summary>
        AlreadyLocked
    }

    /// <summary>
    /// Establishes the GhostMap coordinate frame, once, from a floor the user
    /// aims at and confirms.
    ///
    /// <para>The frame is built exactly as the shared contract documents on
    /// <see cref="GhostCoordinateFrame"/>:</para>
    /// <code>
    /// up      = Vector3.up
    /// forward = ProjectOnPlane(camera.forward, up).normalized
    /// right   = Cross(up, forward).normalized
    /// origin  = floor hit
    /// </code>
    ///
    /// <para><c>Cross(up, forward)</c> is +X in Unity's left-handed basis —
    /// <c>Cross((0,1,0),(0,0,1)) == (1,0,0)</c> — so the resulting axes satisfy
    /// <c>Cross(right, up) == forward</c>, the same relationship Unity's own
    /// world axes satisfy. Ordering the cross the other way would produce a
    /// mirrored frame in which every captured room came out as its own
    /// reflection, and nothing downstream would notice: areas, lengths and
    /// closure error are all sign-blind.</para>
    ///
    /// <para>Once locked the frame never changes. <see cref="GhostCoordinateFrame"/>
    /// is itself immutable, and this controller refuses a second lock, so no
    /// later camera motion, plane update or XR Origin change can move the
    /// origin out from under coordinates that were already captured against
    /// it.</para>
    ///
    /// <para>This type is plain C# rather than a MonoBehaviour so that every
    /// rule below is testable without a device. <see cref="UI.FloorLockHud"/>
    /// is the scene-facing wrapper.</para>
    /// </summary>
    public sealed class FloorLockController
    {
        /// <summary>
        /// Shortest projected-forward length that still yields a trustworthy
        /// axis. Aiming at the floor tilts the camera down, and in the limit —
        /// the phone held flat, pointing at the user's feet — the projection
        /// onto the horizontal plane collapses toward zero and its direction
        /// becomes whatever rounding noise is left. Normalizing that would spin
        /// the whole room's yaw. In practice a user framing a floor a metre or
        /// more ahead projects around 0.5 to 0.9, so this only rejects an aim
        /// within about a twentieth of a degree of straight down.
        /// </summary>
        public const float MinForwardProjectionMagnitude = 1e-3f;

        private readonly ISpatialProvider provider;

        public FloorLockController(ISpatialProvider provider)
        {
            this.provider = provider;
        }

        /// <summary>The locked frame, or null before the floor is locked.</summary>
        public GhostCoordinateFrame Frame { get; private set; }

        public bool IsLocked => Frame != null;

        /// <summary>Why the most recent <see cref="TryLockFloor"/> was refused.</summary>
        public FloorLockRejection LastRejection { get; private set; }

        /// <summary>
        /// Whether the lock control should be enabled. Tracking quality alone
        /// gates the button; whether the crosshair is actually on a floor is
        /// only known once the attempt runs.
        /// </summary>
        public bool CanLock => !IsLocked && provider.IsTrackingGood;

        /// <summary>
        /// Attempts to lock the floor under the crosshair and establish the
        /// GhostMap frame.
        /// </summary>
        /// <returns>True when the frame was established by this call.</returns>
        public bool TryLockFloor(out FloorLockRejection rejection)
        {
            rejection = Evaluate(out GhostCoordinateFrame frame);
            LastRejection = rejection;

            if (rejection != FloorLockRejection.None)
            {
                return false;
            }

            Frame = frame;
            return true;
        }

        private FloorLockRejection Evaluate(out GhostCoordinateFrame frame)
        {
            frame = null;

            if (IsLocked)
            {
                return FloorLockRejection.AlreadyLocked;
            }

            if (!provider.IsTrackingGood)
            {
                return FloorLockRejection.TrackingNotGood;
            }

            if (!provider.TryGetCameraPose(out Pose cameraPose))
            {
                return FloorLockRejection.NoCameraPose;
            }

            if (!provider.TryGetFloorHit(provider.CenterScreenPoint, out FloorHit hit))
            {
                return FloorLockRejection.NoFloorHit;
            }

            // HorizontalUp only. A wall is HorizontalDown-adjacent at best and
            // Vertical at worst, and a ceiling is HorizontalDown; locking
            // either would put the room's origin above the user's head and
            // invert which way corners lie.
            if (hit.Alignment != PlaneAlignment.HorizontalUp)
            {
                return FloorLockRejection.PlaneNotHorizontalUp;
            }

            return TryBuildFrame(hit.WorldPosition, cameraPose.forward, out frame)
                ? FloorLockRejection.None
                : FloorLockRejection.DegenerateForward;
        }

        /// <summary>
        /// Builds the frame from a world-space floor point and the world-space
        /// camera forward at the moment of the lock. Both arguments must come
        /// from the same world space; see <see cref="ArSpatialProvider"/>.
        /// </summary>
        public static bool TryBuildFrame(
            Vector3 floorWorldOrigin,
            Vector3 cameraWorldForward,
            out GhostCoordinateFrame frame)
        {
            frame = null;

            Vector3 up = Vector3.up;
            Vector3 projected = Vector3.ProjectOnPlane(cameraWorldForward, up);

            if (projected.magnitude < MinForwardProjectionMagnitude)
            {
                return false;
            }

            Vector3 forward = projected.normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            frame = new GhostCoordinateFrame(floorWorldOrigin, right, up, forward);
            return true;
        }
    }
}
