using System.Text;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// The Task S4 scene-facing shell: cycle through the four walls the S3
    /// footprint derived, aim at the ceiling/wall line, capture the height, or
    /// fall back to typing it in.
    ///
    /// <para>All geometry and validation lives in
    /// <see cref="HeightCaptureController"/> and <see cref="ScanWorkflowController"/>.
    /// This class reads them, forwards four button presses, and draws a
    /// world-space line on the selected wall plus a marker at the aim point,
    /// so nothing here needs a device to be trusted.</para>
    ///
    /// <para>The one behavior owned here rather than by the controller: the
    /// first time this HUD sees <see cref="ScanPhase.CaptureHeight"/> with no
    /// wall yet selected, it selects wall 0. <see cref="HeightCaptureController"/>
    /// itself never auto-selects, so every one of its own tests can assert an
    /// explicit choice; the HUD does it purely so the user is never staring at
    /// a screen with nothing selected and no way to tell why capture is
    /// disabled.</para>
    /// </summary>
    public sealed class HeightCaptureHud : MonoBehaviour
    {
        private const float WallLineWidthM = 0.025f;
        private const float AimMarkerDiameterM = 0.08f;

        private static readonly Color SelectedWallColor = new Color(1f, 0.85f, 0.15f);
        private static readonly Color AimValidColor = new Color(0.20f, 0.85f, 0.35f);
        private static readonly Color AimInvalidColor = new Color(0.9f, 0.25f, 0.2f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button selectWallButton;
        [SerializeField] private Text selectWallLabel;
        [SerializeField] private Button captureHeightButton;
        [SerializeField] private Text captureHeightLabel;
        [SerializeField] private InputField manualHeightInput;
        [SerializeField] private Button useManualHeightButton;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();

        private LineRenderer wallLine;
        private GameObject aimMarker;

        private void Awake()
        {
            if (selectWallButton != null)
            {
                selectWallButton.onClick.AddListener(OnSelectWallPressed);
            }

            if (captureHeightButton != null)
            {
                captureHeightButton.onClick.AddListener(OnCaptureHeightPressed);
            }

            if (useManualHeightButton != null)
            {
                useManualHeightButton.onClick.AddListener(OnUseManualHeightPressed);
            }
        }

        private void OnDestroy()
        {
            if (selectWallButton != null)
            {
                selectWallButton.onClick.RemoveListener(OnSelectWallPressed);
            }

            if (captureHeightButton != null)
            {
                captureHeightButton.onClick.RemoveListener(OnCaptureHeightPressed);
            }

            if (useManualHeightButton != null)
            {
                useManualHeightButton.onClick.RemoveListener(OnUseManualHeightPressed);
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

            bool inHeightPhase = workflow.Phase == ScanPhase.CaptureHeight;

            HeightCaptureController height = workflow.Height;

            // The HUD's one autonomous decision: give the user a starting
            // wall rather than a screen with nothing selected and every
            // control disabled for a reason nothing on screen explains.
            if (inHeightPhase && !height.HasWallSelected && height.WallCount > 0)
            {
                workflow.SelectHeightWall(0);
            }

            SetActive(selectWallButton, inHeightPhase);
            SetActive(captureHeightButton, inHeightPhase);
            SetActive(manualHeightInput, inHeightPhase);
            SetActive(useManualHeightButton, inHeightPhase);

            if (!inHeightPhase)
            {
                ClearWallLine();
                ClearAimMarker();

                if (readoutText != null)
                {
                    readoutText.text = string.Empty;
                }

                return;
            }

            UpdateControls(workflow, height);
            UpdateWallLine(workflow.Frame, height);

            if (readoutText != null)
            {
                readoutText.text = BuildReadout(height);
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

        private void UpdateControls(ScanWorkflowController workflow, HeightCaptureController height)
        {
            if (selectWallLabel != null)
            {
                selectWallLabel.text = height.WallCount == 0
                    ? "No walls"
                    : $"Wall {height.SelectedWallIndex + 1}/{height.WallCount}";
            }

            if (selectWallButton != null)
            {
                selectWallButton.interactable = height.WallCount > 1;
            }

            if (captureHeightButton != null)
            {
                captureHeightButton.interactable = height.CanAimAtWall;
            }

            if (captureHeightLabel != null)
            {
                captureHeightLabel.text = "Capture Height";
            }
        }

        private void OnSelectWallPressed()
        {
            ScanWorkflowController workflow = Workflow;
            HeightCaptureController height = workflow?.Height;

            if (workflow == null || height == null || height.WallCount == 0)
            {
                return;
            }

            int next = height.HasWallSelected
                ? (height.SelectedWallIndex + 1) % height.WallCount
                : 0;

            workflow.SelectHeightWall(next);
        }

        private void OnCaptureHeightPressed() => Workflow?.TryCaptureHeight(out _);

        private void OnUseManualHeightPressed()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null || manualHeightInput == null)
            {
                return;
            }

            if (float.TryParse(manualHeightInput.text, out float typed))
            {
                workflow.TrySetManualHeight(typed, out _);
            }
        }

        // -------------------------------------------------------------------
        // Selected-wall line and aim marker
        // -------------------------------------------------------------------

        private void UpdateWallLine(GhostCoordinateFrame frame, HeightCaptureController height)
        {
            if (frame == null || !height.HasWallSelected)
            {
                ClearWallLine();
                ClearAimMarker();
                return;
            }

            WallDefinition wall = height.Walls[height.SelectedWallIndex];

            if (wallLine == null)
            {
                wallLine = CreateLine();
            }

            wallLine.SetPosition(0, frame.GhostToWorld(wall.Start));
            wallLine.SetPosition(1, frame.GhostToWorld(wall.End));

            if (height.TryProjectCrosshairToWall(out Vector3 ghostAim))
            {
                if (aimMarker == null)
                {
                    aimMarker = CreateMarker();
                }

                bool valid = HeightCaptureController.ValidateHeight(ghostAim.y).IsValid;
                SetMarker(aimMarker, frame.GhostToWorld(ghostAim), valid ? AimValidColor : AimInvalidColor);
            }
            else
            {
                ClearAimMarker();
            }
        }

        private LineRenderer CreateLine()
        {
            var lineGo = new GameObject("SelectedWallLine", typeof(LineRenderer));
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

        private GameObject CreateMarker()
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "HeightAimMarker";
            marker.transform.SetParent(transform, worldPositionStays: false);
            marker.transform.localScale = Vector3.one * AimMarkerDiameterM;

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

        private void ClearAimMarker()
        {
            if (aimMarker != null)
            {
                Destroy(aimMarker);
                aimMarker = null;
            }
        }

        // -------------------------------------------------------------------
        // Readout
        // -------------------------------------------------------------------

        private string BuildReadout(HeightCaptureController height)
        {
            builder.Clear();

            if (height.WallCount == 0)
            {
                builder.Append("No footprint to derive walls from.\n");
                return builder.ToString();
            }

            WallDefinition wall = height.Walls[height.SelectedWallIndex];

            builder.AppendFormat(
                "Wall {0}/{1}: {2}->{3} ({4:F2} m)\n",
                height.SelectedWallIndex + 1, height.WallCount,
                wall.StartCornerId, wall.EndCornerId, wall.LengthM);

            AppendAimLine(builder, height, wall);

            if (height.HasCapturedHeight)
            {
                builder.AppendFormat(
                    "Room height {0:F2} m ({1})\n",
                    height.HeightM, height.IsManualEntry ? "manual" : "auto");
            }

            if (!string.IsNullOrEmpty(height.LastError))
            {
                builder.Append("Rejected: ").Append(height.LastError).Append('\n');
            }
            else if (height.LastRejection != HeightCaptureRejection.None)
            {
                builder.Append("Rejected: ").Append(height.LastRejection).Append('\n');
            }

            return builder.ToString();
        }

        private static void AppendAimLine(StringBuilder target, HeightCaptureController height, WallDefinition wall)
        {
            if (!height.TryProjectCrosshairToWall(out Vector3 ghost, out bool withinSpan))
            {
                target.Append("Aim: no wall intersection\n");
                return;
            }

            WallGeometry.ToWallLocal(wall, ghost, out float u, out float v);

            bool validHeight = HeightCaptureController.ValidateHeight(v).IsValid;

            target.AppendFormat(
                "Aim u={0:F2} h={1:F2} — {2} — {3}\n",
                u, v,
                withinSpan ? "on wall" : "off wall",
                validHeight ? "valid" : "out of range");
        }
    }
}
