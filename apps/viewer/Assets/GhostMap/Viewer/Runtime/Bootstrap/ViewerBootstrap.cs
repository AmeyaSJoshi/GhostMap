using GhostMap.Shared.Protocol;
using UnityEngine;

namespace GhostMap.Viewer.Bootstrap
{
    /// <summary>
    /// Task V1's entry point: the thin MonoBehaviour wrapper that owns one
    /// <see cref="ViewerSession"/> and ticks it every frame. All actual
    /// networking/scene-store logic lives in <see cref="ViewerSession"/>,
    /// which has no Unity dependency and is what tests drive directly.
    /// </summary>
    public sealed class ViewerBootstrap : MonoBehaviour
    {
        [SerializeField] private int port = ProtocolConstants.Port;

        public ViewerSession Session { get; private set; }

        private void Awake()
        {
            Session = new ViewerSession(port);
            Session.Start();
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
