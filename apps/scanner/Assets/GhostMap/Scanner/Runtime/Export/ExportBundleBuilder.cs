using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Export;
using UnityEngine;

namespace GhostMap.Scanner.Export
{
    /// <summary>
    /// Task R6 (ADR-0013 section 1): builds the zip the phone keeps and sends
    /// for one finalized room. Contract: <c>docs/contracts/export-bundle-v1.md</c>.
    ///
    /// <code>
    /// GhostMap-&lt;room&gt;-&lt;yyyyMMdd-HHmm&gt;.zip
    /// ├── room.html         the browser viewer template, filled with this room
    /// ├── room.glb          floor, ceiling, walls with openings cut
    /// ├── objects/&lt;id&gt;.glb  one per furniture object, centred on the origin
    /// ├── scene.json        the finalized SceneSnapshot
    /// └── README.txt
    /// </code>
    ///
    /// Pure C#: bytes in, bytes out, no filesystem. The geometry is the shared
    /// <see cref="GlbWriter"/>; the HTML fill follows the reference in
    /// <c>apps/web-viewer/scripts/fill.mjs</c> exactly.
    /// </summary>
    public static class ExportBundleBuilder
    {
        /// <summary>The template's single data placeholder (contract section 2.1).</summary>
        public const string Placeholder = "__GHOSTMAP_DATA__";

        public const string DataFormat = "ghostmap-room-html";
        public const int DataFormatVersion = 1;

        public const string RoomHtmlEntry = "room.html";
        public const string RoomGlbEntry = "room.glb";
        public const string SceneJsonEntry = "scene.json";
        public const string ReadmeEntry = "README.txt";
        public const string ObjectsFolder = "objects/";

        /// <summary>Builds the bundle for a finalized snapshot.</summary>
        /// <param name="now">Local time of the export; names the file and stamps the zip entries.</param>
        public static bool TryBuild(
            SceneSnapshot snapshot,
            string roomTemplateHtml,
            DateTime now,
            out byte[] zip,
            out string fileName,
            out string error)
        {
            zip = null;
            fileName = null;

            if (snapshot?.room == null)
            {
                error = "There is no room to export.";
                return false;
            }

            if (!snapshot.finalized)
            {
                error = "Finish the scan before exporting it.";
                return false;
            }

            if (!GlbWriter.TryExportRoom(snapshot.room, out byte[] roomGlb, out error))
            {
                return false;
            }

            SceneObjectModel[] objects = snapshot.room.objects ?? Array.Empty<SceneObjectModel>();
            var objectGlbs = new List<KeyValuePair<SceneObjectModel, byte[]>>(objects.Length);

            foreach (SceneObjectModel model in objects)
            {
                if (!GlbWriter.TryExportObject(model, out byte[] glb, out string objectError))
                {
                    error = $"Could not export the {model?.type ?? "object"}: {objectError}";
                    return false;
                }

                objectGlbs.Add(new KeyValuePair<SceneObjectModel, byte[]>(model, glb));
            }

            if (!TryFillTemplate(roomTemplateHtml, snapshot, roomGlb, objectGlbs, out string roomHtml, out error))
            {
                return false;
            }

            fileName = FileNameFor(snapshot, now);
            zip = WriteZip(snapshot, roomHtml, roomGlb, objectGlbs, now);
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Fills the browser viewer template (contract section 2): replaces its
        /// one placeholder with the data object, escaping every "&lt;" as
        /// <c><</c> so nothing in the scene can end the script tag.
        /// </summary>
        public static bool TryFillTemplate(
            string template,
            SceneSnapshot snapshot,
            byte[] roomGlb,
            IReadOnlyList<KeyValuePair<SceneObjectModel, byte[]>> objectGlbs,
            out string html,
            out string error)
        {
            html = null;

            if (string.IsNullOrEmpty(template))
            {
                error = "The room viewer template is missing from the app.";
                return false;
            }

            int first = template.IndexOf(Placeholder, StringComparison.Ordinal);

            if (first < 0 || template.IndexOf(Placeholder, first + Placeholder.Length, StringComparison.Ordinal) >= 0)
            {
                error = "The room viewer template must contain its data placeholder exactly once.";
                return false;
            }

            var data = new StringBuilder(roomGlb.Length * 2);
            data.Append("{\"format\":").Append(Quote(DataFormat));
            data.Append(",\"formatVersion\":").Append(DataFormatVersion.ToString(CultureInfo.InvariantCulture));
            data.Append(",\"scene\":").Append(JsonUtility.ToJson(snapshot));
            data.Append(",\"roomGlb\":\"").Append(Convert.ToBase64String(roomGlb)).Append('"');
            data.Append(",\"objectGlbs\":{");

            for (int i = 0; i < objectGlbs.Count; i++)
            {
                data.Append(i == 0 ? "" : ",");
                data.Append(Quote(objectGlbs[i].Key.id)).Append(":\"")
                    .Append(Convert.ToBase64String(objectGlbs[i].Value)).Append('"');
            }

            data.Append("}}");
            data.Replace("<", "\\u003c");

            html = template.Substring(0, first) + data + template.Substring(first + Placeholder.Length);
            error = string.Empty;
            return true;
        }

        /// <summary><c>GhostMap-&lt;room&gt;-&lt;yyyyMMdd-HHmm&gt;.zip</c>, safe on every file system.</summary>
        public static string FileNameFor(SceneSnapshot snapshot, DateTime now)
        {
            string room = SafeName(snapshot?.room?.name, 40);
            return $"GhostMap-{(room.Length > 0 ? room : "Room")}-{now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.zip";
        }

        /// <summary>
        /// The zip path of one object's model: <c>objects/&lt;id&gt;.glb</c>,
        /// with any character outside letters, digits, <c>.</c>, <c>_</c> and
        /// <c>-</c> replaced by <c>_</c>, and a suffix if two ids collide.
        /// </summary>
        public static IReadOnlyList<string> ObjectEntryNames(IReadOnlyList<SceneObjectModel> objects)
        {
            var names = new List<string>(objects.Count);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SceneObjectModel model in objects)
            {
                string stem = SafeName(model?.id, 64);
                if (stem.Length == 0)
                {
                    stem = "object";
                }

                string candidate = stem;
                for (int n = 2; !used.Add(candidate); n++)
                {
                    candidate = $"{stem}_{n}";
                }

                names.Add(ObjectsFolder + candidate + ".glb");
            }

            return names;
        }

