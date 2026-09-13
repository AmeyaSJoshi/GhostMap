using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// The live AR Foundation implementation of <see cref="ISpatialProvider"/>.
    ///
    /// <para><b>Everything here is read in Unity world space, deliberately.</b>
    /// The AR camera is a child of XR Origin's Camera Offset, whose local Y is
    /// <c>XROrigin.CameraYOffset</c> (1.1176 by default) whenever ARKit reports
    /// <c>TrackingOriginModeFlags.Device</c>, as it does on iPhone. Detected
    /// planes are not children of Camera Offset — they hang off
    /// <c>XROrigin.TrackablesParent</c>. It would be reasonable to fear that the
    /// camera and the planes therefore sit in two spaces a metre apart.</para>
    ///
    /// <para>They do not. <c>XROrigin.OnBeforeRender</c> assigns
    /// <c>TrackablesParent</c> the world pose returned by
    /// <c>GetCameraOriginPose()</c>, which is the pose of the camera's
    /// <i>parent</i> — Camera Offset itself. So the same Y offset is baked into
    /// both, and a raycast hit and the camera position can be compared and
    /// subtracted directly.</para>
    ///
    /// <para>That is what makes the GhostMap frame immune to
    /// <c>CameraYOffset</c>: the frame's origin is the floor hit, and
    /// <c>WorldToGhost</c> subtracts that origin, so any constant offset shared
    /// by both readings cancels exactly. Mixing in a <i>local</i> position —
    /// the camera's <c>localPosition</c>, say, which the S1 diagnostics print as
    /// "Cam L" — would break that, which is why nothing in this class reads
    /// one.</para>
    /// </summary>
    public sealed class ArSpatialProvider : MonoBehaviour, ISpatialProvider
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private Camera arCamera;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        public Camera ArCamera => arCamera;

        public ARSessionState SessionState => ARSession.state;

        public NotTrackingReason NotTrackingReason => ARSession.notTrackingReason;

        public bool IsTrackingGood =>
            ARSession.state == ARSessionState.SessionTracking &&
            ARSession.notTrackingReason == NotTrackingReason.None;

        public Vector2 CenterScreenPoint => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        private void Reset()
        {
            raycastManager = FindFirstObjectByType<ARRaycastManager>();
            arCamera = Camera.main;
        }

        public bool TryGetFloorHit(Vector2 screenPoint, out FloorHit hit)
        {
            hit = default;

            if (raycastManager == null)
            {
                return false;
            }

            hits.Clear();

            // PlaneWithinPolygon, not PlaneWithinInfinity: a plane's estimated
            // extent is the part ARKit has actually observed. Hitting its
            // infinite extension would happily "find floor" through a wall.
            if (!raycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon))
            {
                return false;
            }

            // Raycast returns hits sorted by distance, so the first is nearest.
            ARRaycastHit nearest = hits[0];
            var plane = nearest.trackable as ARPlane;

            hit = new FloorHit(
                nearest.pose.position,
                plane != null ? plane.alignment : PlaneAlignment.None,
                nearest.trackableId);

            return true;
        }

        public Ray GetScreenRay(Vector2 screenPoint)
        {
            return arCamera == null
                ? new Ray(Vector3.zero, Vector3.forward)
                : arCamera.ScreenPointToRay(screenPoint);
        }

        public bool TryGetCameraPose(out Pose pose)
        {
            if (arCamera == null)
            {
                pose = Pose.identity;
                return false;
            }

            Transform cameraTransform = arCamera.transform;
            pose = new Pose(cameraTransform.position, cameraTransform.rotation);
            return true;
        }
    }
}
