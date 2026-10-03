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
    /// The buttons and per-frame feed for the automatic room scan.
    ///
    /// <para>One primary button changes its job with the phase: "Scan Room"
    /// once the floor is locked, "Finish Scan" while turning. One secondary
    /// button is the way out at each stage: "Trace Walls Instead" before the
    /// scan, "Help GhostMap" during it (the existing wall sweep, without
    /// losing the floor lock), and "Rescan Room" afterwards. The hierarchy the
    /// demo tells is AUTO, then guided recovery, then the sweep, then manual.</para>
    ///
    /// <para>The scan finishes itself when the whole turn has been seen and
    /// the walls close into a legal room. All guidance text comes from
    /// <see cref="ScanGuide"/>; this class holds none of it.</para>
    /// </summary>
    public sealed class AutoScanHud : MonoBehaviour
    {
        /// <summary>The room must stay buildable for this long before the scan finishes itself.</summary>
        private const float AutoFinishHoldS = 1.0f;

        private const float LogIntervalS = 2.0f;

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button primaryButton;
        [SerializeField] private Text primaryButtonLabel;
        [SerializeField] private Button secondaryButton;
        [SerializeField] private Text secondaryButtonLabel;
        [SerializeField] private Button addAllButton;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();

        private float readySince = -1f;
        private float lastLogAt;

        private void Awake()
        {
            if (primaryButton != null)
            {
                primaryButton.onClick.AddListener(OnPrimaryPressed);
            }

            if (secondaryButton != null)
            {
                secondaryButton.onClick.AddListener(OnSecondaryPressed);
            }

            if (addAllButton != null)
            {
                addAllButton.onClick.AddListener(OnAddAllPressed);
            }
        }

        private void OnDestroy()
        {
            if (primaryButton != null)
            {
                primaryButton.onClick.RemoveListener(OnPrimaryPressed);
            }

            if (secondaryButton != null)
            {
                secondaryButton.onClick.RemoveListener(OnSecondaryPressed);
            }

            if (addAllButton != null)
            {
                addAllButton.onClick.RemoveListener(OnAddAllPressed);
            }
        }

        private ScanWorkflowController Workflow =>
            floorLockHud != null ? floorLockHud.Workflow : null;

        private void Update()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null || workflow.Auto == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;

            if (workflow.Phase == ScanPhase.AutoScanRoom)
            {
                workflow.TickAutoScan(now);
                MaybeAutoFinish(workflow, now);
                MaybeLog(workflow, now);
            }
            else
            {
                readySince = -1f;
            }

            UpdateControls(workflow);

            if (readoutText != null)
            {
                readoutText.text = workflow.Phase == ScanPhase.AutoScanRoom ? BuildReadout(workflow) : string.Empty;
            }
        }

        private void MaybeAutoFinish(ScanWorkflowController workflow, float now)
        {
            if (!workflow.Auto.IsReadyToFinish)
            {
                readySince = -1f;
                return;
            }

            if (readySince < 0f)
            {
                readySince = now;
            }

            if (now - readySince >= AutoFinishHoldS)
            {
                Finish(workflow);
            }
        }

        private void UpdateControls(ScanWorkflowController workflow)
        {
            ScanPhase phase = workflow.Phase;
            bool floorLocked = phase == ScanPhase.FloorLocked;
            bool scanning = phase == ScanPhase.AutoScanRoom;
            bool canRescan = workflow.RoomWasAutoScanned &&
                             (phase == ScanPhase.CaptureHeight || phase == ScanPhase.AddOpenings);

            if (primaryButton != null)
            {
                primaryButton.gameObject.SetActive(floorLocked || scanning);
                primaryButton.interactable = floorLocked || scanning;
            }

            if (primaryButtonLabel != null)
            {
                primaryButtonLabel.text = scanning ? "Finish Scan" : "Scan Room";
            }

            if (secondaryButton != null)
            {
                secondaryButton.gameObject.SetActive(floorLocked || scanning || canRescan);
            }

            if (secondaryButtonLabel != null)
            {
                secondaryButtonLabel.text = scanning ? "Help GhostMap" : canRescan ? "Rescan Room" : "Trace Walls Instead";
            }

            if (addAllButton != null)
            {
                bool offer = phase == ScanPhase.AddObjects && workflow.FurnitureDetection.CandidateCount > 0;
                addAllButton.gameObject.SetActive(offer);
            }
        }

        private void OnPrimaryPressed()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null)
            {
                return;
            }

            if (workflow.Phase == ScanPhase.FloorLocked)
            {
                readySince = -1f;
                workflow.BeginAutoScan(Time.realtimeSinceStartup);
                Debug.Log("GhostMap auto: scan started");
            }
            else if (workflow.Phase == ScanPhase.AutoScanRoom)
            {
                Finish(workflow);
            }
        }

        private void OnSecondaryPressed()
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

                case ScanPhase.AutoScanRoom:
                    Debug.Log("GhostMap auto: user asked for help, falling back to wall sweep");
                    workflow.FallBackFromAutoScan();
                    break;

                case ScanPhase.CaptureHeight:
                case ScanPhase.AddOpenings:
                    workflow.RedoAutoScan(Time.realtimeSinceStartup);
                    break;
            }
        }

        private void OnAddAllPressed()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null)
            {
                return;
            }

            int added = workflow.TryAcceptAllDetectedFurniture();
            Debug.Log("GhostMap auto: added " + added + " detected object(s)");
        }

        private void Finish(ScanWorkflowController workflow)
        {
            bool ok = workflow.TryFinishAutoScan(out AutoScanRejection rejection);

            readySince = -1f;

            if (!ok)
            {
                // Stay in the scan; the guide already says what is missing.
                Debug.Log("GhostMap auto: finish refused (" + rejection + ")\n" + workflow.Auto.DebugSummary);
                return;
            }

            LogRoom(workflow);
        }

        private static void LogRoom(ScanWorkflowController workflow)
        {
            var sb = new StringBuilder("GhostMap auto: room built, walls (m):");
            var corners = workflow.Corners.Corners;

            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 a = corners[i].position.ToVector3();
                Vector3 b = corners[(i + 1) % corners.Count].position.ToVector3();
                sb.AppendFormat(" {0:F2}", Vector3.Distance(a, b));
            }

            sb.AppendFormat(
                " | height {0} | openings {1}",
                workflow.Height.HasCapturedHeight ? workflow.Height.HeightM.ToString("F2") : "not seen",
                workflow.Openings.OpeningCount);

            Debug.Log(sb.ToString());
        }

        private void MaybeLog(ScanWorkflowController workflow, float now)
        {
            if (now - lastLogAt < LogIntervalS)
            {
                return;
            }

            lastLogAt = now;
            Debug.Log("GhostMap " + workflow.Auto.DebugSummary);
        }

        private string BuildReadout(ScanWorkflowController workflow)
        {
            builder.Clear();
            builder.Append(workflow.Auto.DebugSummary);
            return builder.ToString();
        }
    }
}
