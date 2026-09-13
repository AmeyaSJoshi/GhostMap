using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// One center-screen AR raycast result, reduced to the facts floor lock
    /// needs.
    ///
    /// This exists instead of passing <c>ARRaycastHit</c> around because
    /// <c>ARRaycastHit</c> cannot be constructed in an EditMode test — it needs
    /// a real <c>ARPlane</c> trackable owned by a live plane subsystem — which
    /// would make <see cref="ISpatialProvider"/> unmockable and push every
    /// floor-lock rule onto a physical device to verify.
    ///
    /// <see cref="WorldPosition"/> is in Unity world space, the same space the
    /// AR camera's <c>transform.position</c> is in. See
    /// <see cref="ArSpatialProvider"/> for why that matters.
    /// </summary>
    public readonly struct FloorHit
    {
        public FloorHit(Vector3 worldPosition, PlaneAlignment alignment, TrackableId planeId)
        {
            WorldPosition = worldPosition;
            Alignment = alignment;
            PlaneId = planeId;
        }

        public Vector3 WorldPosition { get; }

        /// <summary>Only <see cref="PlaneAlignment.HorizontalUp"/> may be locked as a floor.</summary>
        public PlaneAlignment Alignment { get; }

        public TrackableId PlaneId { get; }
    }

    /// <summary>
    /// The scanner's seam onto AR Foundation. Everything that touches
    /// <c>ARSession</c>, <c>ARRaycastManager</c> or the AR camera goes through
    /// here, so capture logic can be tested without a device.
    ///
    /// Every position and direction returned is in Unity **world** space.
    /// </summary>
    public interface ISpatialProvider
    {
        /// <summary>
        /// True only when the session is tracking and reports no
        /// not-tracking reason. Capture is blocked whenever this is false.
        /// </summary>
        bool IsTrackingGood { get; }

        /// <summary>The crosshair position: the center of the screen.</summary>
        Vector2 CenterScreenPoint { get; }

        /// <summary>
        /// Raycasts the screen point against detected planes, within their
        /// polygon rather than their infinite extent.
        /// </summary>
        /// <returns>False when nothing was hit.</returns>
        bool TryGetFloorHit(Vector2 screenPoint, out FloorHit hit);

        /// <summary>A world-space ray from the camera through the screen point.</summary>
        Ray GetScreenRay(Vector2 screenPoint);

        /// <summary>The AR camera's world pose. False when there is no camera.</summary>
        bool TryGetCameraPose(out Pose pose);
    }
}
