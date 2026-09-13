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
    /// The Task S5 Part 1 scene-facing shell: cycle through the four walls,
    /// toggle door/window, capture a lower-left then an upper-right point,
    /// undo the most recent opening, and finish into furniture placement.
    ///
    /// <para>All geometry and validation lives in
    /// <see cref="OpeningCaptureController"/> and <see cref="ScanWorkflowController"/>.
    /// This class reads them, forwards button presses, and draws a
    /// world-space line on the selected wall plus a marker at the aim point
    /// and at any pending start point, so nothing here needs a device to be
    /// trusted.</para>
    ///
    /// <para>Bring-up instrumentation, not the capture UI Task S6 owns — the
    /// same status every S2-S4 HUD carries.</para>
    /// </summary>
    public sealed class OpeningCaptureHud : MonoBehaviour
    {
        private const float WallLineWidthM = 0.025f;
        private const float MarkerDiameterM = 0.08f;

        private static readonly Color SelectedWallColor = new Color(1f, 0.85f, 0.15f);
        private static readonly Color AimValidColor = new Color(0.20f, 0.85f, 0.35f);
        private static readonly Color StartPointColor = new Color(0.25f, 0.65f, 1f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button selectWallButton;
        [SerializeField] private Text selectWallLabel;
        [SerializeField] private Button toggleTypeButton;
        [SerializeField] private Text toggleTypeLabel;
        [SerializeField] private Button capturePointButton;
        [SerializeField] private Text capturePointLabel;
        [SerializeField] private Button undoOpeningButton;
        [SerializeField] private Button finishOpeningsButton;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();

        private LineRenderer wallLine;
        private GameObject aimMarker;
        private GameObject startMarker;

        private void Awake()
        {
            if (selectWallButton != null)
            {
                selectWallButton.onClick.AddListener(OnSelectWallPressed);
            }

            if (toggleTypeButton != null)
            {
                toggleTypeButton.onClick.AddListener(OnToggleTypePressed);
            }

            if (capturePointButton != null)
            {
                capturePointButton.onClick.AddListener(OnCapturePointPressed);
            }

            if (undoOpeningButton != null)
            {
                undoOpeningButton.onClick.AddListener(OnUndoOpeningPressed);
            }

            if (finishOpeningsButton != null)
            {
                finishOpeningsButton.onClick.AddListener(OnFinishOpeningsPressed);
            }
        }

        private void OnDestroy()
        {
            if (selectWallButton != null)
            {
                selectWallButton.onClick.RemoveListener(OnSelectWallPressed);
            }

            if (toggleTypeButton != null)
            {
                toggleTypeButton.onClick.RemoveListener(OnToggleTypePressed);
            }

            if (capturePointButton != null)
            {
                capturePointButton.onClick.RemoveListener(OnCapturePointPressed);
            }

            if (undoOpeningButton != null)
            {
                undoOpeningButton.onClick.RemoveListener(OnUndoOpeningPressed);
            }

            if (finishOpeningsButton != null)
            {
                finishOpeningsButton.onClick.RemoveListener(OnFinishOpeningsPressed);
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

            bool inOpeningsPhase = workflow.Phase == ScanPhase.AddOpenings;

            OpeningCaptureController capture = workflow.Openings;

            // The HUD's one autonomous decision, matching HeightCaptureHud:
            // give the user a starting wall rather than a screen with nothing
            // selected and every control disabled for a reason nothing on
            // screen explains.
            if (inOpeningsPhase && !capture.HasWallSelected && capture.WallCount > 0)
            {
                workflow.SelectOpeningWall(0);
            }

            SetActive(selectWallButton, inOpeningsPhase);
            SetActive(toggleTypeButton, inOpeningsPhase);
            SetActive(capturePointButton, inOpeningsPhase);
            SetActive(undoOpeningButton, inOpeningsPhase);
            SetActive(finishOpeningsButton, inOpeningsPhase);

            if (!inOpeningsPhase)
            {
                ClearWallLine();
                ClearMarker(ref aimMarker);
                ClearMarker(ref startMarker);

                if (readoutText != null)
                {
                    readoutText.text = string.Empty;
                }

                return;
            }

            UpdateControls(workflow, capture);
            UpdateWallLine(workflow.Frame, capture);

            if (readoutText != null)
            {
                readoutText.text = BuildReadout(capture);
            }
        }

        private static void SetActive(Selectable selectable, bool active)
        {
            if (selectable != null)
            {
                selectable.gameObject.SetActive(active);
            }
        }

        // -------------------------------------------------------------------
        // Controls
        // -------------------------------------------------------------------

        private void UpdateControls(ScanWorkflowController workflow, OpeningCaptureController capture)
        {
            if (selectWallLabel != null)
            {
                selectWallLabel.text = capture.WallCount == 0
                    ? "No walls"
                    : $"Wall {capture.SelectedWallIndex + 1}/{capture.WallCount}";
            }

            if (selectWallButton != null)
            {
                selectWallButton.interactable = capture.WallCount > 1;
            }

            if (toggleTypeLabel != null)
            {
                toggleTypeLabel.text = $"Type: {capture.SelectedType}";
            }

            if (capturePointButton != null)
            {
                capturePointButton.interactable = capture.CanAimAtWall;
            }

            if (capturePointLabel != null)
            {
                capturePointLabel.text = capture.HasPendingStartPoint
                    ? "Capture Upper-Right"
                    : "Capture Lower-Left";
            }

            if (undoOpeningButton != null)
            {
                undoOpeningButton.interactable = capture.OpeningCount > 0;
            }
        }

        private void OnSelectWallPressed()
        {
            ScanWorkflowController workflow = Workflow;
            OpeningCaptureController capture = workflow?.Openings;

            if (workflow == null || capture == null || capture.WallCount == 0)
            {
                return;
            }

            int next = capture.HasWallSelected
                ? (capture.SelectedWallIndex + 1) % capture.WallCount
                : 0;

            workflow.SelectOpeningWall(next);
        }

        private void OnToggleTypePressed()
        {
            ScanWorkflowController workflow = Workflow;
            OpeningCaptureController capture = workflow?.Openings;

            if (workflow == null || capture == null)
            {
                return;
            }

            string next = capture.SelectedType == OpeningValidator.TypeDoor
                ? OpeningValidator.TypeWindow
                : OpeningValidator.TypeDoor;

            workflow.SetOpeningType(next);
        }

        private void OnCapturePointPressed()
        {
            ScanWorkflowController workflow = Workflow;
            OpeningCaptureController capture = workflow?.Openings;

            if (workflow == null || capture == null)
            {
                return;
            }

            if (capture.HasPendingStartPoint)
            {
                workflow.TryCaptureOpeningEndPoint(out _);
            }
            else
            {
                workflow.TryCaptureOpeningStartPoint(out _);
            }
        }

        private void OnUndoOpeningPressed() => Workflow?.TryUndoLastOpening(out _);

        private void OnFinishOpeningsPressed() => Workflow?.FinishAddingOpenings();

        // -------------------------------------------------------------------
        // Selected-wall line and markers
        // -------------------------------------------------------------------

        private void UpdateWallLine(GhostCoordinateFrame frame, OpeningCaptureController capture)
        {
            if (frame == null || !capture.HasWallSelected)
            {
                ClearWallLine();
                ClearMarker(ref aimMarker);
                ClearMarker(ref startMarker);
                return;
            }

            WallDefinition wall = capture.Walls[capture.SelectedWallIndex];

            if (wallLine == null)
            {
                wallLine = CreateLine();
            }

            wallLine.SetPosition(0, frame.GhostToWorld(wall.Start));
            wallLine.SetPosition(1, frame.GhostToWorld(wall.End));

            if (capture.HasPendingStartPoint)
            {
                if (startMarker == null)
                {
                    startMarker = CreateMarker("OpeningStartMarker");
                }

                SetMarker(startMarker, frame.GhostToWorld(capture.PendingStartGhost), StartPointColor);
            }
            else
            {
                ClearMarker(ref startMarker);
            }

            if (capture.TryProjectCrosshairToWall(out Vector3 ghostAim))
            {
                if (aimMarker == null)
                {
                    aimMarker = CreateMarker("OpeningAimMarker");
                }

                // Whether this single point would end up inside a legal
                // opening is only knowable once both points exist, so the aim
                // marker is a plain locator rather than a pass/fail signal.
                SetMarker(aimMarker, frame.GhostToWorld(ghostAim), AimValidColor);
            }
            else
            {
                ClearMarker(ref aimMarker);
            }
        }

        private LineRenderer CreateLine()
        {
            var lineGo = new GameObject("SelectedOpeningWallLine", typeof(LineRenderer));
            lineGo.transform.SetParent(transform, worldPositionStays: false);

            var line = lineGo.GetComponent<LineRenderer>();
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = SelectedWallColor;
            line.endColor = SelectedWallColor;
            line.widthMultiplier = WallLineWidthM;
            line.positionCount = 2;
            line.useWorldSpace = true;

            return line;
        }

        private GameObject CreateMarker(string markerName)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(transform, worldPositionStays: false);
            marker.transform.localScale = Vector3.one * MarkerDiameterM;

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

        private void ClearWallLine()
        {
            if (wallLine != null)
            {
                Destroy(wallLine.gameObject);
                wallLine = null;
            }
        }

        private void ClearMarker(ref GameObject marker)
        {
            if (marker != null)
            {
                Destroy(marker);
                marker = null;
            }
        }

        // -------------------------------------------------------------------
        // Readout
        // -------------------------------------------------------------------

        private string BuildReadout(OpeningCaptureController capture)
        {
            builder.Clear();

            builder.Append("Openings ").Append(capture.OpeningCount).Append('\n');

            if (capture.WallCount == 0)
            {
                builder.Append("No footprint to derive walls from.\n");
                return builder.ToString();
            }

            WallDefinition wall = capture.Walls[capture.SelectedWallIndex];

            builder.AppendFormat(
                "Wall {0}/{1}: {2}->{3} ({4:F2} m) | type {5}\n",
                capture.SelectedWallIndex + 1, capture.WallCount,
                wall.StartCornerId, wall.EndCornerId, wall.LengthM, capture.SelectedType);

            if (capture.HasPendingStartPoint)
            {
                WallGeometry.ToWallLocal(wall, capture.PendingStartGhost, out float su, out float sv);
                builder.AppendFormat("Start u={0:F2} h={1:F2}\n", su, sv);
            }

            if (capture.TryProjectCrosshairToWall(out Vector3 ghost))
            {
                WallGeometry.ToWallLocal(wall, ghost, out float u, out float v);
                builder.AppendFormat("Aim u={0:F2} h={1:F2}\n", u, v);
            }
            else
            {
                builder.Append("Aim: no wall intersection\n");
            }

            IReadOnlyList<OpeningModel> openings = capture.Openings;
            for (int i = 0; i < openings.Count; i++)
            {
                OpeningModel opening = openings[i];
                builder.AppendFormat(
                    "  {0} {1} offset {2:F2} width {3:F2} sill {4:F2} height {5:F2}\n",
                    i + 1, opening.type, opening.offsetM, opening.widthM, opening.sillHeightM, opening.heightM);
            }

            if (!string.IsNullOrEmpty(capture.LastError))
            {
                builder.Append("Rejected: ").Append(capture.LastError).Append('\n');
            }
            else if (capture.LastRejection != OpeningCaptureRejection.None)
            {
                builder.Append("Rejected: ").Append(capture.LastRejection).Append('\n');
            }

            return builder.ToString();
        }
    }
}
