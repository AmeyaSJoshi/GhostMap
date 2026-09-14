using System.Text;
using System.Threading;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Networking;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// Task S6's scene-facing shell for networking and finalization: a
    /// connection screen (laptop IP, port, Connect), a live network-status
    /// readout, Reset, Finalize, and one consolidated status line — phase,
    /// tracking quality, network status and closure error together — so the
    /// scan workflow is understandable without reading every per-phase debug
    /// readout the S2-S5 HUDs still carry for device verification.
    ///
    /// <para>All networking logic lives in <see cref="ScannerNetworkClient"/>
    /// and <see cref="ScannerSnapshotPublisher"/>. This class only owns the
    /// background thread that drives <see cref="ScannerNetworkClient.PumpOnce"/>
    /// on device (implementation plan section 7 / Task S6: "background
    /// read/write loop", "no Unity API calls from network background thread")
    /// and forwards three button presses, so nothing here needs a device to be
    /// trusted — only <see cref="ScannerNetworkClient"/> and
    /// <see cref="ScannerSnapshotPublisher"/> need EditMode tests.</para>
    ///
    /// <para><see cref="FloorLockHud"/> remains the scene's composition root
    /// for the scan workflow itself; this class reads
    /// <see cref="FloorLockHud.Workflow"/> freshly every frame, the same
    /// pattern every other HUD uses, so a Reset (which replaces the whole
    /// controller graph) is picked up automatically by re-binding the
    /// publisher rather than by this class caching anything stale.</para>
    /// </summary>
    public sealed class ScannerHudController : MonoBehaviour
    {
        private const int NetworkThreadPollIntervalMs = 100;

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private InputField hostInput;
        [SerializeField] private InputField portInput;
        [SerializeField] private Button connectButton;
        [SerializeField] private Text networkStatusText;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button finalizeButton;
        [SerializeField] private Text statusText;

        private readonly StringBuilder builder = new StringBuilder();

        private ScannerNetworkClient client;
        private ScannerSnapshotPublisher publisher;
        private ScanWorkflowController boundWorkflow;

        private Thread networkThread;
        private volatile bool networkThreadRunning;

        private ScanWorkflowController Workflow => floorLockHud != null ? floorLockHud.Workflow : null;

        private void Awake()
        {
            client = new ScannerNetworkClient(
                () => boundWorkflow?.Snapshot,
                Application.version,
                SystemInfo.deviceName);

            publisher = new ScannerSnapshotPublisher(client);
            RebindIfNeeded();

            if (connectButton != null)
            {
                connectButton.onClick.AddListener(OnConnectPressed);
            }

            if (resetButton != null)
            {
                resetButton.onClick.AddListener(OnResetPressed);
            }

            if (finalizeButton != null)
            {
                finalizeButton.onClick.AddListener(OnFinalizePressed);
            }

            networkThreadRunning = true;
            networkThread = new Thread(NetworkThreadLoop) { IsBackground = true, Name = "GhostMapScannerNetwork" };
            networkThread.Start();
        }

        private void OnDestroy()
        {
            networkThreadRunning = false;
            networkThread?.Join(NetworkThreadPollIntervalMs * 5);
            client?.Dispose();

            if (connectButton != null)
            {
                connectButton.onClick.RemoveListener(OnConnectPressed);
            }

            if (resetButton != null)
            {
                resetButton.onClick.RemoveListener(OnResetPressed);
            }

            if (finalizeButton != null)
            {
                finalizeButton.onClick.RemoveListener(OnFinalizePressed);
            }
        }

        /// <summary>
        /// The only thing this component's background thread touches. No
        /// UnityEngine API is called from here — <see cref="ScannerNetworkClient.PumpOnce"/>
        /// only touches sockets and its own outgoing queue.
        /// </summary>
        private void NetworkThreadLoop()
        {
            while (networkThreadRunning)
            {
                client.PumpOnce();
                Thread.Sleep(NetworkThreadPollIntervalMs);
            }
        }

        private void Update()
        {
            if (Workflow == null)
            {
                return;
            }

            RebindIfNeeded();
            publisher.Tick();

            UpdateControls();

            if (networkStatusText != null)
            {
                networkStatusText.text = BuildNetworkStatus();
            }

            if (statusText != null)
            {
                statusText.text = BuildStatus();
            }
        }

        /// <summary>
        /// Picks up a Reset's fresh <see cref="ScanWorkflowController"/>: the
        /// publisher must forget the previous session's last-seen revision so
        /// the new session's revision-0 snapshot is treated as a change worth
        /// publishing, not a duplicate.
        /// </summary>
        private void RebindIfNeeded()
        {
            ScanWorkflowController current = Workflow;

            if (!ReferenceEquals(current, boundWorkflow))
            {
                boundWorkflow = current;
                publisher.Rebind(current);
            }
        }

        private void UpdateControls()
        {
            if (finalizeButton != null)
            {
                finalizeButton.interactable = Workflow.Phase == ScanPhase.ReadyToFinalize;
            }
        }

        private void OnConnectPressed()
        {
            string targetHost = hostInput != null ? hostInput.text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(targetHost))
            {
                return;
            }

            int targetPort = ProtocolConstants.Port;

            if (portInput != null && !string.IsNullOrEmpty(portInput.text))
            {
                int.TryParse(portInput.text, out targetPort);
            }

            client.RequestConnect(targetHost, targetPort);
        }

        private void OnResetPressed() => floorLockHud?.ResetScan();

        private void OnFinalizePressed() => Workflow?.TryFinalize(out _);

        private string BuildNetworkStatus()
        {
            builder.Clear();
            builder.Append("Network: ").Append(client.State);

            if (!string.IsNullOrEmpty(client.Host))
            {
                builder.Append(" (").Append(client.Host).Append(':').Append(client.Port).Append(')');
            }

            if (client.State != NetworkConnectionState.Connected && !string.IsNullOrEmpty(client.LastError))
            {
                builder.Append(" — ").Append(client.LastError);
            }

            return builder.ToString();
        }

        private string BuildStatus()
        {
            ScanWorkflowController workflow = Workflow;

            builder.Clear();
            builder.Append("Phase: ").Append(workflow.Phase).Append(" | rev ").Append(workflow.Revision).Append('\n');

            if (spatialProvider != null)
            {
                builder.Append("Tracking: ").Append(spatialProvider.IsTrackingGood ? "good" : "poor")
                    .Append(" (").Append(spatialProvider.NotTrackingReason).Append(")\n");
            }

            builder.Append("Closure error: ").Append(workflow.Snapshot.closureErrorM.ToString("F3")).Append(" m\n");

            builder.Append(workflow.Phase == ScanPhase.Finalized ? "FINALIZED" : "Not finalized");

            return builder.ToString();
        }
    }
}
