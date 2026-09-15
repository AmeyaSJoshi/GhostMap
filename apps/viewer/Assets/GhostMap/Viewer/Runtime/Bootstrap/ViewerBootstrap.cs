using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Rendering;
using UnityEngine;

namespace GhostMap.Viewer.Bootstrap
{
    /// <summary>
    /// Task V1's entry point: the thin MonoBehaviour wrapper that owns one
    /// <see cref="ViewerSession"/> and ticks it every frame. All actual
    /// networking/scene-store logic lives in <see cref="ViewerSession"/>,
    /// which has no Unity dependency and is what tests drive directly.
    ///
    /// Tasks V2 and V4 add the lines connecting rendering and the camera to
    /// that session: the
    /// optional <see cref="RoomRenderer"/> is attached to the session's
    /// <see cref="ViewerSceneStore"/> here, in the presentation-layer glue,
    /// never inside <see cref="ViewerSession"/> itself (AGENTS.md: the TCP
    /// server must not be responsible for creating Unity GameObjects).
    /// </summary>
    public sealed class ViewerBootstrap : MonoBehaviour
    {
        [SerializeField] private int port = ProtocolConstants.Port;
        [SerializeField] private RoomRenderer roomRenderer;
        [SerializeField] private OrbitCameraController cameraController;

        public ViewerSession Session { get; private set; }

        private void Awake()
        {
            Session = new ViewerSession(port);
            Session.Start();

            if (roomRenderer != null)
            {
                roomRenderer.Attach(Session.SceneStore);
            }

            // Task V4: the camera frames the room from the same accepted scene
            // state the renderer draws, never from a socket message.
            if (cameraController != null)
            {
                cameraController.Attach(Session.SceneStore);
            }
        }

        private void Update()
        {
            Session?.Pump();
        }

        private void OnDestroy()
        {
            Session?.Dispose();
        }
    }
}
