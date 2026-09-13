using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace GhostMap.Scanner.Bootstrap
{
    /// <summary>
    /// Task S1 physical-device smoke test readout, and the bring-up
    /// instrumentation for it.
    ///
    /// The readout deliberately separates every link in the chain that carries
    /// an ARKit pose to the AR camera's Transform, because each one fails in a
    /// way that looks identical from the outside — a camera that never moves:
    ///
    /// <list type="number">
    /// <item><description>the XR loader initializes (XR block);</description></item>
    /// <item><description>the XRInputSubsystem runs (XRInput);</description></item>
    /// <item><description>the XR head node reports a tracked pose, read straight
    /// from the subsystem and bypassing the Input System (Head);</description></item>
    /// <item><description>the Input System backend exists at all, without which
    /// TrackedPoseDriver resolves no controls (InputSys);</description></item>
    /// <item><description>TrackedPoseDriver writes the camera's LOCAL pose
    /// (Cam L, and the moved/Δ counters that prove it frame to frame);</description></item>
    /// <item><description>XR Origin and Camera Offset turn that into the WORLD
    /// pose everything downstream uses (Origin W, Offset W, Cam W).</description></item>
    /// </list>
    ///
    /// Read top to bottom: the first line that looks wrong is where tracking
    /// stops propagating.
    /// </summary>
    public sealed class ScannerBootstrap : MonoBehaviour
    {
        [SerializeField] private ARSession arSession;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Text diagnosticsText;

        private readonly StringBuilder builder = new StringBuilder();
        private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
        private readonly List<XRNodeState> nodeStates = new List<XRNodeState>();

        private XROrigin xrOrigin;
        private Transform cameraOffset;

        private Vector3 previousCameraLocalPosition;
        private Quaternion previousCameraLocalRotation = Quaternion.identity;
        private bool hasPreviousCameraPose;
        private int framesCameraPoseChanged;
        private float maxPositionDelta;
        private float maxRotationDelta;

        private void Reset()
        {
            arSession = FindFirstObjectByType<ARSession>();
            planeManager = FindFirstObjectByType<ARPlaneManager>();
            arCamera = Camera.main;
        }

        private void Awake()
        {
            // Resolved here rather than serialized so that this instrumentation
            // needs no change to the scene asset.
            xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null)
            {
                cameraOffset = xrOrigin.CameraFloorOffsetObject.transform;
            }
        }

        private void Update()
        {
            TrackCameraPoseChanges();

            if (diagnosticsText == null)
            {
                return;
            }

            builder.Clear();
            AppendXrDiagnostics(builder);
            AppendSessionDiagnostics(builder);
            AppendTrackingChainDiagnostics(builder);
            AppendPlaneDiagnostics(builder);

            diagnosticsText.text = builder.ToString();
        }

        /// <summary>
        /// Proves, rather than assumes, whether the AR camera's own Transform is
        /// changing: counts the frames in which its local pose actually moved
        /// and keeps the largest deltas seen. A frozen camera reports 0 frames
        /// and 0 deltas no matter how the phone is waved around.
        /// </summary>
        private void TrackCameraPoseChanges()
        {
            if (arCamera == null)
            {
                return;
            }

            Transform cameraTransform = arCamera.transform;
            Vector3 localPosition = cameraTransform.localPosition;
            Quaternion localRotation = cameraTransform.localRotation;

            if (hasPreviousCameraPose)
            {
                float positionDelta = Vector3.Distance(localPosition, previousCameraLocalPosition);
                float rotationDelta = Quaternion.Angle(localRotation, previousCameraLocalRotation);

                if (positionDelta > 0f || rotationDelta > 0f)
                {
                    framesCameraPoseChanged++;
                }

                maxPositionDelta = Mathf.Max(maxPositionDelta, positionDelta);
                maxRotationDelta = Mathf.Max(maxRotationDelta, rotationDelta);
            }

            previousCameraLocalPosition = localPosition;
            previousCameraLocalRotation = localRotation;
            hasPreviousCameraPose = true;
        }

        private void AppendXrDiagnostics(StringBuilder target)
        {
#if UNITY_XR_ARKIT_LOADER_ENABLED
            const string arkitNative = "in";
#else
            const string arkitNative = "MISSING";
#endif

#if ENABLE_INPUT_SYSTEM
            const string inputSystemBackend = "ON";
#else
            const string inputSystemBackend = "OFF (no XR input device can exist)";
#endif

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            XRManagerSettings manager = settings != null ? settings.Manager : null;

            if (manager == null)
            {
                target.Append("XR settings: ").Append(settings == null ? "not in build" : "present, no manager")
                    .Append('\n');
                target.Append("Configured loaders: unknown\nActive loader: none\n");
            }
            else
            {
                target.Append("XR settings: present (init on start: ")
                    .Append(settings.InitManagerOnStart ? "yes" : "no")
                    .Append(", init complete: ")
                    .Append(manager.isInitializationComplete ? "yes" : "no")
                    .Append(")\n");

                target.Append("Configured loaders: [");
                IReadOnlyList<XRLoader> loaders = manager.activeLoaders;
                for (int i = 0; i < loaders.Count; i++)
                {
                    if (i > 0)
                    {
                        target.Append(", ");
                    }

                    target.Append(loaders[i] == null ? "<null>" : loaders[i].GetType().Name);
                }

                target.Append("]\n");

                target.Append("Active loader: ")
                    .Append(manager.activeLoader == null ? "none" : manager.activeLoader.GetType().Name)
                    .Append('\n');
            }

            target.Append("ARKit native: ").Append(arkitNative)
                .Append(" | InputSys: ").Append(inputSystemBackend).Append('\n');
        }

        private void AppendSessionDiagnostics(StringBuilder target)
        {
            target.Append("Session: ").Append(ARSession.state)
                .Append(" | reason: ").Append(ARSession.notTrackingReason).Append('\n');

            inputSubsystems.Clear();
            SubsystemManager.GetSubsystems(inputSubsystems);
            if (inputSubsystems.Count == 0)
            {
                target.Append("XRInput: no subsystem\n");
            }
            else
            {
                target.Append("XRInput: ");
                for (int i = 0; i < inputSubsystems.Count; i++)
                {
                    if (i > 0)
                    {
                        target.Append(", ");
                    }

                    target.Append(inputSubsystems[i].running ? "running" : "NOT running");
                }

                target.Append('\n');
            }

            // Read straight from the XR subsystem, bypassing the Input System.
            // A tracked head node here alongside a frozen camera means the pose
            // exists and it is the Input System layer that is not delivering it.
            nodeStates.Clear();
            InputTracking.GetNodeStates(nodeStates);
            bool foundHead = false;
            foreach (XRNodeState nodeState in nodeStates)
            {
                if (nodeState.nodeType != XRNode.Head && nodeState.nodeType != XRNode.CenterEye)
                {
                    continue;
                }

                foundHead = true;
                target.Append("Head node: ").Append(nodeState.tracked ? "tracked" : "NOT tracked");
                if (nodeState.TryGetPosition(out Vector3 nodePosition))
                {
                    target.AppendFormat(" p({0:F2},{1:F2},{2:F2})", nodePosition.x, nodePosition.y, nodePosition.z);
                }

                target.Append('\n');
                break;
            }

            if (!foundHead)
            {
                target.Append("Head node: none reported\n");
            }
        }

        private void AppendTrackingChainDiagnostics(StringBuilder target)
        {
            AppendPose(target, "Origin W", xrOrigin != null ? xrOrigin.transform : null, world: true);
            AppendPose(target, "Offset W", cameraOffset, world: true);
            AppendPose(target, "Cam L", arCamera != null ? arCamera.transform : null, world: false);
            AppendPose(target, "Cam W", arCamera != null ? arCamera.transform : null, world: true);

            target.Append("Cam moved: ").Append(framesCameraPoseChanged).Append(" frames")
                .AppendFormat(" (max dp {0:F3}m, dr {1:F1}deg)", maxPositionDelta, maxRotationDelta)
                .Append('\n');
        }

        private static void AppendPose(StringBuilder target, string label, Transform source, bool world)
        {
            target.Append(label).Append(": ");
            if (source == null)
            {
                target.Append("missing\n");
                return;
            }

            Vector3 position = world ? source.position : source.localPosition;
            Vector3 eulerAngles = world ? source.eulerAngles : source.localEulerAngles;

            target.AppendFormat(
                "p({0:F2},{1:F2},{2:F2}) r({3:F0},{4:F0},{5:F0})\n",
                position.x, position.y, position.z, eulerAngles.x, eulerAngles.y, eulerAngles.z);
        }

        private void AppendPlaneDiagnostics(StringBuilder target)
        {
            int planeCount = 0;
            bool hasFloorCandidate = false;
            if (planeManager != null)
            {
                foreach (ARPlane plane in planeManager.trackables)
                {
                    planeCount++;
                    if (plane.alignment == PlaneAlignment.HorizontalUp)
                    {
                        hasFloorCandidate = true;
                    }
                }
            }

            target.Append("Planes: ").Append(planeCount)
                .Append(" (floor candidate: ").Append(hasFloorCandidate ? "yes" : "no").Append(')');
        }
    }
}
