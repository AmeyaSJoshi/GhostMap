using System;
using System.Globalization;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Viewer.UI
{
    /// <summary>
    /// Task V5's inspector (implementation plan section 13.2/13.4): shows the
    /// selected object's type/id and editable position X/Z, yaw, width,
    /// depth, height as legacy UGUI <see cref="InputField"/>s, plus the
    /// measurement toggle/clear buttons and the current measurement readout.
    ///
    /// Every field is driven straight from <see cref="ObjectSelectionController.GetSelectedModel"/>
    /// each frame — the same "repaint from current truth" style
    /// <see cref="ViewerHudController"/> already uses — except a field the
    /// user is actively typing into (<see cref="InputField.isFocused"/>),
    /// which is never overwritten out from under them. Submitting a field
    /// calls straight into <see cref="ObjectEditController"/>'s matching
    /// Try* method; a rejected edit (disabled, invalid) leaves the model
    /// untouched and the next repaint snaps the field back to the real value.
    /// </summary>
    public sealed class InspectorPanelController : MonoBehaviour
    {
        [SerializeField] private ObjectSelectionController selection;
        [SerializeField] private ObjectEditController edit;
        [SerializeField] private MeasurementController measurement;

        [SerializeField] private Text selectedInfoText;
        [SerializeField] private Text editStatusText;

        [SerializeField] private InputField positionXField;
        [SerializeField] private InputField positionZField;
        [SerializeField] private InputField yawField;
        [SerializeField] private InputField widthField;
        [SerializeField] private InputField depthField;
        [SerializeField] private InputField heightField;

        [SerializeField] private Button measureToggleButton;
        [SerializeField] private Text measureToggleLabel;
        [SerializeField] private Button clearMeasurementButton;
        [SerializeField] private Text measurementText;

        private string _lastError = string.Empty;

        public void SetSelectionController(ObjectSelectionController controller) => selection = controller;
        public void SetEditController(ObjectEditController controller) => edit = controller;
        public void SetMeasurementController(MeasurementController controller) => measurement = controller;

        private void Awake()
        {
            WireField(positionXField, value => edit.TrySetPositionXZ(value, CurrentZ(), out _lastError));
            WireField(positionZField, value => edit.TrySetPositionXZ(CurrentX(), value, out _lastError));
            WireField(yawField, value => edit.TrySetYaw(value, out _lastError));
            WireField(widthField, value => edit.TrySetWidth(value, out _lastError));
            WireField(depthField, value => edit.TrySetDepth(value, out _lastError));
            WireField(heightField, value => edit.TrySetHeight(value, out _lastError));

            if (measureToggleButton != null)
            {
                measureToggleButton.onClick.AddListener(() => measurement?.ToggleActive());
            }

            if (clearMeasurementButton != null)
            {
                clearMeasurementButton.onClick.AddListener(() => measurement?.Clear());
            }
        }

        private float CurrentX() => selection != null ? selection.GetSelectedModel()?.center.x ?? 0f : 0f;

        private float CurrentZ() => selection != null ? selection.GetSelectedModel()?.center.z ?? 0f : 0f;

        private void WireField(InputField field, Func<float, bool> apply)
        {
            if (field == null)
            {
                return;
            }

            field.onEndEdit.AddListener(text =>
            {
                if (edit == null)
                {
                    return;
                }

                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    apply(value);
                }
                else
                {
                    _lastError = $"'{text}' is not a number.";
                }
            });
        }

        private void Update()
        {
            RefreshSelection();
            RefreshMeasurement();
        }

        private void RefreshSelection()
        {
            SceneObjectModel model = selection != null ? selection.GetSelectedModel() : null;
            bool editingEnabled = edit != null && edit.EditingEnabled;

            if (selectedInfoText != null)
            {
                selectedInfoText.text = model != null
                    ? $"Selected: {model.type} ({model.id})"
                    : "Selected: none";
            }

            if (editStatusText != null)
            {
                string state = editingEnabled
                    ? "Editing: ENABLED"
                    : "Editing: disabled (finalize scan to edit)";
                editStatusText.text = string.IsNullOrEmpty(_lastError) ? state : $"{state}\n{_lastError}";
            }

            bool interactable = model != null && editingEnabled;
            SetInteractable(positionXField, interactable);
            SetInteractable(positionZField, interactable);
            SetInteractable(yawField, interactable);
            SetInteractable(widthField, interactable);
            SetInteractable(depthField, interactable);
            SetInteractable(heightField, interactable);

            if (model == null)
            {
                return;
            }

            RefreshField(positionXField, model.center.x);
            RefreshField(positionZField, model.center.z);
            RefreshField(yawField, model.yawDeg);
            RefreshField(widthField, model.widthM);
            RefreshField(depthField, model.depthM);
            RefreshField(heightField, model.heightM);
        }

        private void RefreshMeasurement()
        {
            if (measurement == null)
            {
                return;
            }

            if (measureToggleLabel != null)
            {
                measureToggleLabel.text = measurement.IsActive ? "Exit Measure (M)" : "Measure (M)";
            }

            if (measurementText == null)
            {
                return;
            }

            if (!measurement.IsActive)
            {
                measurementText.text = "Measurement: off";
            }
            else if (!measurement.PointA.HasValue)
            {
                measurementText.text = "Measurement: click a first point";
            }
            else if (!measurement.HasMeasurement)
            {
                measurementText.text = "Measurement: click a second point";
            }
            else
            {
                measurementText.text =
                    $"Distance: {measurement.DistanceM:F2} m  (XZ {measurement.DistanceXZM:F2} m)";
            }
        }

        private static void SetInteractable(InputField field, bool interactable)
        {
            if (field != null)
            {
                field.interactable = interactable;
            }
        }

        private static void RefreshField(InputField field, float value)
        {
            if (field == null || field.isFocused)
            {
                return;
            }

            field.SetTextWithoutNotify(value.ToString("F2", CultureInfo.InvariantCulture));
        }
    }
}
