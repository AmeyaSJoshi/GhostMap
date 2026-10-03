using System.Collections.Generic;
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
    /// The ADR-0005 scene-facing shell: one context-sensitive primary button,
    /// Cancel, Undo, a fallback to walked corners, a readout, and world-space
    /// markers showing the live aim point and each fitted wall.
    ///
    /// <para><b>Sweeping is a tap-to-start, tap-to-finish toggle, not a held
    /// button.</b> Holding a button while panning a phone across a room is
    /// awkward one-handed and would need <c>EventTrigger</c> pointer plumbing
    /// that no other HUD in this scene uses. A toggle needs only
    /// <see cref="Button.onClick"/>, exactly like
    /// <see cref="CornerCaptureHud"/>, and the per-frame sampling happens in
    /// <see cref="Update"/> while a sweep is active.</para>
    ///
    /// <para>All logic lives in <see cref="WallSweepController"/> and
    /// <see cref="ScanWorkflowController"/>. This class reads them, forwards
    /// button presses and draws markers, so nothing here needs a device to be
    /// trusted.</para>
    ///
    /// <para><see cref="FloorLockHud"/> is the scene's composition root, so the
    /// workflow is read lazily rather than cached in this component's own
    /// <c>Awake</c> — Unity does not order <c>Awake</c> across GameObjects, and
    /// caching it would be a null reference that appeared only sometimes.</para>
    ///
    /// <para>The live aim marker is the one piece of feedback the Task S3
    /// device work explicitly wanted and did not have (see the scanner status'
    /// "no AR marker for the aim point" note). Sweeping needs it far more than
    /// tapping did: the user is tracking a line rather than stopping on a
    /// point.</para>
    /// </summary>
    public sealed class WallSweepHud : MonoBehaviour
    {
        private const float AimMarkerDiameterM = 0.07f;
        private const float WallEndMarkerDiameterM = 0.08f;

        private static readonly Color AimMarkerColor = new Color(1f, 0.95f, 0.25f);
        private static readonly Color WallEndMarkerColor = new Color(0.25f, 0.65f, 1f);
        private static readonly Color FarWallEndMarkerColor = new Color(1f, 0.45f, 0.15f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button primaryButton;
        [SerializeField] private Text primaryButtonLabel;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button walkCornersButton;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();
        private readonly List<GameObject> wallEndMarkers = new List<GameObject>();

        private GameObject aimMarker;

        private void Awake()
        {
            if (primaryButton != null)
            {
                primaryButton.onClick.AddListener(OnPrimaryPressed);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(OnCancelPressed);
            }

            if (undoButton != null)
            {
                undoButton.onClick.AddListener(OnUndoPressed);
            }

            if (walkCornersButton != null)
            {
                walkCornersButton.onClick.AddListener(OnWalkCornersPressed);
            }
        }

        private void OnDestroy()
        {
            if (primaryButton != null)
            {
                primaryButton.onClick.RemoveListener(OnPrimaryPressed);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(OnCancelPressed);
            }

            if (undoButton != null)
            {
                undoButton.onClick.RemoveListener(OnUndoPressed);
            }

            if (walkCornersButton != null)
            {
                walkCornersButton.onClick.RemoveListener(OnWalkCornersPressed);
            }
        }

        private ScanWorkflowController Workflow =>
            floorLockHud != null ? floorLockHud.Workflow : null;

        private void Update()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null || spatialProvider == null)
            {
                return;
            }

            // The sample feed. This is the only per-frame work the sweep does,
            // and it publishes nothing — see WallSweepController.TryAddSample.
            if (workflow.Phase == ScanPhase.SweepWalls && workflow.WallSweep.IsSweeping)
            {
                workflow.TryAddWallSweepSample(out _);
            }

            UpdateControls(workflow);
            UpdateMarkers(workflow);

            bool inSweepPhase = workflow.Phase == ScanPhase.SweepWalls;

            if (readoutText != null)
            {
                readoutText.text = inSweepPhase ? BuildReadout(workflow) : string.Empty;
            }
        }

        // -------------------------------------------------------------------
        // Controls
        // -------------------------------------------------------------------

        private void UpdateControls(ScanWorkflowController workflow)
        {
            WallSweepController sweep = workflow.WallSweep;

            bool floorLocked = workflow.Phase == ScanPhase.FloorLocked;
            bool sweeping = workflow.Phase == ScanPhase.SweepWalls;

            if (primaryButton != null)
            {
                primaryButton.gameObject.SetActive(floorLocked || sweeping);
                primaryButton.interactable = IsPrimaryUsable(workflow);
            }

            if (primaryButtonLabel != null)
            {
                primaryButtonLabel.text = PrimaryLabel(workflow);
            }

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(sweeping && sweep.IsSweeping);
            }

            if (undoButton != null)
            {
                undoButton.gameObject.SetActive(sweeping);
                undoButton.interactable = !sweep.IsSweeping && sweep.WallCount > 0;
            }

            // The fallback exists for the case where sweeping underperforms in
            // a real room, so it is offered only before any wall is committed —
            // switching path discards whatever was swept.
            if (walkCornersButton != null)
            {
                walkCornersButton.gameObject.SetActive(sweeping && !sweep.IsSweeping);
            }
        }

        private static bool IsPrimaryUsable(ScanWorkflowController workflow)
        {
            WallSweepController sweep = workflow.WallSweep;

            switch (workflow.Phase)
            {
                case ScanPhase.FloorLocked:
                    return true;

                case ScanPhase.SweepWalls:
                    // While sweeping, Finish is always offered: the user must be
                    // able to stop, and the controller explains any refusal.
                    return sweep.IsSweeping || sweep.IsComplete || sweep.CanSweep;

                default:
                    return false;
            }
        }

        private static string PrimaryLabel(ScanWorkflowController workflow)
        {
            WallSweepController sweep = workflow.WallSweep;

            switch (workflow.Phase)
            {
                case ScanPhase.FloorLocked:
                    return "Start Walls";

                case ScanPhase.SweepWalls:
                    if (sweep.IsSweeping)
                    {
                        return sweep.ActiveSweepLooksUsable
                            ? $"Finish Wall {sweep.WallCount + 1}"
                            : $"Finish Wall {sweep.WallCount + 1} (keep sweeping)";
                    }

                    if (sweep.IsComplete)
                    {
                        return "Build Room";
                    }

                    return
                        $"Sweep Wall {sweep.WallCount + 1}/" +
                        $"{WallSweepController.RequiredWallCount}";

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
                    workflow.BeginWallSweeping();
                    break;

                case ScanPhase.SweepWalls:
                    if (workflow.WallSweep.IsSweeping)
                    {
                        workflow.TryCompleteWallSweep(out _);
                    }
                    else if (workflow.WallSweep.IsComplete)
                    {
                        workflow.TryDeriveRoomFromSweeps(out _, out _);
                    }
                    else
                    {
                        workflow.TryBeginWallSweep(out _);
                    }

                    break;
            }
        }

        private void OnCancelPressed() => Workflow?.CancelWallSweep();

        private void OnUndoPressed() => Workflow?.TryUndoLastWall(out _);

        private void OnWalkCornersPressed() => Workflow?.FallBackToWalkedCorners();

        // -------------------------------------------------------------------
        // Markers
        // -------------------------------------------------------------------

        private void UpdateMarkers(ScanWorkflowController workflow)
        {
            GhostCoordinateFrame frame = workflow.Frame;

            if (frame == null || workflow.Phase != ScanPhase.SweepWalls)
            {
                ClearMarkers();
                return;
            }

            UpdateAimMarker(workflow, frame);
            UpdateWallMarkers(workflow, frame);
        }

        private void UpdateAimMarker(ScanWorkflowController workflow, GhostCoordinateFrame frame)
        {
            if (!workflow.WallSweep.TryProjectCrosshairToFloor(out Vector3 ghost, out _))
            {
                if (aimMarker != null)
                {
                    aimMarker.SetActive(false);
                }

                return;
            }

            if (aimMarker == null)
            {
                aimMarker = CreateMarker("Sweep Aim", AimMarkerDiameterM);
            }

            aimMarker.SetActive(true);
            SetMarker(aimMarker, frame.GhostToWorld(ghost), AimMarkerColor);
        }

        /// <summary>
        /// Two markers per accepted wall, at the ends of the segment that was
        /// actually swept.
        ///
        /// <para>Showing the swept extent rather than the derived corners is
        /// deliberate: the extent is what the fit was computed from, so a wall
        /// whose markers sit well short of the real corners is visibly a
        /// long extrapolation, which is exactly the accuracy risk ADR-0005
        /// records. Derived corners get their own markers from
        /// <see cref="CornerCaptureHud"/> once the room is built.</para>
        /// </summary>
        private void UpdateWallMarkers(ScanWorkflowController workflow, GhostCoordinateFrame frame)
        {
            IReadOnlyList<SweptWall> walls = workflow.WallSweep.Walls;
            int wanted = walls.Count * 2;

            while (wallEndMarkers.Count > wanted)
            {
                int last = wallEndMarkers.Count - 1;
                Destroy(wallEndMarkers[last]);
                wallEndMarkers.RemoveAt(last);
            }

            while (wallEndMarkers.Count < wanted)
            {
                wallEndMarkers.Add(CreateMarker(
                    $"Wall End {wallEndMarkers.Count + 1}", WallEndMarkerDiameterM));
            }

            for (int i = 0; i < walls.Count; i++)
            {
                WallLine line = walls[i].Line;
                Vector3 half = line.Direction * (line.SpanM * 0.5f);

                Color color = walls[i].MaxSampleDistanceM > WallSweepController.FarSweepWarningM
                    ? FarWallEndMarkerColor
                    : WallEndMarkerColor;

                SetMarker(wallEndMarkers[i * 2], frame.GhostToWorld(line.Point - half), color);
                SetMarker(wallEndMarkers[(i * 2) + 1], frame.GhostToWorld(line.Point + half), color);
            }
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
            for (int i = 0; i < wallEndMarkers.Count; i++)
            {
                Destroy(wallEndMarkers[i]);
            }

            wallEndMarkers.Clear();

            if (aimMarker != null)
            {
                Destroy(aimMarker);
                aimMarker = null;
            }
        }

        // -------------------------------------------------------------------
        // Readout
        // -------------------------------------------------------------------

        private string BuildReadout(ScanWorkflowController workflow)
        {
            WallSweepController sweep = workflow.WallSweep;

            builder.Clear();

            builder.Append("Walls ").Append(sweep.WallCount)
                .Append('/').Append(WallSweepController.RequiredWallCount);

            if (sweep.IsSweeping)
            {
                builder.Append(" | sweeping");
            }
            else if (sweep.IsComplete)
            {
                builder.Append(" | ready to build");
            }

            builder.Append('\n');

            AppendAimLine(builder, sweep);

            if (sweep.IsSweeping)
            {
                builder.AppendFormat(
                    "  pts {0} (need {1}) span {2:F2} m (need {3:F2})\n",
                    sweep.ActiveSampleCount,
                    WallFitting.MinSampleCount,
                    sweep.ActiveSpanM,
                    WallFitting.MinSpanM);
            }

            for (int i = 0; i < sweep.Walls.Count; i++)
            {
                WallLine line = sweep.Walls[i].Line;

                builder.AppendFormat(
                    "  w{0} span {1:F2} m rms {2:F3} m @{3:F1} m\n",
                    i + 1,
                    line.SpanM,
                    line.RmsResidualM,
                    sweep.Walls[i].MaxSampleDistanceM);
            }

            if (sweep.HasFarSweptWall)
            {
                builder.AppendFormat(
                    "Far sweep (>{0:F0} m): accuracy limited by distance, not fit.\n",
                    WallSweepController.FarSweepWarningM);
            }

            if (!string.IsNullOrEmpty(sweep.LastError))
            {
                builder.Append("Rejected: ").Append(sweep.LastError).Append('\n');
            }
            else if (sweep.LastRejection != WallSweepRejection.None)
            {
                builder.Append("Rejected: ").Append(sweep.LastRejection).Append('\n');
            }

            // Room-level refusals come from the corner store, not the sweeper:
            // a geometrically fine set of four walls can still fail
            // RoomValidator, and that message is the actionable one.
            if (!string.IsNullOrEmpty(workflow.Corners.LastError))
            {
                builder.Append("Room: ").Append(workflow.Corners.LastError).Append('\n');
            }

            return builder.ToString();
        }

        private void AppendAimLine(StringBuilder target, WallSweepController sweep)
        {
            if (!sweep.TryProjectCrosshairToFloor(out Vector3 aim, out float distanceM))
            {
                target.Append("Aim: no floor intersection\n");
                return;
            }

            target.AppendFormat(
                "Aim G({0:F2},{1:F2}) @{2:F1} m\n", aim.x, aim.z, distanceM);
        }
    }
}
