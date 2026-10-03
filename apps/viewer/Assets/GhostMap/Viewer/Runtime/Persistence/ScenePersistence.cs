using System;
using System.IO;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Scene;
using UnityEngine;

namespace GhostMap.Viewer.Persistence
{
    /// <summary>
    /// Task V6's save/load core, deliberately plain C# (no <see cref="MonoBehaviour"/>)
    /// so it is testable without a Play-mode loop, mirroring
    /// <see cref="FixtureLoader"/>'s own split between file I/O and the
    /// caller that wires it into the scene.
    ///
    /// <para>Persists the frozen shared <see cref="SceneSnapshot"/> schema
    /// verbatim via <c>JsonUtility</c> — the exact same type and serializer
    /// the wire protocol and <see cref="FixtureLoader"/> already use
    /// (<c>AGENTS.md</c> rules 2 and 3: no duplicate DTO, no second JSON
    /// schema). A save file is therefore byte-for-byte a bare fixture file
    /// (protocol v1 section 8) and can be re-loaded by either this type or
    /// the "Load fixture" developer button.</para>
    ///
    /// <para><b>What gets saved is whatever the caller passes in</b> — this
    /// type has no opinion on scanner-vs-viewer authority. The Task V6
    /// requirement ("persist the CURRENT EFFECTIVE VIEWER STATE, not the
    /// stale pre-edit scanner snapshot") is satisfied by the caller always
    /// passing <c>ViewerEditableScene.Current</c>, never
    /// <c>ViewerSceneStore.Current</c> — see <c>ViewerHudController</c>.</para>
    /// </summary>
    public static class ScenePersistence
    {
        /// <summary>
        /// Serializes <paramref name="snapshot"/> and writes it to
        /// <paramref name="path"/>. Writes to a temporary sibling file first
        /// and atomically replaces the target, so a failure or a crash
        /// mid-write can never leave a corrupt/truncated save file where a
        /// good one used to be.
        /// </summary>
        public static bool TrySave(SceneSnapshot snapshot, string path, out string error)
        {
            if (snapshot == null)
            {
                error = "Nothing to save: scene is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Save path is empty.";
                return false;
            }

            string json;
            try
            {
                json = JsonUtility.ToJson(snapshot, prettyPrint: true);
            }
            catch (Exception exception)
            {
                error = $"Could not serialize scene: {exception.Message}";
                return false;
            }

            string tempPath = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(tempPath, json);

                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception exception)
            {
                error = $"Could not write save file '{path}': {exception.Message}";
                TryDeleteTempFile(tempPath);
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Reads and fully validates a saved scene: well-formedness
        /// (existence, JSON, schema version, a present room — exactly
        /// <see cref="FixtureLoader.TryLoadFromFile"/>'s own checks) plus the
        /// same domain validation every scanner snapshot must pass
        /// (<see cref="SceneSnapshotValidator"/>). Returns false, with
        /// <paramref name="snapshot"/> left <c>null</c>, for anything short
        /// of a fully legal room — the caller must never install a
        /// partially-checked result over the currently displayed scene.
        /// </summary>
        public static bool TryLoad(string path, out SceneSnapshot snapshot, out string error)
        {
            if (!FixtureLoader.TryLoadFromFile(path, out snapshot, out error))
            {
                snapshot = null;
                return false;
            }

            if (!SceneSnapshotValidator.TryValidate(snapshot, out error))
            {
                snapshot = null;
                return false;
            }

            return true;
        }

        private static void TryDeleteTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup only; the caller already has the real error.
            }
        }
    }
}
