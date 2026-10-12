using System;
using System.IO;
using System.Text;
using System.Threading;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Export;
using GhostMap.Scanner.Networking;
using GhostMap.Scanner.Share;
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

        /// <summary>How long Restart waits for the confirming second tap.</summary>
        private const float ResetConfirmSeconds = 3f;

        private static readonly Color ConnectedDotColor = new Color(0.29f, 0.87f, 0.50f);
        private static readonly Color ConnectingDotColor = new Color(1f, 0.71f, 0.28f);
        private static readonly Color DisconnectedDotColor = new Color(1f, 1f, 1f, 0.45f);

        [SerializeField] private FloorLockHud floorLockHud;
        [SerializeField] private ArSpatialProvider spatialProvider;
        [SerializeField] private InputField hostInput;
        [SerializeField] private InputField portInput;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button sendToComputerButton;
        [SerializeField] private Button shareButton;
        [SerializeField] private Text networkStatusText;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button finalizeButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text resetLabel;
        [SerializeField] private Button connectionChipButton;
        [SerializeField] private Text connectionChipLabel;
        [SerializeField] private Graphic connectionChipDot;
        [SerializeField] private GameObject connectPanel;

        private readonly StringBuilder builder = new StringBuilder();

        private ScannerNetworkClient client;
        private PeerDiscoveryClient discovery;
        private ScannerSnapshotPublisher publisher;
        private ScanWorkflowController boundWorkflow;

        // Task R6: the export bundle written on finalize.
        private ScanExporter exporter;
        private ScanWorkflowController exportedWorkflow;
        private ExportResult lastExport;

        // Task R7: Send to Computer through the share sheet.
        private ShareController sharing;
        private ScanWorkflowController sharedWorkflow;

        private Thread networkThread;
        private volatile bool networkThreadRunning;

        private float resetArmedUntil = -1f;
        private bool connectPanelOpen;
        private NetworkConnectionState lastState = NetworkConnectionState.Disconnected;

        /// <summary>True once the computer has accepted the connection.</summary>
        public bool IsConnected => client != null && client.State == NetworkConnectionState.Connected;

        private ScanWorkflowController Workflow => floorLockHud != null ? floorLockHud.Workflow : null;

        /// <summary>Resources name of the browser viewer template (a copy of apps/web-viewer/dist/room-template.html).</summary>
        public const string RoomTemplateResource = "GhostMapRoomTemplate";

        /// <summary>
        /// The export of the current scan, or <c>null</c> if it has not been
        /// exported (not finalized yet, or Restart started a new scan).
        /// </summary>
        public ExportResult? CurrentExport =>
            exportedWorkflow != null && exportedWorkflow == Workflow ? lastExport : (ExportResult?)null;

        /// <summary>Saved rooms on this phone, newest first.</summary>
        public SavedScanStore SavedScans => exporter?.Store;

        /// <summary>Send to Computer (task R7): the share sheet and its last outcome.</summary>
        public ShareController Sharing => sharing;

        /// <summary>
        /// Whether the finished-scan Send to Computer button is offered: only
        /// once the scan is finalized and an export was attempted (a failed one
        /// is retried by pressing it).
        /// </summary>
        public static bool ShouldOfferSend(ScanPhase phase, ExportResult? export)
            => phase == ScanPhase.Finalized && export.HasValue;

        private void Awake()
        {
            client = new ScannerNetworkClient(
                () => boundWorkflow?.Snapshot,
                Application.version,
                SystemInfo.deviceName);

            publisher = new ScannerSnapshotPublisher(client);
            exporter = new ScanExporter(
                LoadRoomTemplate,
                new SavedScanStore(Path.Combine(Application.persistentDataPath, "scans")));
            sharing = new ShareController(ShareSheets.CreateDefault());
            discovery = new PeerDiscoveryClient();
            RebindIfNeeded();

            if (connectButton != null)
            {
                connectButton.onClick.AddListener(OnConnectPressed);
            }

            if (sendToComputerButton != null)
            {
                sendToComputerButton.onClick.AddListener(OnSendToComputerPressed);
            }

            if (shareButton != null)
            {
                shareButton.onClick.AddListener(OnSharePressed);
            }

            if (resetButton != null)
            {
                resetButton.onClick.AddListener(OnResetPressed);
            }

            if (finalizeButton != null)
            {
                finalizeButton.onClick.AddListener(OnFinalizePressed);
            }

            if (connectionChipButton != null)
            {
                connectionChipButton.onClick.AddListener(OnConnectionChipPressed);
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
            discovery?.Dispose();

            if (connectButton != null)
            {
                connectButton.onClick.RemoveListener(OnConnectPressed);
            }

            if (sendToComputerButton != null)
            {
                sendToComputerButton.onClick.RemoveListener(OnSendToComputerPressed);
            }

            if (shareButton != null)
            {
                shareButton.onClick.RemoveListener(OnSharePressed);
            }

            if (resetButton != null)
            {
                resetButton.onClick.RemoveListener(OnResetPressed);
            }

            if (finalizeButton != null)
            {
                finalizeButton.onClick.RemoveListener(OnFinalizePressed);
            }

            if (connectionChipButton != null)
            {
                connectionChipButton.onClick.RemoveListener(OnConnectionChipPressed);
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
            ConnectToDiscoveredViewerIfReady();

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
            ScanPhase phase = Workflow.Phase;

            if (finalizeButton != null)
            {
                finalizeButton.gameObject.SetActive(phase == ScanPhase.ReadyToFinalize);
                finalizeButton.interactable = phase == ScanPhase.ReadyToFinalize;
            }

            // Developer live view: find the desktop Viewer over the network.
            if (sendToComputerButton != null)
            {
                sendToComputerButton.interactable = discovery.State != PeerDiscoveryState.Searching;
            }

            if (shareButton != null)
            {
                bool offer = ShouldOfferSend(phase, CurrentExport);
                shareButton.gameObject.SetActive(offer);
                shareButton.interactable = offer && sharing.State != ShareState.Sharing;
            }

            if (resetLabel != null)
            {
                resetLabel.text = Time.unscaledTime < resetArmedUntil ? "Tap to confirm" : "Restart";
            }

            UpdateConnectionChip();
            UpdateConnectPanel(phase);
        }

        private void UpdateConnectionChip()
        {
            NetworkConnectionState state = client.State;

            // A fresh connection closes a panel the user opened: the job is done.
            if (state == NetworkConnectionState.Connected && lastState != NetworkConnectionState.Connected)
            {
                connectPanelOpen = false;
            }

            lastState = state;

            if (connectionChipLabel != null)
            {
                connectionChipLabel.text = state switch
                {
                    NetworkConnectionState.Connected => "Live view on",
                    NetworkConnectionState.Disconnected => "Live view",
                    _ => "Connecting\u2026"
                };
            }

            if (connectionChipDot != null)
            {
                connectionChipDot.color = state switch
                {
                    NetworkConnectionState.Connected => ConnectedDotColor,
                    NetworkConnectionState.Disconnected => DisconnectedDotColor,
                    _ => ConnectingDotColor
                };
            }
        }

        /// <summary>
        /// The connect card opens by itself at the end of the scan if the
        /// computer is not connected yet, because Finish & Send needs it; the
        /// user can still open or close it at any time from the chip.
        /// </summary>
        private void UpdateConnectPanel(ScanPhase phase)
        {
            if (connectPanel == null)
            {
                return;
            }

            // ADR-0013: the network connection is a developer tool, so the
            // card opens only from the chip; Send to Computer needs no network.
            connectPanel.SetActive(connectPanelOpen);
        }

        private void OnConnectionChipPressed()
        {
            connectPanelOpen = !(connectPanel != null && connectPanel.activeSelf);
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

        /// <summary>
        /// Developer live view (ADR-0013 section 5): discovers a running
        /// desktop Viewer on the local network and hands its TCP endpoint to
        /// the snapshot client. Not the product's Send to Computer.
        /// </summary>
        private void OnSendToComputerPressed()
        {
            discovery.Start();
        }

        /// <summary>
        /// Task R7: Send to Computer. Opens the share sheet with this scan's
        /// saved bundle. A failed export (for example a full disk) is retried
        /// first, so a finished room is never stuck unsaved.
        /// </summary>
        private void OnSharePressed()
        {
            if (Workflow?.Phase != ScanPhase.Finalized)
            {
                return;
            }

            if (CurrentExport is ExportResult failed && !failed.Succeeded)
            {
                ExportCurrentScan();
            }

            if (CurrentExport is ExportResult export && export.Succeeded)
            {
                ShareSaved(export.Saved.Path);
            }
        }

        /// <summary>Opens the share sheet for any saved room (also used by the saved-rooms list).</summary>
        public bool ShareSaved(string path)
        {
            sharedWorkflow = Workflow;
            return sharing.TryShare(path);
        }

        /// <summary>The share outcome for the current scan, or Idle after Restart.</summary>
        public ShareState CurrentShareState =>
            sharedWorkflow != null && sharedWorkflow == Workflow ? sharing.State : ShareState.Idle;

        public string CurrentShareMessage =>
            sharedWorkflow != null && sharedWorkflow == Workflow ? sharing.Message : string.Empty;

        private void ConnectToDiscoveredViewerIfReady()
        {
            if (discovery.TryTakeResult(out string host, out int port, out _))
            {
                client.RequestConnect(host, port);
                connectPanelOpen = false;
            }
        }

        /// <summary>
        /// Restart throws the whole scan away, so it takes a second tap within
        /// <see cref="ResetConfirmSeconds"/>. One stray touch on a phone held
        /// at arm's length must not cost a finished room.
        /// </summary>
        private void OnResetPressed()
        {
            if (Time.unscaledTime >= resetArmedUntil)
            {
                resetArmedUntil = Time.unscaledTime + ResetConfirmSeconds;
                return;
            }

            resetArmedUntil = -1f;
            floorLockHud?.ResetScan();
        }

        private void OnFinalizePressed()
        {
            if (Workflow != null && Workflow.TryFinalize(out _))
            {
                ExportCurrentScan();
            }
        }

        /// <summary>
        /// Task R6: builds the export bundle for the finalized scan and keeps it
        /// on the phone. Logs one <c>GhostMap export:</c> line for the device
        /// session (spec target: under 2 s).
        /// </summary>
        private void ExportCurrentScan()
        {
            ScanWorkflowController workflow = Workflow;

            if (workflow == null || workflow.Phase != ScanPhase.Finalized)
            {
                return;
            }

            lastExport = exporter.Export(workflow.Snapshot, workflow.ObjectPhotos);
            exportedWorkflow = workflow;

            if (lastExport.Succeeded)
            {
                Debug.Log($"GhostMap export: saved {lastExport.Saved.FileName} " +
                          $"({lastExport.Saved.SizeBytes / 1024} KB) in {lastExport.ElapsedMs} ms");
            }
            else
            {
                Debug.LogWarning($"GhostMap export: failed after {lastExport.ElapsedMs} ms: {lastExport.Error}");
            }
        }

        private static string LoadRoomTemplate()
        {
            var asset = Resources.Load<TextAsset>(RoomTemplateResource);

            if (asset == null)
            {
                throw new InvalidOperationException($"Resources/{RoomTemplateResource} is missing from the build.");
            }

            return asset.text;
        }

        private string BuildNetworkStatus()
        {
            builder.Clear();
            if (discovery != null && discovery.State == PeerDiscoveryState.Searching)
            {
                builder.Append("Finding your computer…");
                return builder.ToString();
            }

            if (discovery != null && discovery.State == PeerDiscoveryState.TimedOut)
            {
                builder.Append("No computer found. Check that GhostMap Viewer is open, then try again.");
                return builder.ToString();
            }

            if (discovery != null && discovery.State == PeerDiscoveryState.Failed)
            {
                builder.Append("Could not search for a computer: ").Append(discovery.LastError);
                return builder.ToString();
            }

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

            builder.Append(workflow.Phase == ScanPhase.Finalized ? "FINALIZED" : "Not finalized").Append('\n');

            AppendDiagnostics(builder);

            return builder.ToString();
        }

        /// <summary>
        /// Proves, on the phone's own screen, that this component is actually
        /// running and that <see cref="finalizeButton"/> is in the state the
        /// current phase implies — added after a device pass where no
        /// Finalize button was visible at <see cref="ScanPhase.ReadyToFinalize"/>
        /// and the underlying scene/wiring could not be reproduced as broken
        /// from the Editor (see the S6 hardening handoff). If this line is
        /// ever missing from the screen, <see cref="ScannerHudController"/>
        /// itself is not running — the deployed build predates this wiring,
        /// or the component/scene reference is broken. If it is present but
        /// the values are wrong, that pinpoints exactly which one.
        /// </summary>
        private void AppendDiagnostics(StringBuilder target)
        {
            target.Append("S6 diag: hud.enabled=").Append(enabled)
                .Append(" finalizeBtn.active=").Append(finalizeButton != null ? finalizeButton.gameObject.activeSelf.ToString() : "NULL")
                .Append(" .interactable=").Append(finalizeButton != null ? finalizeButton.interactable.ToString() : "NULL")
                .Append(" resetBtn.active=").Append(resetButton != null ? resetButton.gameObject.activeSelf.ToString() : "NULL");
        }
    }
}
