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
        private Text redoLabel;

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

            bool canCapture = workflow.Phase == ScanPhase.CaptureCorners && capture.CanCapture;
            bool canVerify = workflow.Phase == ScanPhase.VerifyClosure && capture.CanVerifyClosure;

            // FloorLocked no longer belongs to this HUD. ADR-0005 makes
            // sweeping the default route out of it, and WallSweepHud owns the
            // same on-screen slot there; the walked path is entered from the
            // sweep phase via "Walk Corners Instead". The
            // FloorLocked -> CaptureCorners transition itself is retained in
            // the workflow, so the controller-level path is unchanged.
            if (primaryButton != null)
            {
                primaryButton.gameObject.SetActive(
                    workflow.Phase == ScanPhase.CaptureCorners
                    || workflow.Phase == ScanPhase.VerifyClosure);

                primaryButton.interactable = canCapture || canVerify;
            }

            if (primaryButtonLabel != null)
            {
                primaryButtonLabel.text = PrimaryLabel(workflow);
            }

            // Undoing one derived corner of a swept room would drop the user
            // into walking corners, so Undo belongs to the walked path only.
            if (undoButton != null)
            {
                undoButton.gameObject.SetActive(
                    workflow.Phase == ScanPhase.CaptureCorners
                    || (workflow.Phase == ScanPhase.VerifyClosure && !workflow.RoomWasSwept));

                undoButton.interactable = capture.CornerCount > 0;
            }

            // Redo is offered as soon as there is a footprint to throw away,
            // and returns to whichever path produced it (RedoRoom).
            if (redoButton != null)
            {
                redoButton.gameObject.SetActive(
                    workflow.Phase == ScanPhase.VerifyClosure
                    || workflow.Phase == ScanPhase.CaptureHeight);

                if (redoLabel == null)
                {
                    redoLabel = redoButton.GetComponentInChildren<Text>(includeInactive: true);
                }

                if (redoLabel != null)
                {
                    redoLabel.text = workflow.RoomWasSwept ? "Redo Walls" : "Redo Corners";
                }
            }
        }

        private static string PrimaryLabel(ScanWorkflowController workflow)
        {
            CornerCaptureController capture = workflow.Corners;

            switch (workflow.Phase)
            {
                case ScanPhase.CaptureCorners:
                    return $"Capture Corner {capture.CornerCount + 1} of {CornerCaptureController.RequiredCornerCount}";

                case ScanPhase.VerifyClosure:
                    return "Check Corner";

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
                case ScanPhase.CaptureCorners:
                    workflow.TryCaptureCorner(out _);
                    break;

                case ScanPhase.VerifyClosure:
                    workflow.TryVerifyClosure(out _, out _);

                    if (workflow.Corners.HasClosureMeasurement)
                    {
                        Debug.Log(string.Format(
                            "GhostMap closure: error {0:F3} m quality {1} ({2}) {3}",
                            workflow.Corners.ClosureErrorM,
                            workflow.Corners.LastClosureQuality,
                            workflow.RoomWasSwept ? "swept" : "walked",
                            DescribeFootprint(workflow.Corners)));
                    }

                    break;
            }
        }

        private void OnUndoPressed() => Workflow?.TryUndoCorner(out _);

        private void OnRedoPressed() => Workflow?.RedoRoom();

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
            MarkerMaterials.MakeUnlit(marker);
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

        /// <summary>
        /// Corner-to-corner wall lengths, c1→c2 first: the numbers a
        /// tape-measure comparison needs, whichever path made the corners.
        /// </summary>
        public static string DescribeWallLengths(CornerCaptureController capture)
        {
            var lengths = new StringBuilder("Walls");
            int count = capture.Corners.Count;

            for (int i = 0; i < count; i++)
            {
                Vector3 a = capture.Corners[i].position.ToVector3();
                Vector3 b = capture.Corners[(i + 1) % count].position.ToVector3();
                float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;

                lengths.AppendFormat(" L{0} {1:F3}", i + 1, length);
            }

            return lengths.Append(" m").ToString();
        }

        /// <summary>Corners and wall lengths on one line, for the Xcode console.</summary>
        public static string DescribeFootprint(CornerCaptureController capture)
        {
            var footprint = new StringBuilder();

            for (int i = 0; i < capture.Corners.Count; i++)
            {
                Vector3 ghost = capture.Corners[i].position.ToVector3();
                footprint.AppendFormat("c{0} ({1:F3},{2:F3}) ", i + 1, ghost.x, ghost.z);
            }

            return footprint.Append(DescribeWallLengths(capture)).ToString();
        }

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

            if (capture.IsComplete)
            {
                builder.Append(DescribeWallLengths(capture)).Append('\n');
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
