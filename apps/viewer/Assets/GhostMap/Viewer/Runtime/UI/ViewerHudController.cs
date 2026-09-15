using System.IO;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Bootstrap;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Networking;
using GhostMap.Viewer.Persistence;
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
            SceneSnapshot current = bootstrap != null ? bootstrap.EditableScene?.Current : null;
            if (current == null)
            {
                _lastPersistenceMessage = "Nothing to save yet.";
                return;
            }

            string path = ResolveSaveFilePath();
            _lastPersistenceMessage = ScenePersistence.TrySave(current, path, out string error)
                ? $"Saved to {path}"
                : $"Save failed: {error}";
        }

        private void OnLoadClicked()
        {
            string path = ResolveSaveFilePath();
            if (!ScenePersistence.TryLoad(path, out SceneSnapshot loaded, out string error))
            {
                _lastPersistenceMessage = $"Load failed: {error}";
                return;
            }

            _lastPersistenceMessage = bootstrap.EditableScene.LoadExternalSnapshot(loaded, out string loadError)
                ? $"Loaded from {path}"
                : $"Load failed: {loadError}";
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
