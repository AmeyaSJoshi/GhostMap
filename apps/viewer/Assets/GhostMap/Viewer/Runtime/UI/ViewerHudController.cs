using System.IO;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Bootstrap;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Networking;
using GhostMap.Viewer.Persistence;
using GhostMap.Viewer.Scene;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Viewer.UI
{
    /// <summary>
    /// The Viewer's real control surface (Task V1's diagnostics screen,
    /// polished into the Task V6 MVP HUD): connection state, a shortened
    /// session id, revision, scan phase, and every top-level control —
    /// "Load fixture" (developer convenience, unchanged since V1), Save,
    /// Load, Reset View, Dollhouse — plus a live persistence status line.
    /// The object inspector (position/yaw/size fields, the measure toggle
    /// and its readout) stays <see cref="InspectorPanelController"/>'s own
    /// panel rather than being folded in here — a second on-screen panel is
    /// not "a competing UI system" as long as, as here, every one of them
    /// drives the same single <see cref="Interaction.MeasurementController"/>/
    /// <see cref="Scene.ViewerEditableScene"/>, never a parallel copy of
    /// that state.
    /// </summary>
    public sealed class ViewerHudController : MonoBehaviour
    {
        [SerializeField] private ViewerBootstrap bootstrap;
        [SerializeField] private Button loadFixtureButton;
        [SerializeField] private Text statusText;

        /// <summary>Task V4: on-screen equivalents of section 13.1's `F` and
        /// `D` keys, so the controls are discoverable without the manual.</summary>
        [SerializeField] private Button resetViewButton;
        [SerializeField] private Button dollhouseButton;
        [SerializeField] private OrbitCameraController cameraController;

        /// <summary>Task V6: save/load the effective (post-finalization,
        /// locally-edited) scene to a single fixed slot.</summary>
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;

        /// <summary>
        /// Fix (post-V6 review): a successful Load replaces what the user is
        /// inspecting, so it must reset transient interaction state that
        /// referenced the old room — never on a failed load, which leaves
        /// the current scene (and therefore this state) untouched.
        /// </summary>
        [SerializeField] private ObjectSelectionController selectionController;
        [SerializeField] private MeasurementController measurementController;

        /// <summary>
        /// The fixture loaded by the button. Resolved relative to the repo
        /// root at edit/dev time; V1 targets desktop development, not a
        /// standalone player deployment.
        /// </summary>
        [SerializeField] private string fixtureRelativePath = "fixtures/valid-room-v1.json";

        /// <summary>
        /// Task V6: the single save slot's file name, under
        /// <see cref="Application.persistentDataPath"/> — unlike
        /// <see cref="fixtureRelativePath"/>'s repo-relative dev shortcut,
        /// this resolves correctly in a standalone player build too.
        /// </summary>
        [SerializeField] private string saveFileName = "ghostmap-scene.json";

        private string _lastPersistenceMessage = string.Empty;

        /// <summary>Test-time wiring, mirroring the <c>SetX</c> pattern every
        /// other interaction controller already uses (e.g.
        /// <c>ObjectSelectionController.SetRoomRenderer</c>), so the small
        /// pure/testable pieces below can be exercised without a full scene
        /// build.</summary>
        public void SetSelectionController(ObjectSelectionController value) => selectionController = value;

        public void SetMeasurementController(MeasurementController value) => measurementController = value;

        public void SetCameraController(OrbitCameraController value) => cameraController = value;

        private void Awake()
        {
            if (loadFixtureButton != null)
            {
                loadFixtureButton.onClick.AddListener(OnLoadFixtureClicked);
            }

            if (resetViewButton != null)
            {
                resetViewButton.onClick.AddListener(OnResetViewClicked);
            }

            if (dollhouseButton != null)
            {
                dollhouseButton.onClick.AddListener(OnDollhouseClicked);
            }

            if (saveButton != null)
            {
                saveButton.onClick.AddListener(OnSaveClicked);
            }

            if (loadButton != null)
            {
                loadButton.onClick.AddListener(OnLoadClicked);
            }
        }

        private void OnResetViewClicked()
        {
            if (cameraController != null)
            {
                cameraController.FrameRoom();
            }
        }

        private void OnDollhouseClicked()
        {
            if (cameraController != null)
            {
                cameraController.ToggleDollhouse();
            }
        }

        private void OnLoadFixtureClicked()
        {
            string path = ResolveFixturePath(fixtureRelativePath);
            bootstrap.Session.LoadFixture(path);
        }

        private void OnSaveClicked()
        {
            ViewerEditableScene editable = bootstrap != null ? bootstrap.EditableScene : null;

            if (!CanSave(editable))
            {
                _lastPersistenceMessage = "Save unavailable until the scan is finalized.";
                return;
            }

            string path = ResolveSaveFilePath();
            _lastPersistenceMessage = ScenePersistence.TrySave(editable.Current, path, out string error)
                ? $"Saved to {path}"
                : $"Save failed: {error}";
        }

        private void OnLoadClicked()
        {
            ViewerEditableScene editable = bootstrap != null ? bootstrap.EditableScene : null;

            if (!CanLoad(editable))
            {
                _lastPersistenceMessage = "Finish or reset the active scan before loading a saved room.";
                return;
            }

            string path = ResolveSaveFilePath();
            if (!ScenePersistence.TryLoad(path, out SceneSnapshot loaded, out string error))
            {
                _lastPersistenceMessage = $"Load failed: {error}";
                return;
            }

            if (!editable.LoadExternalSnapshot(loaded, out string loadError))
            {
                _lastPersistenceMessage = $"Load failed: {loadError}";
                return;
            }

            ResetTransientStateAfterLoad();
            _lastPersistenceMessage = $"Loaded from {path}";
        }

        /// <summary>
        /// Fix (post-V6 review), ADR-0003: Save must only ever persist a
        /// Viewer-owned, finalized scene — never a Scanner-owned, in-progress
        /// scan. <see cref="ViewerEditableScene.EditingEnabled"/> and
        /// <see cref="SceneSnapshot.finalized"/> are kept in sync by
        /// construction (every path that sets <c>Current</c> sets
        /// <c>EditingEnabled</c> from that same snapshot's <c>finalized</c>
        /// flag), but both are checked here anyway so this boundary does not
        /// rely on an invariant holding elsewhere. Public and static so it is
        /// directly testable without a scene.
        /// </summary>
        public static bool CanSave(ViewerEditableScene editable)
            => editable != null && editable.Current != null && editable.EditingEnabled && editable.Current.finalized;

        /// <summary>
        /// Fix (post-V6 review), ADR-0003: Load must never replace a
        /// Scanner-owned, in-progress scan — only an empty Viewer (nothing to
        /// protect yet) or an already Viewer-owned/finalized scene may be
        /// replaced. Public and static for the same reason as
        /// <see cref="CanSave"/>.
        /// </summary>
        public static bool CanLoad(ViewerEditableScene editable)
            => editable == null || editable.Current == null || editable.EditingEnabled;

        /// <summary>
        /// Fix (post-V6 review): a successful Load replaces the inspected
        /// room, so whatever the user was doing to the *previous* room
        /// (a selection, an in-progress or completed measurement) must not
        /// silently carry over and reference stale geometry. Only called
        /// after <see cref="ViewerEditableScene.LoadExternalSnapshot"/>
        /// actually succeeds — a failed load must never touch any of this.
        /// Public so it is directly testable by wiring real controllers via
        /// the <c>SetX</c> methods above, without a full scene build.
        /// </summary>
        public void ResetTransientStateAfterLoad()
        {
            selectionController?.ClearSelection();

            if (measurementController != null)
            {
                measurementController.SetActive(false);
                measurementController.Clear();
            }

            if (cameraController != null)
            {
                cameraController.FrameRoom();
            }
        }

        private string ResolveSaveFilePath() => Path.Combine(Application.persistentDataPath, saveFileName);

        private static string ResolveFixturePath(string relativePath)
        {
            // apps/viewer/Assets -> repo root is three levels up.
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            return Path.Combine(repoRoot, relativePath);
        }

        private void Update()
        {
            if (statusText == null || bootstrap == null || bootstrap.Session == null)
            {
                return;
            }

            statusText.text = BuildStatusText();
        }

        private string BuildStatusText()
        {
            var builder = new StringBuilder();
            ViewerSession session = bootstrap.Session;
            ViewerTcpServer server = session.Server;

            builder.Append("Connection: ").Append(server.State);
            if (!string.IsNullOrEmpty(server.RemoteEndpoint))
            {
                builder.Append(" (").Append(server.RemoteEndpoint).Append(')');
            }
            builder.AppendLine();

            // Task V5: the HUD reflects the *effective* scene — live scanner
            // data before finalization, the locally-edited or loaded copy
            // after — never the raw scanner-only ViewerSceneStore.Current.
            SceneSnapshot current = bootstrap.EditableScene?.Current;

            if (current == null)
            {
                // Task V6: before any scene exists the HUD must clearly say
                // whether it is listening/waiting rather than showing a bare
                // "none yet" with no explanation.
                builder.Append(
                    server.State == ServerConnectionState.Listening
                        ? "Waiting for a scanner connection..."
                        : "Scene: none yet");
            }
            else
            {
                builder.Append("Session: ").Append(ShortenSessionId(current.sessionId));
                if (!string.IsNullOrEmpty(session.LastDeviceName))
                {
                    builder.Append(" (").Append(session.LastDeviceName).Append(')');
                }
                builder.AppendLine();

                builder.Append("Revision: ").Append(current.revision).AppendLine();
                builder.Append("Phase: ").Append(current.scanPhase).AppendLine();
                builder.Append(current.finalized ? "FINALIZED" : "Not finalized").AppendLine();
                builder.Append("Room: ")
                    .Append(current.room?.corners?.Length ?? 0).Append(" corners, ")
                    .Append(current.room?.openings?.Length ?? 0).Append(" openings, ")
                    .Append(current.room?.objects?.Length ?? 0).Append(" objects").AppendLine();
                builder.Append(
                    bootstrap.EditableScene != null && bootstrap.EditableScene.EditingEnabled
                        ? "Editing: ENABLED"
                        : "Editing: disabled (finalize scan to edit)");

                if (server.State == ServerConnectionState.Disconnected)
                {
                    // Protocol v1 section 6's exact required message: a
                    // network drop must never look like the scene vanished.
                    builder.AppendLine().Append("Disconnected — displaying last snapshot.");
                }
            }

            if (!string.IsNullOrEmpty(_lastPersistenceMessage))
            {
                builder.AppendLine().Append(_lastPersistenceMessage);
            }

            if (!string.IsNullOrEmpty(server.LastError))
            {
                builder.AppendLine().Append("Last protocol error: ").Append(server.LastError);
            }

            if (!string.IsNullOrEmpty(session.LastSceneRejection))
            {
                builder.AppendLine().Append("Last scene rejection: ").Append(session.LastSceneRejection);
            }

            if (!string.IsNullOrEmpty(session.LastFixtureError))
            {
                builder.AppendLine().Append("Last fixture error: ").Append(session.LastFixtureError);
            }

            builder.AppendLine().AppendLine()
                .Append("Camera: drag = orbit, right/middle drag = pan, scroll = zoom, ")
                .Append("F = frame room, D = dollhouse")
                .AppendLine()
                .Append("Click = select object, drag selected = move, M = measure");

            if (cameraController != null && cameraController.DollhouseEnabled)
            {
                builder.AppendLine().Append("Dollhouse ON (ceiling hidden)");
            }

            return builder.ToString();
        }

        /// <summary>Task V6: a short, stable session label for the HUD — the
        /// full id is still used everywhere logic depends on it (revision
        /// arbitration, ownership), this only shortens what is displayed.</summary>
        private static string ShortenSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return "-";
            }

            const int visibleChars = 8;
            return sessionId.Length <= visibleChars ? sessionId : sessionId.Substring(0, visibleChars) + "…";
        }
    }
}