        // -------------------------------------------------------------------

        private static byte[] WriteZip(
            SceneSnapshot snapshot,
            string roomHtml,
            byte[] roomGlb,
            List<KeyValuePair<SceneObjectModel, byte[]>> objectGlbs,
            DateTime now)
        {
            var models = new List<SceneObjectModel>(objectGlbs.Count);
            foreach (KeyValuePair<SceneObjectModel, byte[]> pair in objectGlbs)
            {
                models.Add(pair.Key);
            }

            IReadOnlyList<string> objectNames = ObjectEntryNames(models);
            var utf8 = new UTF8Encoding(false);

            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Add(archive, RoomHtmlEntry, utf8.GetBytes(roomHtml), now);
                    Add(archive, RoomGlbEntry, roomGlb, now);

                    for (int i = 0; i < objectGlbs.Count; i++)
                    {
                        Add(archive, objectNames[i], objectGlbs[i].Value, now);
                    }

                    Add(archive, SceneJsonEntry, utf8.GetBytes(JsonUtility.ToJson(snapshot, true)), now);
                    Add(archive, ReadmeEntry, utf8.GetBytes(Readme(snapshot, objectGlbs.Count)), now);
                }

                return stream.ToArray();
            }
        }

        private static void Add(ZipArchive archive, string name, byte[] bytes, DateTime now)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);

            // Zip timestamps cannot go before 1980; a phone clock never should.
            entry.LastWriteTime = now.Year < 1980 ? new DateTime(1980, 1, 1) : now;

            using (Stream output = entry.Open())
            {
                output.Write(bytes, 0, bytes.Length);
            }
        }

        private static string Readme(SceneSnapshot snapshot, int objectCount)
        {
            var text = new StringBuilder();
            text.Append("GhostMap room export\n");
            text.Append("====================\n\n");
            text.Append("Room: ").Append(string.IsNullOrEmpty(snapshot.room.name) ? "Room" : snapshot.room.name).Append('\n');
            text.Append("Furniture objects: ").Append(objectCount.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
            text.Append("room.html          Double-click to open in any web browser. Look around, hide the\n");
            text.Append("                   ceiling (Dollhouse), click furniture for its size, measure\n");
            text.Append("                   distances. Works offline; nothing is sent anywhere.\n");
            text.Append("room.glb           The room: floor, ceiling, and walls with doors and windows cut out.\n");
            text.Append("objects/<id>.glb   One 3D model per piece of furniture, centred on its own origin.\n");
            text.Append("                   Its place in the room is \"center\" and \"yawDeg\" in scene.json.\n");
            text.Append("scene.json         The room as structured data: corners, height, openings, furniture\n");
            text.Append("                   (GhostMap scene schema v1, metres, +Y up).\n\n");
            text.Append("Using the files in Unity\n");
            text.Append("------------------------\n");
            text.Append("1. Add the glTFast package: Window > Package Manager > + > Add package by name >\n");
            text.Append("   com.unity.cloud.gltfast\n");
            text.Append("2. Drag room.glb and the objects folder into your project's Assets folder.\n");
            text.Append("3. Drag room.glb into a scene. It comes in at real size, in metres.\n");
            text.Append("4. Furniture: drag each model from objects/ into the scene (not inside room.glb)\n");
            text.Append("   and set its Transform from its entry in scene.json:\n");
            text.Append("     Position = (-center.x, center.y, -center.z)    x and z negated\n");
            text.Append("     Rotation = (0, yawDeg, 0)\n");
            text.Append("   glTFast turns glTF files 180 degrees about Y as it imports them; negating x and z\n");
            text.Append("   matches that, so the furniture lands where it was in the room.\n");
            text.Append("\nThe .glb files are glTF 2.0 and also open in Blender and most 3D tools.\n");
            return text.ToString();
        }

        private static string SafeName(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(value.Length, maxLength));

            foreach (char c in value)
            {
                if (builder.Length >= maxLength)
                {
                    break;
                }

                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                            c == '-' || c == '_' || c == '.';
                builder.Append(safe ? c : '_');
            }

            return builder.ToString().Trim('.', '_');
        }

        private static string Quote(string value)
        {
            var builder = new StringBuilder((value?.Length ?? 0) + 2);
            builder.Append('"');

            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
