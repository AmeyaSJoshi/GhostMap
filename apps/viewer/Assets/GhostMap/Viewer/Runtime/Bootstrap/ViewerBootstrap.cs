using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using UnityEngine;

namespace GhostMap.Viewer.Bootstrap
{
    /// <summary>
    /// Task V1's entry point: the thin MonoBehaviour wrapper that owns one
    /// <see cref="ViewerSession"/> and ticks it every frame. All actual
    /// networking/scene-store logic lives in <see cref="ViewerSession"/>,
    /// which has no Unity dependency and is what tests drive directly.
    ///
    /// Tasks V2, V4 and V5 add the lines connecting rendering, the camera and
    /// interaction to that session in the presentation-layer glue, never
    /// inside <see cref="ViewerSession"/> itself (AGENTS.md: the TCP server
    /// must not be responsible for creating Unity GameObjects).
    ///
    /// <para><b>Task V5:</b> owns the one <see cref="ViewerEditableScene"/>
    /// that sits between the scanner-authoritative
    /// <see cref="ViewerSceneStore"/> and every consumer. Rendering, the
    /// camera and every interaction controller attach to
    /// <see cref="EditableScene"/>, never to <see cref="ViewerSession.SceneStore"/>
    /// directly, so they all see the same effective scene: live scanner
    /// updates before finalization, the locally-edited copy after.</para>
    /// </summary>
    public sealed class ViewerBootstrap : MonoBehaviour
    {
        [SerializeField] private int port = ProtocolConstants.Port;
        [SerializeField] private RoomRenderer roomRenderer;
        [SerializeField] private OrbitCameraController cameraController;
        [SerializeField] private ObjectSelectionController selectionController;
        [SerializeField] private ObjectEditController editController;
        [SerializeField] private MeasurementController measurementController;

        public ViewerSession Session { get; private set; }

        public ViewerEditableScene EditableScene { get; private set; }

        private void Awake()
        {
            Session = new ViewerSession(port);
            Session.Start();

            EditableScene = new ViewerEditableScene();
            EditableScene.Attach(Session.SceneStore);

            if (roomRenderer != null)
            {
                roomRenderer.Attach(EditableScene);
            }

            // Task V4: the camera frames the room from the same accepted scene
            // state the renderer draws, never from a socket message.
            if (cameraController != null)
            {
                cameraController.Attach(EditableScene);
            }

            if (selectionController != null)
            {
                selectionController.SetRoomRenderer(roomRenderer);
                selectionController.Attach();
            }

            if (editController != null)
            {
                editController.SetSelectionController(selectionController);
                editController.Attach(EditableScene);
            }

            if (measurementController != null)
            {
                measurementController.Attach(EditableScene);
            }
        }

        private void Update()
        {
            Session?.Pump();
        }

        private void OnDestroy()
        {
            selectionController?.Detach();
            editController?.Detach();
            measurementController?.Detach();
            roomRenderer?.Detach();
            cameraController?.Detach();
            EditableScene?.Detach();
            Session?.Dispose();
        }
    }
}
