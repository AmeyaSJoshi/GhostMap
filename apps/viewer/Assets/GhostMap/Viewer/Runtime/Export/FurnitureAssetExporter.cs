using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Viewer.Export
{
    /// <summary>
    /// One object's export result, so a partial run can report exactly what
    /// landed and what did not.
    /// </summary>
    public readonly struct AssetExportResult
    {
        public AssetExportResult(string objectId, string path, bool succeeded, string error)
        {
            ObjectId = objectId;
            Path = path;
            Succeeded = succeeded;
            Error = error;
        }

        public string ObjectId { get; }

        /// <summary>Where the file landed, or empty when it did not.</summary>
        public string Path { get; }

        public bool Succeeded { get; }

        /// <summary>Empty on success.</summary>
        public string Error { get; }
    }

    /// <summary>
    /// ADR-0006: writes each piece of furniture in a scene out as its own
    /// <c>.glb</c> asset.
    ///
    /// <para>Writes are atomic — temp file then
    /// <c>File.Replace</c>/<c>Move</c> — exactly as
    /// <see cref="Persistence.ScenePersistence"/> does, so an interrupted
    /// export cannot leave a half-written model behind that a reader would
    /// treat as valid.</para>
    ///
    /// <para><b>One object failing does not abort the rest.</b> A scene can hold
    /// an object whose type produces no geometry; exporting the other seven
    /// pieces is more useful than refusing the lot, so every object gets a
    /// result and the caller decides what to say.</para>
    ///
    /// <para>Plain C# so the whole thing can be driven against a temp directory
    /// in an EditMode test.</para>
    /// </summary>
    public static class FurnitureAssetExporter
    {
        /// <summary>
        /// Subdirectory of the save location that exported assets go into, so
        /// they never sit loose next to the scene JSON.
        /// </summary>
        public const string DirectoryName = "ghostmap-assets";

        public const string Extension = ".glb";

        /// <summary>
        /// The default export directory, beside the V6 save file under
        /// <c>Application.persistentDataPath</c> so it resolves in a standalone
        /// player build and not only in the Editor.
        /// </summary>
        public static string DefaultDirectory =>
            Path.Combine(Application.persistentDataPath, DirectoryName);

        /// <summary>
        /// Exports every object in the room, one <c>.glb</c> per object.
        /// </summary>
        /// <returns>
        /// False only when nothing could be exported at all. Per-object
        /// outcomes are in <paramref name="results"/>.
        /// </returns>
        public static bool TryExportRoomObjects(
            RoomModel room,
            string directory,
            out IReadOnlyList<AssetExportResult> results,
            out string error)
        {
            var collected = new List<AssetExportResult>();
            results = collected;

            if (room?.objects == null || room.objects.Length == 0)
            {
                error = "This room has no furniture to export.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "Export directory is empty.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception)
            {
                error = $"Could not create export directory '{directory}': {exception.Message}";
                return false;
            }

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int succeeded = 0;

            for (int i = 0; i < room.objects.Length; i++)
            {
                SceneObjectModel model = room.objects[i];

                if (model == null)
                {
                    collected.Add(new AssetExportResult(
                        $"[{i}]", string.Empty, false, "Object entry was null."));
                    continue;
                }

                string fileName = UniqueFileName(model, i, usedNames);
                string path = Path.Combine(directory, fileName);

                if (TryExportOne(model, path, out string objectError))
                {
                    collected.Add(new AssetExportResult(model.id, path, true, string.Empty));
                    succeeded++;
                }
                else
                {
                    collected.Add(new AssetExportResult(model.id, string.Empty, false, objectError));
                }
            }

            if (succeeded == 0)
            {
                error = $"None of the {room.objects.Length} objects could be exported.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>Exports one object to an exact path, atomically.</summary>
        public static bool TryExportOne(SceneObjectModel model, string path, out string error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Export path is empty.";
                return false;
            }

            if (!GlbExporter.TryExportObject(model, out byte[] glb, out error))
            {
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

                File.WriteAllBytes(tempPath, glb);

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
                error = $"Could not write asset '{path}': {exception.Message}";
                TryDeleteTempFile(tempPath);
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// A readable, filesystem-safe, collision-free name: the type, a short
        /// slice of the object id, and the index if even that repeats.
        /// </summary>
        internal static string UniqueFileName(
            SceneObjectModel model,
            int index,
            HashSet<string> usedNames)
        {
            string type = Sanitize(string.IsNullOrEmpty(model.type) ? "object" : model.type);
            string shortId = Sanitize(GlbExporter.Shorten(model.id));

            string candidate = $"{type}-{shortId}{Extension}";

            if (usedNames != null && !usedNames.Add(candidate))
            {
                candidate = $"{type}-{shortId}-{index}{Extension}";
                usedNames.Add(candidate);
            }

            return candidate;
        }

        /// <summary>
        /// Keeps letters, digits, dash and underscore. An object id or type is
        /// not supposed to contain anything else, but a filename built from
        /// unvalidated string fields is not the place to find out.
        /// </summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            var builder = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    builder.Append(c);
                }
            }

            return builder.Length == 0 ? "unknown" : builder.ToString();
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
                // Losing a temp file is not worth failing an export that has
                // already reported its real error.
            }
        }
    }
}
