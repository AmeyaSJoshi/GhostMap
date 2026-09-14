using System.Collections.Generic;
using System.Text;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// The Task S3 scene-facing shell: one context-sensitive primary button,
    /// Undo and Redo, a readout, and a world-space marker on every captured
    /// corner.
    ///
    /// <para>All logic lives in <see cref="CornerCaptureController"/> and
    /// <see cref="ScanWorkflowController"/>. This class reads them, forwards
    /// three button presses and draws markers, so nothing here needs a device
    /// to be trusted.</para>
    ///
    /// <para><see cref="FloorLockHud"/> is the scene's composition root — it
    /// constructs the controllers in <c>Awake</c> — so the workflow is read
    /// lazily here rather than cached in this component's own <c>Awake</c>.
    /// Unity does not order <c>Awake</c> across GameObjects, and caching it
    /// would be a null reference that appeared only sometimes.</para>
    ///
    /// <para>The markers exist for the Task S3 physical test: a corner that
    /// looks right in the readout but lands in the wrong place in the room is
    /// exactly the failure this catches. They are drawn at
    /// <c>frame.GhostToWorld(corner)</c>, so a marker sitting on the real
    /// corner also demonstrates the Ghost round trip on hardware.</para>
    /// </summary>
    public sealed class CornerCaptureHud : MonoBehaviour
    {
        private const float CornerMarkerDiameterM = 0.09f;
        private const float ClosureMarkerDiameterM = 0.07f;

        private static readonly Color CornerMarkerColor = new Color(0.20f, 0.85f, 0.35f);
        private static readonly Color FirstCornerMarkerColor = new Color(0.25f, 0.65f, 1f);
        private static readonly Color ClosureMarkerColor = new Color(1f, 0.45f, 0.15f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button primaryButton;
        [SerializeField] private Text primaryButtonLabel;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button redoButton;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();
        private readonly List<GameObject> cornerMarkers = new List<GameObject>();

        private GameObject closureMarker;

        private void Awake()
        {
            if (primaryButton != null)
            {
                primaryButton.onClick.AddListener(OnPrimaryPressed);
            }

            if (undoButton != null)
            {
                undoButton.onClick.AddListener(OnUndoPressed);
            }

            if (redoButton != null)
            {
                redoButton.onClick.AddListener(OnRedoPressed);
            }
        }

        private void OnDestroy()
        {
            if (primaryButton != null)
            {
                primaryButton.onClick.RemoveListener(OnPrimaryPressed);
            }

            if (undoButton != null)
            {
                undoButton.onClick.RemoveListener(OnUndoPressed);
            }

            if (redoButton != null)
            {
                redoButton.onClick.RemoveListener(OnRedoPressed);
            }
        }

        private ScanWorkflowController Workflow => floorLockHud != null ? floorLockHud.Workflow : null;

        private void Update()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null || spatialProvider == null)
            {
                return;
            }

            UpdateControls(workflow);
            UpdateMarkers(workflow);

            // The world-space corner/closure markers stay drawn in every later
            // phase so device verification can still see the S2 frame and S3
            // footprint hold still (see the S4/S5 handoffs' device-test
            // procedures); only the readout text — redundant once corner
            // capture is done — is cleared, the same way the S4/S5 HUDs
            // already clear theirs outside their own phase.
            bool inCornerPhase =
                workflow.Phase == ScanPhase.CaptureCorners || workflow.Phase == ScanPhase.VerifyClosure;

            if (readoutText != null)
            {
                readoutText.text = inCornerPhase ? BuildReadout(workflow) : string.Empty;
            }
        }

        // -------------------------------------------------------------------
        // Controls
        // -------------------------------------------------------------------

        private void UpdateControls(ScanWorkflowController workflow)
        {
            CornerCaptureController capture = workflow.Corners;

            bool canStart = workflow.Phase == ScanPhase.FloorLocked;
            bool canCapture = workflow.Phase == ScanPhase.CaptureCorners && capture.CanCapture;
            bool canVerify = workflow.Phase == ScanPhase.VerifyClosure && capture.CanVerifyClosure;

            if (primaryButton != null)
            {
                primaryButton.gameObject.SetActive(
                    canStart || workflow.Phase == ScanPhase.CaptureCorners
                    || workflow.Phase == ScanPhase.VerifyClosure);

                primaryButton.interactable = canStart || canCapture || canVerify;
            }

            if (primaryButtonLabel != null)
            {
                primaryButtonLabel.text = PrimaryLabel(workflow);
            }

            if (undoButton != null)
            {
                undoButton.gameObject.SetActive(
                    workflow.Phase == ScanPhase.CaptureCorners
                    || workflow.Phase == ScanPhase.VerifyClosure);

                undoButton.interactable = capture.CornerCount > 0;
            }

            // Redo only appears once there is something to redo: a measured
            // closure the user might want to reject, or one already rejected.
            if (redoButton != null)
            {
                redoButton.gameObject.SetActive(
                    workflow.Phase == ScanPhase.VerifyClosure
                        ? capture.HasClosureMeasurement
                        : workflow.Phase == ScanPhase.CaptureHeight);
            }
        }

        private static string PrimaryLabel(ScanWorkflowController workflow)
        {
            CornerCaptureController capture = workflow.Corners;

            switch (workflow.Phase)
            {
                case ScanPhase.FloorLocked:
                    return "Start Corners";

                case ScanPhase.CaptureCorners:
                    return $"Capture Corner {capture.CornerCount + 1}/{CornerCaptureController.RequiredCornerCount}";

                case ScanPhase.VerifyClosure:
                    return "Verify First Corner";

                default:
                    return "—";
            }
        }

        private void OnPrimaryPressed()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null)
            {
                return;
            }

            switch (workflow.Phase)
            {
                case ScanPhase.FloorLocked:
                    workflow.BeginCornerCapture();
                    break;

                case ScanPhase.CaptureCorners:
                    workflow.TryCaptureCorner(out _);
                    break;

                case ScanPhase.VerifyClosure:
                    workflow.TryVerifyClosure(out _, out _);
                    break;
            }
        }

        private void OnUndoPressed() => Workflow?.TryUndoCorner(out _);

        private void OnRedoPressed() => Workflow?.RedoCorners();

        // -------------------------------------------------------------------
        // Markers
        // -------------------------------------------------------------------

        private void UpdateMarkers(ScanWorkflowController workflow)
        {
            GhostCoordinateFrame frame = workflow.Frame;
            CornerCaptureController capture = workflow.Corners;

            if (frame == null)
            {
                ClearMarkers();
                return;
            }

            IReadOnlyList<CornerModel> corners = capture.Corners;

            while (cornerMarkers.Count > corners.Count)
            {
                int last = cornerMarkers.Count - 1;
                Destroy(cornerMarkers[last]);
                cornerMarkers.RemoveAt(last);
            }

            while (cornerMarkers.Count < corners.Count)
            {
                cornerMarkers.Add(CreateMarker($"Corner {cornerMarkers.Count + 1}", CornerMarkerDiameterM));
            }

            for (int i = 0; i < corners.Count; i++)
            {
                // The first corner is the one the user re-aims at, so it is the
                // one that has to be findable across the room.
                SetMarker(
                    cornerMarkers[i],
                    frame.GhostToWorld(corners[i].position.ToVector3()),
                    i == 0 ? FirstCornerMarkerColor : CornerMarkerColor);
            }

            UpdateClosureMarker(frame, capture);
        }

        private void UpdateClosureMarker(GhostCoordinateFrame frame, CornerCaptureController capture)
        {
            if (!capture.HasClosureMeasurement)
            {
                if (closureMarker != null)
                {
                    Destroy(closureMarker);
                    closureMarker = null;
                }

                return;
            }

            if (closureMarker == null)
            {
                closureMarker = CreateMarker("Closure Point", ClosureMarkerDiameterM);
            }

            SetMarker(closureMarker, frame.GhostToWorld(capture.ClosurePointGhost), ClosureMarkerColor);
        }

        private GameObject CreateMarker(string markerName, float diameterM)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(transform, worldPositionStays: false);
            marker.transform.localScale = Vector3.one * diameterM;

            // A collider on a marker would sit in front of the crosshair and
            // could intercept a later physics raycast. Nothing needs it.
            Collider collider = marker.GetComponent<Collider>();

            if (collider != null)
            {
                Destroy(collider);
            }

            return marker;
        }

        private static void SetMarker(GameObject marker, Vector3 worldPosition, Color color)
        {
            marker.transform.position = worldPosition;

            var renderer = marker.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }

        private void ClearMarkers()
        {
            for (int i = 0; i < cornerMarkers.Count; i++)
            {
                Destroy(cornerMarkers[i]);
            }

            cornerMarkers.Clear();

            if (closureMarker != null)
            {
                Destroy(closureMarker);
                closureMarker = null;
            }
        }

        // -------------------------------------------------------------------
        // Readout
        // -------------------------------------------------------------------

        private string BuildReadout(ScanWorkflowController workflow)
        {
            CornerCaptureController capture = workflow.Corners;

            builder.Clear();

            builder.Append("Corners ").Append(capture.CornerCount)
                .Append('/').Append(CornerCaptureController.RequiredCornerCount);

            if (workflow.Phase == ScanPhase.VerifyClosure)
            {
                builder.Append(" | re-aim at corner 1");
            }

            builder.Append('\n');

            AppendAimLine(builder, capture);

            for (int i = 0; i < capture.Corners.Count; i++)
            {
                Vector3 ghost = capture.Corners[i].position.ToVector3();

                builder.AppendFormat(
                    "  c{0} G({1:F2},{2:F2},{3:F2})\n", i + 1, ghost.x, ghost.y, ghost.z);
            }

            AppendClosureLine(builder, capture);

            if (!string.IsNullOrEmpty(capture.LastError))
            {
                builder.Append("Rejected: ").Append(capture.LastError).Append('\n');
            }
            else if (capture.LastRejection != CornerCaptureRejection.None)
            {
                builder.Append("Rejected: ").Append(capture.LastRejection).Append('\n');
            }

            return builder.ToString();
        }

        private void AppendAimLine(StringBuilder target, CornerCaptureController capture)
        {
            if (!capture.TryProjectCrosshairToFloor(out Vector3 aim))
            {
                target.Append("Aim: no floor intersection\n");
                return;
            }

            target.AppendFormat("Aim G({0:F2},{1:F2},{2:F2})\n", aim.x, aim.y, aim.z);
        }

        private static void AppendClosureLine(StringBuilder target, CornerCaptureController capture)
        {
            if (!capture.HasClosureMeasurement)
            {
                return;
            }

            target.AppendFormat(
                "Closure {0:F3} m — {1}{2}\n",
                capture.ClosureErrorM,
                capture.LastClosureQuality,
                capture.LastClosureQuality == ClosureQuality.Rejected ? " (rescan)" : string.Empty);
        }
    }
}
