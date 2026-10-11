using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

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
    /// <para><b>Demo mode.</b> In the Unity Editor and the iOS Simulator there
    /// is no ARKit, so this provider answers from a <see cref="SimulatedRoom"/>
    /// instead. <see cref="SimulatedRoom.ShouldSimulate"/> never selects it on
    /// a physical iPhone.</para>
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
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera arCamera;

        /// <summary>A fixed id for the demo floor, so repeated hits read as one plane.</summary>
        private static readonly TrackableId SimulatedFloorId = new TrackableId(0x6768_6f73_7400_0001, 1);

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        private SimulatedRoom simulatedRoom;

        public Camera ArCamera => arCamera;

        /// <summary>True when answering from the demo room rather than ARKit.</summary>
        public bool IsSimulated => simulatedRoom != null;

        public ARSessionState SessionState =>
            IsSimulated
                ? (simulatedRoom.IsTrackingGood ? ARSessionState.SessionTracking : ARSessionState.SessionInitializing)
                : ARSession.state;

        public NotTrackingReason NotTrackingReason =>
            IsSimulated
                ? (simulatedRoom.IsTrackingGood ? NotTrackingReason.None : NotTrackingReason.Initializing)
                : ARSession.notTrackingReason;

        public bool IsTrackingGood =>
            IsSimulated
                ? simulatedRoom.IsTrackingGood
                : ARSession.state == ARSessionState.SessionTracking &&
                  ARSession.notTrackingReason == NotTrackingReason.None;

        public Vector2 CenterScreenPoint => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        private void Reset()
        {
            raycastManager = FindFirstObjectByType<ARRaycastManager>();
            planeManager = FindFirstObjectByType<ARPlaneManager>();
            arCamera = Camera.main;
        }

        private void Awake()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance != null
                ? XRGeneralSettings.Instance.Manager
                : null;

            bool hasActiveLoader = manager != null && manager.activeLoader != null;

            if (arCamera != null
                && SimulatedRoom.ShouldSimulate(Application.isEditor, SimulatedRoom.IsRunningInIosSimulator(), hasActiveLoader))
            {
                // With no XR loader the AR session can only fail; stop it
                // trying, so it logs nothing in a mode where that is expected.
                ARSession session = FindFirstObjectByType<ARSession>();
                if (session != null)
                {
                    session.enabled = false;
                }

                var demoGo = new GameObject("Demo Mode (no ARKit)");
                simulatedRoom = demoGo.AddComponent<SimulatedRoom>();
                simulatedRoom.Initialize(arCamera);

                Debug.Log("GhostMap: no ARKit here, so the scanner is running in demo mode with a virtual room.");
            }
        }

        public bool TryGetFloorHit(Vector2 screenPoint, out FloorHit hit)
        {
            hit = default;

            if (IsSimulated)
            {
                if (!SimulatedRoom.TryRaycastFloor(GetScreenRay(screenPoint), out Vector3 floorPoint))
                {
                    return false;
                }

                hit = new FloorHit(floorPoint, PlaneAlignment.HorizontalUp, SimulatedFloorId);
                return true;
            }

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

        /// <summary>
        /// ADR-0006: enumerates detected planes for furniture detection.
        ///
        /// <para>This is the <b>second</b> use of plane detection in GhostMap,
        /// and the only one besides the floor lock. It does not weaken
        /// <c>ADR-0004</c>: that rejected depending on detection of blank
        /// <i>vertical</i> walls, which is slow and partial on a non-Pro
        /// device. Horizontal furniture surfaces are the favourable case —
        /// textured, lit, seen from above — and are exactly what the
        /// already-device-verified floor lock relies on.</para>
        ///
        /// <para>The world centre is <c>TransformPoint(plane.center)</c> rather
        /// than <c>transform.position</c>: ARKit refines the observed centre
        /// within the plane's own space as it sees more of the surface, and
        /// <c>plane.center</c> is where that refinement lands.</para>
        /// </summary>
        public bool TryGetDetectedSurfaces(List<DetectedSurface> into)
        {
            if (into == null)
            {
                return false;
            }

            into.Clear();

            if (planeManager == null || planeManager.trackables.count == 0)
            {
                return planeManager != null;
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane == null)
                {
                    continue;
                }

                Transform planeTransform = plane.transform;

                into.Add(new DetectedSurface(
                    plane.trackableId,
                    planeTransform.TransformPoint(plane.center),
                    planeTransform.rotation,
                    plane.size,
                    plane.alignment,
                    plane.classifications));
            }

            return true;
        }
    }
}
