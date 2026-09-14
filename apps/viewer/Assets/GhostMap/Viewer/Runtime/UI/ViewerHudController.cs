using System.IO;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Bootstrap;
using GhostMap.Viewer.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Viewer.UI
{
    /// <summary>
    /// Task V1's diagnostics screen: connection state, the last scanner
    /// session seen, and the currently displayed scene's revision/phase/
    /// finalized flag — plus the "Load fixture" developer button.
    /// </summary>
    public sealed class ViewerHudController : MonoBehaviour
    {
        [SerializeField] private ViewerBootstrap bootstrap;
        [SerializeField] private Button loadFixtureButton;
        [SerializeField] private Text statusText;

        /// <summary>
        /// The fixture loaded by the button. Resolved relative to the repo
        /// root at edit/dev time; V1 targets desktop development, not a
        /// standalone player deployment.
        /// </summary>
        [SerializeField] private string fixtureRelativePath = "fixtures/valid-room-v1.json";

        private void Awake()
        {
            if (loadFixtureButton != null)
            {
                loadFixtureButton.onClick.AddListener(OnLoadFixtureClicked);
            }
        }

        private void OnLoadFixtureClicked()
        {
            string path = ResolveFixturePath(fixtureRelativePath);
            bootstrap.Session.LoadFixture(path);
        }

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

            builder.Append("Session: ").Append(
                string.IsNullOrEmpty(session.LastSessionId) ? "-" : session.LastSessionId);
            if (!string.IsNullOrEmpty(session.LastDeviceName))
            {
                builder.Append(" (").Append(session.LastDeviceName).Append(')');
            }
            builder.AppendLine();

            SceneSnapshot current = session.SceneStore.Current;
            if (current == null)
            {
                builder.Append("Scene: none yet");
            }
            else
            {
                builder.Append("Revision: ").Append(current.revision).AppendLine();
                builder.Append("Phase: ").Append(current.scanPhase).AppendLine();
                builder.Append(current.finalized ? "FINALIZED" : "Not finalized").AppendLine();
                builder.Append("Room: ")
                    .Append(current.room?.corners?.Length ?? 0).Append(" corners, ")
                    .Append(current.room?.openings?.Length ?? 0).Append(" openings, ")
                    .Append(current.room?.objects?.Length ?? 0).Append(" objects");
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

            return builder.ToString();
        }
    }
}
