using System.Text;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// The Task S2 scene-facing shell: a center crosshair, a Lock Floor button
    /// that is disabled while tracking is poor, and the readout that makes the
    /// GhostMap frame observable on a phone.
    ///
    /// <para>All logic lives in <see cref="FloorLockController"/> and
    /// <see cref="ScanWorkflowController"/>. This class only reads them and
    /// forwards one button press, so nothing here needs a device to be
    /// trusted.</para>
    ///
    /// <para>The readout is built for the S2 hardware test. It prints the frame
    /// once it is locked and never again recomputes it, prints the live camera
    /// position in both world and Ghost space so the two can be compared while
    /// walking, and prints the Ghost position of whatever the crosshair
    /// currently rests on — which must read near zero when the crosshair
    /// returns to the spot the floor was locked at.</para>
    /// </summary>
    public sealed class FloorLockHud : MonoBehaviour
    {
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button lockFloorButton;
        [SerializeField] private Graphic crosshair;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();

        private FloorLockController floorLock;
        private CornerCaptureController cornerCapture;
        private HeightCaptureController heightCapture;
        private OpeningCaptureController openingCapture;
        private ObjectPlacementController objectPlacement;
        private ScanWorkflowController workflow;

        private FloorLockRejection lastAttemptRejection = FloorLockRejection.None;
        private bool hasAttempted;

        /// <summary>
        /// This component is the scene's composition root: it owns the one
        /// <see cref="ScanWorkflowController"/> every other HUD reads. Task S3's
        /// <see cref="CornerCaptureHud"/> takes it from here rather than
        /// building a second one, because two workflows would mean two
        /// revisions and two rooms.
        /// </summary>
        public ScanWorkflowController Workflow => workflow;

        private void Awake()
        {
            BuildWorkflow();

            if (lockFloorButton != null)
            {
                lockFloorButton.onClick.AddListener(OnLockFloorPressed);
            }
        }

        /// <summary>
        /// Task S6's Reset: discards the entire controller graph and rebuilds
        /// it from scratch, exactly as <see cref="Awake"/> did. A fresh
        /// <see cref="ScanWorkflowController"/> means a fresh session id, a
        /// fresh room id, phase <see cref="ScanPhase.Boot"/> and revision 0 —
        /// implementation plan section 10's "Reset starts a new AR session and
        /// new session ID" for the scan's logical state.
        ///
        /// <para>The actual AR tracking session is deliberately left alone:
        /// tearing down ARKit tracking to reset scan bookkeeping would cost the
        /// user their tracking quality for no reason. Every other HUD already
        /// reads <see cref="Workflow"/> freshly every frame rather than caching
        /// it (see the class remarks on <see cref="CornerCaptureHud"/>), so
        /// swapping the instance here is enough for the whole scene to pick up
        /// the reset on the next frame.</para>
        /// </summary>
        public void ResetScan()
        {
            BuildWorkflow();
        }

        private void BuildWorkflow()
        {
            floorLock = new FloorLockController(spatialProvider);
            cornerCapture = new CornerCaptureController(spatialProvider, floorLock);
            heightCapture = new HeightCaptureController(spatialProvider, floorLock, cornerCapture);
            openingCapture = new OpeningCaptureController(spatialProvider, floorLock, cornerCapture, heightCapture);
            objectPlacement = new ObjectPlacementController(spatialProvider, floorLock);
            workflow = new ScanWorkflowController(
                floorLock, cornerCapture, heightCapture, openingCapture, objectPlacement);

            hasAttempted = false;
            lastAttemptRejection = FloorLockRejection.None;
        }

        private void OnDestroy()
        {
            if (lockFloorButton != null)
            {
                lockFloorButton.onClick.RemoveListener(OnLockFloorPressed);
            }
        }

        private void Update()
        {
            if (spatialProvider == null)
            {
                return;
            }

            workflow.Tick(spatialProvider.IsTrackingGood);

            if (lockFloorButton != null)
            {
                lockFloorButton.interactable =
                    workflow.Phase == ScanPhase.FindFloor && floorLock.CanLock;
            }

            if (crosshair != null)
            {
                // Green once the frame exists, amber while a lock is possible,
                // grey while tracking is too poor to try.
                crosshair.color = floorLock.IsLocked
                    ? Color.green
                    : floorLock.CanLock
                        ? new Color(1f, 0.75f, 0.2f)
                        : new Color(0.6f, 0.6f, 0.6f);
            }

            if (readoutText != null)
            {
                readoutText.text = BuildReadout();
            }
        }

        private void OnLockFloorPressed()
        {
            hasAttempted = true;
            workflow.TryLockFloor(out lastAttemptRejection);
        }

        private string BuildReadout()
        {
            builder.Clear();

            builder.Append("Phase: ").Append(workflow.Phase)
                .Append(" | rev ").Append(workflow.Revision).Append('\n');

            builder.Append("Session: ").Append(spatialProvider.SessionState)
                .Append(" | reason: ").Append(spatialProvider.NotTrackingReason).Append('\n');

            builder.Append("Can lock: ").Append(floorLock.CanLock ? "YES" : "no");
            if (hasAttempted)
            {
                builder.Append(" | last attempt: ").Append(lastAttemptRejection);
            }

            builder.Append('\n');

            AppendCrosshairLine(builder);
            AppendFrameLines(builder);
            AppendCameraLines(builder);

            return builder.ToString();
        }

        private void AppendCrosshairLine(StringBuilder target)
        {
            if (!spatialProvider.TryGetFloorHit(spatialProvider.CenterScreenPoint, out FloorHit hit))
            {
                target.Append("Crosshair: no plane\n");
                return;
            }

            target.Append("Crosshair: ").Append(hit.Alignment)
                .AppendFormat(
                    " W({0:F2},{1:F2},{2:F2})",
                    hit.WorldPosition.x, hit.WorldPosition.y, hit.WorldPosition.z);

            GhostCoordinateFrame frame = workflow.Frame;
            if (frame != null)
            {
                Vector3 ghost = frame.WorldToGhost(hit.WorldPosition);
                target.AppendFormat(" G({0:F2},{1:F2},{2:F2})", ghost.x, ghost.y, ghost.z);
            }

            target.Append('\n');
        }

        private void AppendFrameLines(StringBuilder target)
        {
            GhostCoordinateFrame frame = workflow.Frame;
            if (frame == null)
            {
                target.Append("Frame: not locked\n");
                return;
            }

            target.AppendFormat(
                "Frame O W({0:F3},{1:F3},{2:F3}) floorY {3:F3}\n",
                frame.Origin.x, frame.Origin.y, frame.Origin.z, frame.FloorWorldY);

            target.AppendFormat(
                "Frame X({0:F2},{1:F2},{2:F2}) Z({3:F2},{4:F2},{5:F2})\n",
                frame.Right.x, frame.Right.y, frame.Right.z,
                frame.Forward.x, frame.Forward.y, frame.Forward.z);

            // +1 for a frame with Unity's own handedness, -1 for a mirrored
            // one. Printed rather than asserted so a wrong build is visible on
            // the device rather than silently producing reflected rooms.
            float handedness = Vector3.Dot(Vector3.Cross(frame.Right, frame.Up), frame.Forward);
            target.AppendFormat("Handedness: {0:F3} (expect +1.000)\n", handedness);
        }

        private void AppendCameraLines(StringBuilder target)
        {
            if (!spatialProvider.TryGetCameraPose(out Pose cameraPose))
            {
                target.Append("Cam: no camera\n");
                return;
            }

            target.AppendFormat(
                "Cam W({0:F2},{1:F2},{2:F2})",
                cameraPose.position.x, cameraPose.position.y, cameraPose.position.z);

            GhostCoordinateFrame frame = workflow.Frame;
            if (frame != null)
            {
                Vector3 ghost = frame.WorldToGhost(cameraPose.position);
                target.AppendFormat(" G({0:F2},{1:F2},{2:F2})", ghost.x, ghost.y, ghost.z);
            }

            target.Append('\n');
        }
    }
}
