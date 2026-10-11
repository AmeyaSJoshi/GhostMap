using System.Collections.Generic;
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
    /// One detected AR plane, reduced to an oriented rectangle.
    ///
    /// <para>Introduced by <c>ADR-0006</c> for furniture detection. It exists
    /// for the same reason <see cref="FloorHit"/> does: <c>ARPlane</c> is a
    /// <c>MonoBehaviour</c> on a trackable owned by a live plane subsystem and
    /// cannot be constructed in an EditMode test, which would push every
    /// detection rule onto a phone to verify.</para>
    ///
    /// <para><see cref="WorldCenter"/> is the plane's centre in Unity world
    /// space — <c>plane.transform.TransformPoint(plane.center)</c>, not
    /// <c>transform.position</c>, because ARKit's observed centre drifts within
    /// the plane's own space as it sees more of the surface.</para>
    /// </summary>
    public readonly struct DetectedSurface
    {
        public DetectedSurface(
            TrackableId id,
            Vector3 worldCenter,
            Quaternion worldRotation,
            Vector2 extentsM,
            PlaneAlignment alignment,
            PlaneClassifications classifications = PlaneClassifications.None)
        {
            Id = id;
            WorldCenter = worldCenter;
            WorldRotation = worldRotation;
            ExtentsM = extentsM;
            Alignment = alignment;
            Classifications = classifications;
        }

        /// <summary>
        /// Stable for the lifetime of the plane, which is what lets an accepted
        /// surface be remembered so a growing plane is not added twice.
        /// </summary>
        public TrackableId Id { get; }

        public Vector3 WorldCenter { get; }

        /// <summary>
        /// The plane's world rotation. Its local X and Z span the surface;
        /// local Y is the normal.
        /// </summary>
        public Quaternion WorldRotation { get; }

        /// <summary>
        /// Observed extent in meters: <c>x</c> along the plane's local X,
        /// <c>y</c> along its local Z. This is the part of the surface ARKit has
        /// actually seen, which is a lower bound on the real object.
        /// </summary>
        public Vector2 ExtentsM { get; }

        public PlaneAlignment Alignment { get; }

        /// <summary>
        /// ARKit's own label for the surface (<c>ADR-0007</c>): Table, Seat,
        /// Floor and so on, or None while ARKit has not decided yet. ARKit
        /// labels planes on A12 and later devices without LiDAR.
        /// </summary>
        public PlaneClassifications Classifications { get; }
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

        /// <summary>
        /// Every currently detected plane, as oriented rectangles.
        /// <c>ADR-0006</c> furniture detection reads horizontal ones; the
        /// caller filters by <see cref="DetectedSurface.Alignment"/>.
        ///
        /// <para>Fills <paramref name="into"/> rather than allocating, because
        /// the HUD refreshes this while the user is looking around.</para>
        /// </summary>
        /// <returns>False when plane detection is unavailable.</returns>
        bool TryGetDetectedSurfaces(List<DetectedSurface> into);
    }
}
