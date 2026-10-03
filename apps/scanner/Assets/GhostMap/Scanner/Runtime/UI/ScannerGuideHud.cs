using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// The scanner's guide layer: step header and progress, one plain
    /// instruction, a colored message line, a bubble beside the crosshair
    /// naming what to point at, and the crosshair itself, which turns mint
    /// when it is on something it can capture.
    ///
    /// <para>The words come from <see cref="ScanGuide"/>. The per-phase HUDs
    /// still own their buttons, labels and AR markers; this class only
    /// collapses button rows that have nothing visible in them, so the bottom
    /// sheet always fits its current step and never stacks one step's buttons
    /// on another's.</para>
    ///
    /// <para>Runs in <see cref="LateUpdate"/>, after every HUD's
    /// <c>Update</c> has decided which of its buttons are visible this
    /// frame.</para>
    /// </summary>
    public sealed class ScannerGuideHud : MonoBehaviour
    {
        public static readonly Color Accent = new Color(0.22f, 0.86f, 0.76f);
        public static readonly Color AccentText = new Color(0.03f, 0.13f, 0.12f);

        private static readonly Color ReticleIdle = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color ReticleNoTracking = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color BubbleIdle = new Color(0.07f, 0.08f, 0.10f, 0.86f);
        private static readonly Color SegmentFuture = new Color(1f, 1f, 1f, 0.18f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private ScannerHudController scannerHud;

        [Header("Header")]
        [SerializeField] private Text stepLabel;
        [SerializeField] private Text titleText;
        [SerializeField] private Image[] progressSegments;
        [SerializeField] private GameObject demoBadge;

        [Header("Crosshair")]
        [SerializeField] private Graphic crosshair;
        [SerializeField] private GameObject coachBubble;
        [SerializeField] private Image coachBubbleBackground;
        [SerializeField] private Text coachText;

        [Header("Sheet")]
        [SerializeField] private Text instructionText;
        [SerializeField] private GameObject messagePill;
        [SerializeField] private Image messageBackground;
        [SerializeField] private Text messageText;
        [SerializeField] private GameObject[] collapsibleRows;

        [Header("Details")]
        [SerializeField] private Button detailsButton;
        [SerializeField] private Text detailsButtonLabel;
        [SerializeField] private GameObject detailsPanel;

        private bool detailsOpen;
        private Text[] detailsTexts;

        private ScanWorkflowController Workflow => floorLockHud != null ? floorLockHud.Workflow : null;

        private void Awake()
        {
            if (detailsButton != null)
            {
                detailsButton.onClick.AddListener(ToggleDetails);
            }

            if (detailsPanel != null)
            {
                detailsTexts = detailsPanel.GetComponentsInChildren<Text>(includeInactive: true);
                detailsPanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (detailsButton != null)
            {
                detailsButton.onClick.RemoveListener(ToggleDetails);
            }
        }

        private void ToggleDetails()
        {
            detailsOpen = !detailsOpen;

            if (detailsPanel != null)
            {
                detailsPanel.SetActive(detailsOpen);
            }

            if (detailsButtonLabel != null)
            {
                detailsButtonLabel.text = detailsOpen ? "Close" : "Details";
            }
        }

        private void LateUpdate()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null || spatialProvider == null)
            {
                return;
            }

            bool trackingGood = spatialProvider.IsTrackingGood;

            var context = new ScanGuideContext(
                trackingGood,
                floorLockHud.LastAttemptRejection,
                scannerHud != null && scannerHud.IsConnected);

            ScanGuideStep step = ScanGuide.Describe(workflow, context);

            UpdateHeader(step);
            UpdateSheet(workflow, step, trackingGood);
            UpdateCrosshair(workflow, step, trackingGood);
            CollapseEmptyRows();
            HideEmptyDetails();

            if (demoBadge != null)
            {
                demoBadge.SetActive(spatialProvider.IsSimulated);
            }
        }

        private void UpdateHeader(ScanGuideStep step)
        {
            SetText(stepLabel, $"STEP {step.Number} OF {ScanGuide.StepCount}");
            SetText(titleText, step.Title);

            if (progressSegments == null)
            {
                return;
            }

            for (int i = 0; i < progressSegments.Length; i++)
            {
                if (progressSegments[i] != null)
                {
                    progressSegments[i].color = i < step.Number ? Accent : SegmentFuture;
                }
            }
        }

        private void UpdateSheet(ScanWorkflowController workflow, ScanGuideStep step, bool trackingGood)
        {
            SetText(instructionText, step.Instruction);

            string message = step.Message;
            GuideMessageKind kind = step.MessageKind;

            // Losing tracking mid-scan blocks every capture button, so it
            // outranks whatever else the step wanted to say.
            if (!trackingGood && workflow.Frame != null && workflow.Phase != ScanPhase.Finalized)
            {
                message = "Tracking lost. Move the phone slowly and point it at the room.";
                kind = GuideMessageKind.Warning;
            }

            bool hasMessage = !string.IsNullOrEmpty(message) && kind != GuideMessageKind.None;

            if (messagePill != null)
            {
                messagePill.SetActive(hasMessage);
            }

            if (!hasMessage)
            {
                return;
            }

            SetText(messageText, message);
            MessageColors(kind, out Color background, out Color foreground);

            if (messageBackground != null)
            {
                messageBackground.color = background;
            }

            if (messageText != null)
            {
                messageText.color = foreground;
            }
        }

        private void UpdateCrosshair(ScanWorkflowController workflow, ScanGuideStep step, bool trackingGood)
        {
            bool aiming = !string.IsNullOrEmpty(step.AimHint);
            bool onTarget = aiming && trackingGood && IsAimOnTarget(workflow);

            if (crosshair != null)
            {
                crosshair.color = !trackingGood ? ReticleNoTracking : onTarget ? Accent : ReticleIdle;

                // A gentle pulse says "ready to capture" without any words.
                float scale = onTarget ? 1f + (0.08f * Mathf.Sin(Time.unscaledTime * 5f)) : 1f;
                crosshair.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }

            bool showBubble = aiming && !detailsOpen;

            if (coachBubble != null)
            {
                coachBubble.SetActive(showBubble);
            }

            if (!showBubble)
            {
                return;
            }

            SetText(coachText, step.AimHint);

            if (coachBubbleBackground != null)
            {
                coachBubbleBackground.color = onTarget ? Accent : BubbleIdle;
            }

            if (coachText != null)
            {
                coachText.color = onTarget ? AccentText : Color.white;
            }
        }

        /// <summary>
        /// Whether the crosshair currently rests on something this step can
        /// capture. Reads the same projections the capture buttons use, so a
        /// mint crosshair means the button will not refuse for want of a
        /// target.
        /// </summary>
        private bool IsAimOnTarget(ScanWorkflowController workflow)
        {
            switch (workflow.Phase)
            {
                case ScanPhase.FindFloor:
                    return spatialProvider.TryGetFloorHit(spatialProvider.CenterScreenPoint, out FloorHit hit)
                        && hit.Alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp;

                case ScanPhase.SweepWalls:
                    return workflow.WallSweep.TryProjectCrosshairToFloor(out _, out _);

                case ScanPhase.CaptureCorners:
                case ScanPhase.VerifyClosure:
                case ScanPhase.AddObjects:
                    return workflow.Corners.TryProjectCrosshairToFloor(out _);

                case ScanPhase.CaptureHeight:
                    return workflow.Height.TryProjectCrosshairToWall(out Vector3 aim, out bool withinSpan)
                        && withinSpan
                        && HeightCaptureController.ValidateHeight(aim.y).IsValid;

                case ScanPhase.AddOpenings:
                    return workflow.Openings.CanAimAtWall;

                default:
                    return false;
            }
        }

        /// <summary>
        /// A button row whose buttons are all hidden takes no space, so the
        /// sheet shrinks to the current step instead of leaving gaps.
        /// </summary>
        private void CollapseEmptyRows()
        {
            if (collapsibleRows == null)
            {
                return;
            }

            foreach (GameObject row in collapsibleRows)
            {
                if (row == null)
                {
                    continue;
                }

                bool anyVisible = false;
                Transform rowTransform = row.transform;

                for (int i = 0; i < rowTransform.childCount; i++)
                {
                    if (rowTransform.GetChild(i).gameObject.activeSelf)
                    {
                        anyVisible = true;
                        break;
                    }
                }

                row.SetActive(anyVisible);
            }
        }

        /// <summary>
        /// Each HUD blanks its readout outside its own step; an empty line
        /// would still take a line of space in the Details list.
        /// </summary>
        private void HideEmptyDetails()
        {
            if (!detailsOpen || detailsTexts == null)
            {
                return;
            }

            foreach (Text text in detailsTexts)
            {
                if (text != null)
                {
                    text.gameObject.SetActive(!string.IsNullOrEmpty(text.text));
                }
            }
        }

        private static void MessageColors(GuideMessageKind kind, out Color background, out Color foreground)
        {
            switch (kind)
            {
                case GuideMessageKind.Success:
                    background = new Color(0.29f, 0.87f, 0.50f, 0.18f);
                    foreground = new Color(0.62f, 0.96f, 0.74f);
                    break;

                case GuideMessageKind.Warning:
                    background = new Color(1f, 0.71f, 0.28f, 0.18f);
                    foreground = new Color(1f, 0.84f, 0.55f);
                    break;

                case GuideMessageKind.Error:
                    background = new Color(1f, 0.42f, 0.42f, 0.20f);
                    foreground = new Color(1f, 0.70f, 0.70f);
                    break;

                default:
                    background = new Color(1f, 1f, 1f, 0.08f);
                    foreground = new Color(0.80f, 0.84f, 0.90f);
                    break;
            }
        }

        private static void SetText(Text target, string value)
        {
            if (target != null && target.text != value)
            {
                target.text = value;
            }
        }
    }
}
