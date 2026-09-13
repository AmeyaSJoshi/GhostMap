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
    /// The Task S5 Part 2 scene-facing shell: cycle through the MVP furniture
    /// types, place one under the crosshair on the locked floor, undo the
    /// most recent placement, step its width/depth/height/yaw, and finish the
    /// scan's structural capture.
    ///
    /// <para>All geometry and validation lives in
    /// <see cref="ObjectPlacementController"/> and <see cref="ScanWorkflowController"/>.
    /// This class reads them, forwards button presses, and draws a
    /// world-space marker at the aim point plus one per placed object, so
    /// nothing here needs a device to be trusted.</para>
    ///
    /// <para>Adjustment is by fixed step rather than a typed value — the same
    /// simplicity trade Task S4's manual height field makes the other way —
    /// because this is bring-up instrumentation, not the capture UI Task S6
    /// owns. It always acts on the most recently placed object, the one the
    /// user just aimed at.</para>
    /// </summary>
    public sealed class ObjectPlacementHud : MonoBehaviour
    {
        private const float ObjectMarkerDiameterM = 0.12f;
        private const float DimensionStepM = 0.10f;
        private const float YawStepDeg = 15f;

        private static readonly Color AimMarkerColor = new Color(0.25f, 0.65f, 1f);
        private static readonly Color PlacedObjectColor = new Color(0.95f, 0.65f, 0.15f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private Button selectTypeButton;
        [SerializeField] private Text selectTypeLabel;
        [SerializeField] private Button placeObjectButton;
        [SerializeField] private Button undoObjectButton;
        [SerializeField] private Button widthPlusButton;
        [SerializeField] private Button widthMinusButton;
        [SerializeField] private Button depthPlusButton;
        [SerializeField] private Button depthMinusButton;
        [SerializeField] private Button heightPlusButton;
        [SerializeField] private Button heightMinusButton;
        [SerializeField] private Button yawPlusButton;
        [SerializeField] private Button yawMinusButton;
        [SerializeField] private Button finishObjectsButton;
        [SerializeField] private Text readoutText;

        private readonly StringBuilder builder = new StringBuilder();
        private readonly List<GameObject> objectMarkers = new List<GameObject>();

        private GameObject aimMarker;

        private void Awake()
        {
            Bind(selectTypeButton, OnSelectTypePressed);
            Bind(placeObjectButton, OnPlaceObjectPressed);
            Bind(undoObjectButton, OnUndoObjectPressed);
            Bind(widthPlusButton, () => AdjustLast((w, d, h, y) => (w + DimensionStepM, d, h, y)));
            Bind(widthMinusButton, () => AdjustLast((w, d, h, y) => (w - DimensionStepM, d, h, y)));
            Bind(depthPlusButton, () => AdjustLast((w, d, h, y) => (w, d + DimensionStepM, h, y)));
            Bind(depthMinusButton, () => AdjustLast((w, d, h, y) => (w, d - DimensionStepM, h, y)));
            Bind(heightPlusButton, () => AdjustLast((w, d, h, y) => (w, d, h + DimensionStepM, y)));
            Bind(heightMinusButton, () => AdjustLast((w, d, h, y) => (w, d, h - DimensionStepM, y)));
            Bind(yawPlusButton, () => AdjustLast((w, d, h, y) => (w, d, h, y + YawStepDeg)));
            Bind(yawMinusButton, () => AdjustLast((w, d, h, y) => (w, d, h, y - YawStepDeg)));
            Bind(finishObjectsButton, OnFinishObjectsPressed);
        }

        private void OnDestroy()
        {
            Unbind(selectTypeButton, OnSelectTypePressed);
            Unbind(placeObjectButton, OnPlaceObjectPressed);
            Unbind(undoObjectButton, OnUndoObjectPressed);
            Unbind(finishObjectsButton, OnFinishObjectsPressed);

            if (widthPlusButton != null) widthPlusButton.onClick.RemoveAllListeners();
            if (widthMinusButton != null) widthMinusButton.onClick.RemoveAllListeners();
            if (depthPlusButton != null) depthPlusButton.onClick.RemoveAllListeners();
            if (depthMinusButton != null) depthMinusButton.onClick.RemoveAllListeners();
            if (heightPlusButton != null) heightPlusButton.onClick.RemoveAllListeners();
            if (heightMinusButton != null) heightMinusButton.onClick.RemoveAllListeners();
            if (yawPlusButton != null) yawPlusButton.onClick.RemoveAllListeners();
            if (yawMinusButton != null) yawMinusButton.onClick.RemoveAllListeners();
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
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

            bool inObjectsPhase = workflow.Phase == ScanPhase.AddObjects;

            ObjectPlacementController placement = workflow.Objects;

            SetActive(selectTypeButton, inObjectsPhase);
            SetActive(placeObjectButton, inObjectsPhase);
            SetActive(undoObjectButton, inObjectsPhase);
            SetActive(widthPlusButton, inObjectsPhase);
            SetActive(widthMinusButton, inObjectsPhase);
            SetActive(depthPlusButton, inObjectsPhase);
            SetActive(depthMinusButton, inObjectsPhase);
            SetActive(heightPlusButton, inObjectsPhase);
            SetActive(heightMinusButton, inObjectsPhase);
            SetActive(yawPlusButton, inObjectsPhase);
            SetActive(yawMinusButton, inObjectsPhase);
            SetActive(finishObjectsButton, inObjectsPhase);

            if (!inObjectsPhase)
            {
                ClearMarker(ref aimMarker);
                ClearObjectMarkers();

                if (readoutText != null)
                {
                    readoutText.text = string.Empty;
                }

                return;
            }

            UpdateControls(placement);
            UpdateMarkers(workflow.Frame, placement);

            if (readoutText != null)
            {
                readoutText.text = BuildReadout(placement);
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

        private void UpdateControls(ObjectPlacementController placement)
        {
            if (selectTypeLabel != null)
            {
                selectTypeLabel.text = $"Type: {placement.SelectedType}";
            }

            if (placeObjectButton != null)
            {
                placeObjectButton.interactable = placement.CanPlace;
            }

            if (undoObjectButton != null)
            {
                undoObjectButton.interactable = placement.ObjectCount > 0;
            }

            bool hasLastObject = placement.ObjectCount > 0;

            SetInteractable(widthPlusButton, hasLastObject);
            SetInteractable(widthMinusButton, hasLastObject);
            SetInteractable(depthPlusButton, hasLastObject);
            SetInteractable(depthMinusButton, hasLastObject);
            SetInteractable(heightPlusButton, hasLastObject);
            SetInteractable(heightMinusButton, hasLastObject);
            SetInteractable(yawPlusButton, hasLastObject);
            SetInteractable(yawMinusButton, hasLastObject);
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private void OnSelectTypePressed()
        {
            ScanWorkflowController workflow = Workflow;
            ObjectPlacementController placement = workflow?.Objects;

            if (workflow == null || placement == null)
            {
                return;
            }

            IReadOnlyList<string> types = FurnitureValidator.SupportedTypes;
            int currentIndex = 0;

            for (int i = 0; i < types.Count; i++)
            {
                if (types[i] == placement.SelectedType)
                {
                    currentIndex = i;
                    break;
                }
            }

            string next = types[(currentIndex + 1) % types.Count];
            workflow.SetObjectType(next);
        }

        private void OnPlaceObjectPressed() => Workflow?.TryPlaceObject(out _);

        private void OnUndoObjectPressed() => Workflow?.TryUndoLastObject(out _);

        private void OnFinishObjectsPressed() => Workflow?.FinishAddingObjects();

        /// <summary>
        /// Applies a step to the most recently placed object's
        /// width/depth/height/yaw. <paramref name="step"/> receives the
        /// object's current values and returns the requested ones; only one
        /// of the four normally changes per call.
        /// </summary>
        private void AdjustLast(System.Func<float, float, float, float, (float W, float D, float H, float Y)> step)
        {
            ScanWorkflowController workflow = Workflow;
            ObjectPlacementController placement = workflow?.Objects;

            if (workflow == null || placement == null || placement.ObjectCount == 0)
            {
                return;
            }

            int lastIndex = placement.ObjectCount - 1;
            SceneObjectModel current = placement.Objects[lastIndex];

            (float w, float d, float h, float y) = step(
                current.widthM, current.depthM, current.heightM, current.yawDeg);

            if (!Mathf.Approximately(w, current.widthM))
            {
                workflow.TrySetObjectWidth(lastIndex, w, out _);
            }
            else if (!Mathf.Approximately(d, current.depthM))
            {
                workflow.TrySetObjectDepth(lastIndex, d, out _);
            }
            else if (!Mathf.Approximately(h, current.heightM))
            {
                workflow.TrySetObjectHeight(lastIndex, h, out _);
            }
            else if (!Mathf.Approximately(y, current.yawDeg))
            {
                workflow.TrySetObjectYaw(lastIndex, y, out _);
            }
        }

        // -------------------------------------------------------------------
        // Markers
        // -------------------------------------------------------------------

        private void UpdateMarkers(GhostCoordinateFrame frame, ObjectPlacementController placement)
        {
            if (frame == null)
            {
                ClearMarker(ref aimMarker);
                ClearObjectMarkers();
                return;
            }

            if (placement.TryProjectCrosshairToFloor(out Vector3 ghostAim))
            {
                if (aimMarker == null)
                {
                    aimMarker = CreateMarker("ObjectAimMarker", ObjectMarkerDiameterM * 0.6f);
                }

                SetMarker(aimMarker, frame.GhostToWorld(ghostAim), AimMarkerColor);
            }
            else
            {
                ClearMarker(ref aimMarker);
            }

            IReadOnlyList<SceneObjectModel> objects = placement.Objects;

            while (objectMarkers.Count > objects.Count)
            {
                int last = objectMarkers.Count - 1;
                Destroy(objectMarkers[last]);
                objectMarkers.RemoveAt(last);
            }

            while (objectMarkers.Count < objects.Count)
            {
                objectMarkers.Add(CreateMarker($"Object {objectMarkers.Count + 1}", ObjectMarkerDiameterM));
            }

            for (int i = 0; i < objects.Count; i++)
            {
                SetMarker(objectMarkers[i], frame.GhostToWorld(objects[i].center.ToVector3()), PlacedObjectColor);
            }
        }

        private GameObject CreateMarker(string markerName, float diameterM)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = markerName;
            marker.transform.SetParent(transform, worldPositionStays: false);
            marker.transform.localScale = Vector3.one * diameterM;

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

        private void ClearMarker(ref GameObject marker)
        {
            if (marker != null)
            {
                Destroy(marker);
                marker = null;
            }
        }

        private void ClearObjectMarkers()
        {
            for (int i = 0; i < objectMarkers.Count; i++)
            {
                Destroy(objectMarkers[i]);
            }

            objectMarkers.Clear();
        }

        // -------------------------------------------------------------------
        // Readout
        // -------------------------------------------------------------------

        private string BuildReadout(ObjectPlacementController placement)
        {
            builder.Clear();

            builder.Append("Objects ").Append(placement.ObjectCount)
                .Append(" | next type ").Append(placement.SelectedType).Append('\n');

            if (placement.TryProjectCrosshairToFloor(out Vector3 ghost))
            {
                builder.AppendFormat("Aim G({0:F2},{1:F2},{2:F2})\n", ghost.x, ghost.y, ghost.z);
            }
            else
            {
                builder.Append("Aim: no floor intersection\n");
            }

            IReadOnlyList<SceneObjectModel> objects = placement.Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                SceneObjectModel model = objects[i];
                builder.AppendFormat(
                    "  {0} {1} W{2:F2} D{3:F2} H{4:F2} yaw{5:F0}\n",
                    i + 1, model.type, model.widthM, model.depthM, model.heightM, model.yawDeg);
            }

            if (!string.IsNullOrEmpty(placement.LastError))
            {
                builder.Append("Rejected: ").Append(placement.LastError).Append('\n');
            }
            else if (placement.LastRejection != ObjectPlacementRejection.None)
            {
                builder.Append("Rejected: ").Append(placement.LastRejection).Append('\n');
            }

            return builder.ToString();
        }
    }
}
