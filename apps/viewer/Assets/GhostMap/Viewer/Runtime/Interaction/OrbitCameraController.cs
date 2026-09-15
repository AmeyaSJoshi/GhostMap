using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using UnityEngine;

namespace GhostMap.Viewer.Interaction
{
    /// <summary>
    /// Task V4: desktop room navigation, with exactly the control scheme
    /// implementation plan section 13.1 specifies —
    /// left drag orbits, right or middle drag pans, scroll zooms, `F` frames
    /// the whole room, `D` toggles the dollhouse preset.
    ///
    /// All the maths and every clamp live in <see cref="OrbitCameraRig"/>;
    /// this class only reads input and pushes the result onto the transform,
    /// so the camera's behaviour is fully testable without a Play-mode loop.
    ///
    /// Like <see cref="RoomRenderer"/>, it listens to
    /// <see cref="ViewerSceneStore.Changed"/> and never to the TCP server:
    /// framing follows accepted scene state. The first accepted scene frames
    /// itself automatically; later ones only refresh the bounds, so a snapshot
    /// arriving mid-inspection never yanks the view out of the user's hands.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCameraController : MonoBehaviour
    {
        [SerializeField] private RoomRenderer roomRenderer;

        [Header("Sensitivity")]
        [SerializeField] private float orbitDegreesPerPixel = 0.35f;
        [SerializeField] private float panMetersPerPixel = 0.01f;
        [SerializeField] private float zoomStepsPerScrollUnit = 3f;

        private IViewerSceneSource _sceneSource;
        private Camera _camera;
        private Bounds _roomBounds;
        private bool _hasBounds;
        private bool _hasFramed;

        public OrbitCameraRig Rig { get; } = new OrbitCameraRig();

        public bool DollhouseEnabled { get; private set; }

        /// <summary>
        /// Task V5: suspended while <c>ViewerInteractionRouter</c> is dragging
        /// a selected furniture object on the floor plane, so a left-drag
        /// that starts on the selection never also orbits the camera
        /// underneath it (section "Camera + interaction conflicts").
        /// Keyboard shortcuts and framing are unaffected.
        /// </summary>
        public bool InputEnabled { get; set; } = true;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        /// <summary>Editor/test wiring for the renderer whose ceiling the
        /// dollhouse preset hides.</summary>
        public void SetRoomRenderer(RoomRenderer renderer)
        {
            roomRenderer = renderer;
        }

        public void Attach(IViewerSceneSource sceneSource)
        {
            Detach();

            _sceneSource = sceneSource;
            if (_sceneSource == null)
            {
                return;
            }

            _sceneSource.Changed += OnSceneChanged;

            if (_sceneSource.Current != null)
            {
                OnSceneChanged(_sceneSource.Current);
            }
        }

        public void Detach()
        {
            if (_sceneSource != null)
            {
                _sceneSource.Changed -= OnSceneChanged;
                _sceneSource = null;
            }

            _hasFramed = false;
        }

        private void OnDestroy()
        {
            Detach();
        }

        private void OnSceneChanged(SceneSnapshot snapshot)
        {
            _hasBounds = RoomBounds.TryCompute(snapshot?.room, out Bounds bounds);
            _roomBounds = bounds;

            if (!_hasBounds || _hasFramed)
            {
                return;
            }

            // The first room to arrive frames itself; after that the user owns
            // the camera until they ask for F or D.
            FrameRoom();
            _hasFramed = true;
        }

        /// <summary>Section 13.1's `F`, and the reset/home view.</summary>
        public void FrameRoom()
        {
            if (!_hasBounds)
            {
                return;
            }

            Rig.Frame(_roomBounds, FieldOfView, Aspect);
            ApplyToTransform();
        }

        /// <summary>Section 13.1's `D`: angled overhead view, framed on the
        /// room, with the ceiling out of the way so the interior is visible
        /// (section 12.2).</summary>
        public void SetDollhouse(bool enabled)
        {
            DollhouseEnabled = enabled;

            if (roomRenderer != null)
            {
                roomRenderer.SetCeilingVisible(!enabled);
            }

            if (!_hasBounds)
            {
                return;
            }

            if (enabled)
            {
                Rig.Dollhouse(_roomBounds, FieldOfView, Aspect);
            }
            else
            {
                Rig.Frame(_roomBounds, FieldOfView, Aspect);
            }

            ApplyToTransform();
        }

        public void ToggleDollhouse() => SetDollhouse(!DollhouseEnabled);

        public void ApplyToTransform()
        {
            if (!Rig.IsValid)
            {
                return;
            }

            transform.SetPositionAndRotation(Rig.Position, Rig.Rotation);
        }

        private float FieldOfView => _camera != null ? _camera.fieldOfView : 60f;

        private float Aspect => _camera != null && _camera.aspect > 0.01f
            ? _camera.aspect
            : 16f / 9f;

        private void Update()
        {
            if (InputEnabled)
            {
                ReadMouse();
                ReadKeys();
            }

            ApplyToTransform();
        }

        private void ReadMouse()
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            // Mouse axes are already frame-rate normalised by the input
            // manager, so orbit/pan must NOT be multiplied by deltaTime as
            // well — that would make the camera speed depend on frame rate.
            if (Input.GetMouseButton(0))
            {
                Rig.Orbit(mouseX * orbitDegreesPerPixel * 10f, -mouseY * orbitDegreesPerPixel * 10f);
            }
            else if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                Rig.Pan(
                    -mouseX * panMetersPerPixel * Rig.Distance,
                    -mouseY * panMetersPerPixel * Rig.Distance);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!Mathf.Approximately(scroll, 0f))
            {
                Rig.Zoom(scroll * zoomStepsPerScrollUnit * 10f);
            }
        }

        private void ReadKeys()
        {
            if (Input.GetKeyDown(KeyCode.F))
            {
                FrameRoom();
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                ToggleDollhouse();
            }
        }
    }
}
