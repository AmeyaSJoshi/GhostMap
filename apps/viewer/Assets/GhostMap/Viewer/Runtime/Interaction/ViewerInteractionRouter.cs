using UnityEngine;
using UnityEngine.EventSystems;

namespace GhostMap.Viewer.Interaction
{
    /// <summary>
    /// Task V5's input coordinator (implementation plan "Camera + interaction
    /// conflicts"): the single place that reads the mouse each frame and
    /// decides whether a gesture is a click (selection, or a measurement
    /// point placement), a furniture drag, or neither — leaving
    /// <see cref="OrbitCameraController"/>'s own orbit/pan/zoom reads
    /// untouched except for a deliberate suspension while a furniture drag is
    /// in progress.
    ///
    /// Rules, in priority order:
    /// <list type="number">
    /// <item>a click that starts over a UI element
    /// (<see cref="EventSystem.IsPointerOverGameObject"/>) is ignored
    /// entirely, so a button press never also selects or measures whatever is
    /// behind it;</item>
    /// <item>while measurement mode is active, every click places a
    /// measurement point — it never selects furniture, even if the click
    /// lands on an object ("measurement mode intercepts click placement");</item>
    /// <item>a mouse-down that lands on the already-selected object's own
    /// collider, with editing enabled and not measuring, begins a floor-plane
    /// drag instead of an orbit, and suspends
    /// <see cref="OrbitCameraController.InputEnabled"/> for the duration;</item>
    /// <item>otherwise a genuine click (movement under
    /// <see cref="clickDragThresholdPx"/>) selects whatever is under the
    /// cursor, or clears the selection if nothing is there.</item>
    /// </list>
    ///
    /// <c>M</c> toggles measurement mode, mirroring V4's on-screen-button-plus-key
    /// pattern for <c>F</c>/<c>D</c>; <see cref="UI.InspectorPanelController"/>'s
    /// Measure button does the same thing.
    ///
    /// Deliberately thin and untestable in EditMode for the same reason
    /// <see cref="OrbitCameraController"/>'s own <c>Update()</c> is (V4 known
    /// issues): it only reads <c>Input</c> and dispatches. Every command it
    /// dispatches to — <see cref="ObjectSelectionController.TrySelectAt(Ray)"/>,
    /// <see cref="ObjectEditController.TryDragToFloorPoint"/>,
    /// <see cref="MeasurementController.TryPlacePoint"/> — is covered directly
    /// by EditMode tests with explicit rays. Only the click/drag threshold
    /// itself, <see cref="IsClick"/>, is a pure function and is tested here.
    /// </summary>
    public sealed class ViewerInteractionRouter : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private ObjectSelectionController selection;
        [SerializeField] private ObjectEditController edit;
        [SerializeField] private MeasurementController measurement;
        [SerializeField] private OrbitCameraController orbitCamera;
        [SerializeField] private float clickDragThresholdPx = 6f;

        private bool _isDraggingObject;
        private Vector2 _mouseDownPosition;

        public void SetCamera(Camera camera) => targetCamera = camera;
        public void SetSelectionController(ObjectSelectionController controller) => selection = controller;
        public void SetEditController(ObjectEditController controller) => edit = controller;
        public void SetMeasurementController(MeasurementController controller) => measurement = controller;
        public void SetOrbitCameraController(OrbitCameraController controller) => orbitCamera = controller;

        /// <summary>Pure: true if the pointer moved no more than
        /// <paramref name="thresholdPx"/> pixels between down and up, i.e. a
        /// click rather than a drag. Public so it is directly unit-testable,
        /// the same way <c>OrbitCameraRig</c>'s pure maths are.</summary>
        public static bool IsClick(Vector2 downPosition, Vector2 upPosition, float thresholdPx)
            => Vector2.Distance(downPosition, upPosition) <= thresholdPx;

        private void Update()
        {
            if (targetCamera == null || IsPointerOverUi())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                OnMouseDown();
            }

            if (_isDraggingObject && Input.GetMouseButton(0))
            {
                Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
                edit?.TryDragToFloorPoint(ray, out _);
            }

            if (Input.GetMouseButtonUp(0))
            {
                OnMouseUp();
            }

            if (Input.GetKeyDown(KeyCode.M))
            {
                measurement?.ToggleActive();
            }
        }

        private void OnMouseDown()
        {
            _mouseDownPosition = Input.mousePosition;

            bool measuring = measurement != null && measurement.IsActive;
            bool canDragSelection = !measuring && selection != null && edit != null && edit.EditingEnabled;

            if (canDragSelection)
            {
                Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
                if (selection.IsPointerOverSelected(ray))
                {
                    _isDraggingObject = true;
                    if (orbitCamera != null)
                    {
                        orbitCamera.InputEnabled = false;
                    }
                }
            }
        }

        private void OnMouseUp()
        {
            if (_isDraggingObject)
            {
                _isDraggingObject = false;
                if (orbitCamera != null)
                {
                    orbitCamera.InputEnabled = true;
                }
                return;
            }

            if (!IsClick(_mouseDownPosition, Input.mousePosition, clickDragThresholdPx))
            {
                return;
            }

            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);

            if (measurement != null && measurement.IsActive)
            {
                measurement.TryPlacePoint(ray);
            }
            else
            {
                selection?.TrySelectAt(ray);
            }
        }

        private static bool IsPointerOverUi()
            => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
