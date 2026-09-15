using GhostMap.Shared.Protocol;
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
    /// Task V2 adds the one line connecting rendering to that session: the
    /// optional <see cref="RoomRenderer"/> is attached to the session's
    /// <see cref="ViewerSceneStore"/> here, in the presentation-layer glue,
    /// never inside <see cref="ViewerSession"/> itself (AGENTS.md: the TCP
    /// server must not be responsible for creating Unity GameObjects).
    /// </summary>
    public sealed class ViewerBootstrap : MonoBehaviour
    {
        [SerializeField] private int port = ProtocolConstants.Port;
        [SerializeField] private RoomRenderer roomRenderer;

        public ViewerSession Session { get; private set; }

        private void Awake()
        {
            Session = new ViewerSession(port);
            Session.Start();

            if (roomRenderer != null)
            {
                roomRenderer.Attach(Session.SceneStore);
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
