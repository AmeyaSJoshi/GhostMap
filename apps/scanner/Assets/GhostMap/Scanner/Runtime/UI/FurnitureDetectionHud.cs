using System.Collections.Generic;
using System.Text;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// The ADR-0006 scene-facing shell: a type toggle, Next, Add and Skip, a
    /// readout, and a world-space marker over each detected candidate.
    ///
    /// <para>Detection refreshes on a timer rather than on a button, because
    /// ARKit grows planes continuously as the user looks around — a candidate
    /// list that only updated on demand would be stale exactly when the user
    /// was moving. The interval is deliberately coarse: nothing it does is free,
    /// and nothing it does is urgent.</para>
    ///
    /// <para><b>Refreshing publishes nothing.</b> Only accepting a candidate
    /// mutates the scene, which keeps performance target section 24's
    /// "snapshots only on mutation" true at a tick rate.</para>
    ///
    /// <para>All logic lives in <see cref="FurnitureDetectionController"/> and
    /// <see cref="ScanWorkflowController"/>. This class reads them, forwards
    /// button presses and draws markers, so nothing here needs a device to be
    /// trusted — though the detection it surfaces certainly does.</para>
    ///
    /// <para>Its buttons live in their own sheet row and are shown only while
    /// a candidate exists, so <see cref="ScannerGuideHud"/> collapses the row
    /// and the manual S5 placement screen looks unchanged when nothing is
    /// detected. The candidate's measurements are spoken by
    /// <see cref="ScanGuide"/>; the full numbers are in Details.</para>
    /// </summary>
    public sealed class FurnitureDetectionHud : MonoBehaviour
    {
        /// <summary>
        /// Seconds between candidate refreshes. Two per second is responsive to
        /// a user turning around and far below anything that would show up in
        /// the frame budget.
        /// </summary>
        public const float RefreshIntervalSeconds = 0.5f;

        private const float CandidateMarkerDiameterM = 0.10f;

        private static readonly Color CandidateMarkerColor = new Color(0.30f, 0.80f, 0.90f);
        private static readonly Color SelectedMarkerColor = new Color(1f, 0.85f, 0.20f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button typeButton;
        [SerializeField] private Text typeButtonLabel;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button addButton;
        [SerializeField] private Text addButtonLabel;
        [SerializeField] private Button skipButton;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();
        private readonly List<GameObject> candidateMarkers = new List<GameObject>();

        private float nextRefreshAt;

        private void Awake()
        {
            if (typeButton != null)
            {
                typeButton.onClick.AddListener(OnTypePressed);
            }

            if (nextButton != null)
            {
                nextButton.onClick.AddListener(OnNextPressed);
            }

            if (addButton != null)
            {
                addButton.onClick.AddListener(OnAddPressed);
            }

            if (skipButton != null)
            {
                skipButton.onClick.AddListener(OnSkipPressed);
            }
        }

        private void OnDestroy()
        {
            if (typeButton != null)
            {
                typeButton.onClick.RemoveListener(OnTypePressed);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(OnNextPressed);
            }

            if (addButton != null)
            {
                addButton.onClick.RemoveListener(OnAddPressed);
            }

            if (skipButton != null)
            {
                skipButton.onClick.RemoveListener(OnSkipPressed);
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

            bool inObjectPhase = workflow.Phase == ScanPhase.AddObjects;

            if (inObjectPhase && Time.unscaledTime >= nextRefreshAt)
            {
                nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
                workflow.TryRefreshFurnitureDetection(out _);
            }

            UpdateControls(workflow, inObjectPhase);
            UpdateMarkers(workflow, inObjectPhase);

            if (readoutText != null)
            {
                readoutText.text = inObjectPhase ? BuildReadout(workflow) : string.Empty;
            }
        }

        // -------------------------------------------------------------------
        // Controls
        // -------------------------------------------------------------------

        private void UpdateControls(ScanWorkflowController workflow, bool inObjectPhase)
        {
            FurnitureDetectionController detection = workflow.FurnitureDetection;
            bool hasCandidate = inObjectPhase && detection.HasSelection;

            // Hidden rather than greyed out with no candidate: four dead
            // buttons above the manual Place button would explain nothing.
            if (typeButton != null)
            {
                typeButton.gameObject.SetActive(hasCandidate);
            }

            if (typeButtonLabel != null)
            {
                typeButtonLabel.text = $"Is: {ScanGuide.Capitalize(detection.SelectedType)}";
            }

            if (nextButton != null)
            {
                nextButton.gameObject.SetActive(hasCandidate && detection.CandidateCount > 1);
            }

            if (addButton != null)
            {
                addButton.gameObject.SetActive(hasCandidate);
            }

            if (addButtonLabel != null)
            {
                addButtonLabel.text = "Add";
            }

            if (skipButton != null)
            {
                skipButton.gameObject.SetActive(hasCandidate);
            }
        }

        /// <summary>
        /// Cycles the type through the shared supported list, so the HUD never
        /// holds its own copy of what GhostMap's furniture types are.
        /// </summary>
        private void OnTypePressed()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null)
            {
                return;
            }

            IReadOnlyList<string> types = FurnitureValidator.SupportedTypes;

            if (types.Count == 0)
            {
                return;
            }

            string current = workflow.FurnitureDetection.SelectedType;
            int index = 0;

            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] == current)
                {
                    index = i;
                    break;
                }
            }

            workflow.SetDetectedFurnitureType(types[(index + 1) % types.Count]);
        }

        private void OnNextPressed() => Workflow?.SelectNextDetectedCandidate();

        private void OnAddPressed()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null || !workflow.FurnitureDetection.HasSelection)
            {
                return;
            }

            FurnitureDetectionController detection = workflow.FurnitureDetection;
            FurnitureCandidate candidate = detection.Candidates[detection.SelectedIndex];
            string type = detection.SelectedType;

            bool accepted = workflow.TryAcceptDetectedFurniture(
                out FurnitureDetectionRejection detectionRejection,
                out ObjectPlacementRejection placementRejection);

            // Device-test observability: the numbers to compare against a
            // tape measure, in the Xcode console, without opening Details.
            Debug.Log(string.Format(
                "GhostMap detect: {0} {1} [ARKit {9}, suggested {10}] G({2:F3},{3:F3}) w {4:F3} d {5:F3} top {6:F3} m yaw {7:F1} -> {8}",
                accepted ? "ADDED" : "REFUSED",
                type,
                candidate.CenterGhost.x,
                candidate.CenterGhost.z,
                candidate.WidthM,
                candidate.DepthM,
                candidate.HeightM,
                candidate.YawDeg,
                accepted
                    ? $"objects {workflow.Objects.ObjectCount}"
                    : $"{detectionRejection}/{placementRejection} {detection.LastError} {workflow.Objects.LastError}",
                candidate.Classifications,
                candidate.SuggestedType));
        }

        private void OnSkipPressed() => Workflow?.DismissDetectedCandidate();

        // -------------------------------------------------------------------
        // Markers
        // -------------------------------------------------------------------

        /// <summary>
        /// A marker floating at each candidate's measured top-surface height,
        /// so the user can see on the real furniture what GhostMap thinks it
        /// found — and in particular see when the height it measured is the seat
        /// of a chair rather than its back.
        /// </summary>
        private void UpdateMarkers(ScanWorkflowController workflow, bool inObjectPhase)
        {
            GhostCoordinateFrame frame = workflow.Frame;

            if (frame == null || !inObjectPhase)
            {
                ClearMarkers();
                return;
            }

            IReadOnlyList<FurnitureCandidate> candidates = workflow.FurnitureDetection.Candidates;

            while (candidateMarkers.Count > candidates.Count)
            {
                int last = candidateMarkers.Count - 1;
                Destroy(candidateMarkers[last]);
                candidateMarkers.RemoveAt(last);
            }

            while (candidateMarkers.Count < candidates.Count)
            {
                candidateMarkers.Add(CreateMarker(
                    $"Candidate {candidateMarkers.Count + 1}", CandidateMarkerDiameterM));
            }

            int selected = workflow.FurnitureDetection.SelectedIndex;

            for (int i = 0; i < candidates.Count; i++)
            {
                FurnitureCandidate candidate = candidates[i];

                var atSurface = new Vector3(
                    candidate.CenterGhost.x, candidate.HeightM, candidate.CenterGhost.z);

                SetMarker(
                    candidateMarkers[i],
                    frame.GhostToWorld(atSurface),
                    i == selected ? SelectedMarkerColor : CandidateMarkerColor);
            }
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
            for (int i = 0; i < candidateMarkers.Count; i++)
            {
                Destroy(candidateMarkers[i]);
            }

            candidateMarkers.Clear();
        }

        // -------------------------------------------------------------------
        // Readout
        // -------------------------------------------------------------------

        private string BuildReadout(ScanWorkflowController workflow)
        {
            FurnitureDetectionController detection = workflow.FurnitureDetection;

            builder.Clear();

            builder.Append("Detected ").Append(detection.CandidateCount)
                .Append(" of ").Append(detection.LastSurfaceCount).Append(" surfaces\n");

            if (detection.HasSelection)
            {
                FurnitureCandidate candidate = detection.Candidates[detection.SelectedIndex];

                builder.AppendFormat(
                    "  sel {0}/{1} G({2:F2},{3:F2}) {4:F2}x{5:F2} top {6:F2} m yaw {7:F0}\n",
                    detection.SelectedIndex + 1,
                    detection.CandidateCount,
                    candidate.CenterGhost.x,
                    candidate.CenterGhost.z,
                    candidate.WidthM,
                    candidate.DepthM,
                    candidate.HeightM,
                    candidate.YawDeg);

                builder.AppendFormat(
                    "  ARKit label {0} -> {1}{2}\n",
                    candidate.Classifications,
                    detection.SelectedType,
                    detection.TypeIsSuggested ? " (suggested)" : " (yours)");
            }
            else if (detection.CandidateCount == 0)
            {
                builder.Append(
                    "  Look at a desk, table or bed top. Place by hand if nothing appears.\n");
            }

            if (!string.IsNullOrEmpty(detection.LastError))
            {
                builder.Append("Rejected: ").Append(detection.LastError).Append('\n');
            }
            else if (detection.LastRejection != FurnitureDetectionRejection.None)
            {
                builder.Append("Rejected: ").Append(detection.LastRejection).Append('\n');
            }

            // A refusal from the object store is the actionable one: the
            // geometry was fine but the shared furniture rules said no.
            if (!string.IsNullOrEmpty(workflow.Objects.LastError))
            {
                builder.Append("Object: ").Append(workflow.Objects.LastError).Append('\n');
            }

            return builder.ToString();
        }
    }
}
