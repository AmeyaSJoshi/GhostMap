using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using GhostMap.Scanner.Export;
using GhostMap.Scanner.UI;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Export;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task R6: the export bundle, checked against
    /// docs/contracts/export-bundle-v1.md using the repository's fixture room.
    /// </summary>
    public sealed class ExportBundleBuilderTests
    {
        private const string Template =
            "<html><body><script type=\"application/json\" id=\"ghostmap-data\">__GHOSTMAP_DATA__</script></body></html>";

        private static readonly DateTime Now = new DateTime(2026, 10, 11, 16, 5, 0);

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static SceneSnapshot Fixture(string name = "room-with-door-window-v1.json")
            => JsonUtility.FromJson<SceneSnapshot>(File.ReadAllText(Path.Combine(RepoRoot, "fixtures", name)));

        private static Dictionary<string, byte[]> Unzip(byte[] zip)
        {
            var entries = new Dictionary<string, byte[]>();
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    using (Stream stream = entry.Open())
                    using (var copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        entries[entry.FullName] = copy.ToArray();
                    }
                }
            }
            return entries;
        }

        private static byte[] Build(SceneSnapshot snapshot, string template = Template)
        {
            Assert.IsTrue(ExportBundleBuilder.TryBuild(snapshot, template, Now, out byte[] zip, out _, out string error), error);
            return zip;
        }

        private static string DataTag(string html)
        {
            const string open = "id=\"ghostmap-data\">";
            int start = html.IndexOf(open, StringComparison.Ordinal) + open.Length;
            int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
            return html.Substring(start, end - start);
        }

        // ---------------------------------------------------------------
        // Layout (contract section 1)
        // ---------------------------------------------------------------

        [Test]
        public void Bundle_ContainsExactlyTheContractEntries()
        {
            Dictionary<string, byte[]> entries = Unzip(Build(Fixture()));

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "room.html", "room.glb", "objects/bed-1.glb", "objects/desk-1.glb", "objects/chair-1.glb",
                    "scene.json", "README.txt"
                },
                entries.Keys);
        }

        [Test]
        public void Bundle_FileNameIsGhostMapRoomAndMinute()
        {
            Assert.IsTrue(ExportBundleBuilder.TryBuild(Fixture(), Template, Now, out _, out string fileName, out _));
            Assert.AreEqual("GhostMap-Fixture_Bedroom_With_Window-20261011-1605.zip", fileName);
        }

        [Test]
        public void FileName_IsSafeForAnyRoomName()
        {
            SceneSnapshot snapshot = Fixture();

            snapshot.room.name = "../Kid's room: 2/3 \"new\"";
            string name = ExportBundleBuilder.FileNameFor(snapshot, Now);
            StringAssert.StartsWith("GhostMap-", name);
            StringAssert.EndsWith("-20261011-1605.zip", name);
            Assert.AreEqual(Path.GetFileName(name), name, "no folders");
            Assert.IsFalse(name.Contains(".."));

            snapshot.room.name = string.Empty;
            Assert.AreEqual("GhostMap-Room-20261011-1605.zip", ExportBundleBuilder.FileNameFor(snapshot, Now));
        }

        [Test]
        public void SceneJson_ReadsBackAsTheSameSnapshot()
        {
            SceneSnapshot snapshot = Fixture();
            byte[] json = Unzip(Build(snapshot))["scene.json"];

            SceneSnapshot read = JsonUtility.FromJson<SceneSnapshot>(Encoding.UTF8.GetString(json));
            Assert.AreEqual(JsonUtility.ToJson(snapshot), JsonUtility.ToJson(read));
            Assert.AreNotEqual(0xEF, json[0], "UTF-8 without a byte-order mark");
        }

        [Test]
        public void Glbs_AreTheSharedWritersOutput()
        {
            SceneSnapshot snapshot = Fixture();
            Dictionary<string, byte[]> entries = Unzip(Build(snapshot));

            Assert.IsTrue(GlbWriter.TryExportRoom(snapshot.room, out byte[] room, out _));
            CollectionAssert.AreEqual(room, entries["room.glb"]);

            foreach (SceneObjectModel model in snapshot.room.objects)
            {
                Assert.IsTrue(GlbWriter.TryExportObject(model, out byte[] glb, out _));
                CollectionAssert.AreEqual(glb, entries[$"objects/{model.id}.glb"], model.id);
            }
        }

        [Test]
        public void Readme_ExplainsEveryFileAndTheUnityImport()
        {
            string readme = Encoding.UTF8.GetString(Unzip(Build(Fixture()))["README.txt"]);
            foreach (string expected in new[] { "room.html", "room.glb", "objects/", "scene.json", "com.unity.cloud.gltfast",
                                               "Position = (-center.x, center.y, -center.z)", "Rotation = (0, yawDeg, 0)" })
            {
                StringAssert.Contains(expected, readme);
            }
        }

        [Test]
        public void ObjectEntryNames_AreSafeAndUnique()
        {
            var objects = new[]
            {
                new SceneObjectModel { id = "a/b" },
                new SceneObjectModel { id = "a_b" },
                new SceneObjectModel { id = "" },
                new SceneObjectModel { id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301" }
            };

            CollectionAssert.AreEqual(
                new[] { "objects/a_b.glb", "objects/a_b_2.glb", "objects/object.glb", "objects/3f2504e0-4f89-11d3-9a0c-0305e82c3301.glb" },
                ExportBundleBuilder.ObjectEntryNames(objects));
        }

        // ---------------------------------------------------------------
        // room.html data (contract section 2)
        // ---------------------------------------------------------------

        [Test]
        public void RoomHtml_ReplacesThePlaceholderWithTheDataObject()
        {
            SceneSnapshot snapshot = Fixture();
            string html = Encoding.UTF8.GetString(Unzip(Build(snapshot))["room.html"]);

            Assert.IsFalse(html.Contains(ExportBundleBuilder.Placeholder));
            string data = DataTag(html);
            StringAssert.StartsWith("{\"format\":\"ghostmap-room-html\",\"formatVersion\":1,\"scene\":{", data);

            Assert.IsTrue(GlbWriter.TryExportRoom(snapshot.room, out byte[] room, out _));
            StringAssert.Contains("\"roomGlb\":\"" + Convert.ToBase64String(room) + "\"", data);

            foreach (SceneObjectModel model in snapshot.room.objects)
            {
                Assert.IsTrue(GlbWriter.TryExportObject(model, out byte[] glb, out _));
                StringAssert.Contains($"\"{model.id}\":\"{Convert.ToBase64String(glb)}\"", data);
            }
        }

        [Test]
        public void RoomHtml_EscapesEveryLessThanSoNothingEndsTheScriptTag()
        {
            SceneSnapshot snapshot = Fixture();
            snapshot.room.name = "Den </script><script>alert(1)</script>";

            string html = Encoding.UTF8.GetString(Unzip(Build(snapshot))["room.html"]);
            string data = DataTag(html);

            Assert.IsFalse(data.Contains("<"), "no raw '<' in the data");
            StringAssert.Contains("Den \\u003c/script>", data);
            Assert.AreEqual(1, CountOccurrences(html, "id=\"ghostmap-data\""));
        }

        [Test]
        public void Template_WithoutExactlyOnePlaceholderIsRefused()
        {
            SceneSnapshot snapshot = Fixture();
            foreach (string template in new[] { null, "", "<html></html>", Template + Template })
            {
                Assert.IsFalse(ExportBundleBuilder.TryBuild(snapshot, template, Now, out byte[] zip, out _, out string error));
                Assert.IsNull(zip);
                Assert.IsNotEmpty(error);
            }
        }

        // ---------------------------------------------------------------
        // Refusals
        // ---------------------------------------------------------------

        [Test]
        public void UnfinalizedOrMissingSnapshotsAreRefused()
        {
            SceneSnapshot snapshot = Fixture();
            snapshot.finalized = false;
            Assert.IsFalse(ExportBundleBuilder.TryBuild(snapshot, Template, Now, out _, out _, out string error));
            StringAssert.Contains("Finish", error);

            Assert.IsFalse(ExportBundleBuilder.TryBuild(null, Template, Now, out _, out _, out error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RoomWithoutHeightIsRefused()
        {
            SceneSnapshot snapshot = Fixture();
            snapshot.room.heightM = 0f;
            Assert.IsFalse(ExportBundleBuilder.TryBuild(snapshot, Template, Now, out _, out _, out string error));
            StringAssert.Contains("height", error);
        }

        // ---------------------------------------------------------------
        // The shipped template
        // ---------------------------------------------------------------

        [Test]
        public void ShippedTemplate_MatchesTheWebViewerBuild()
        {
            // Resources/GhostMapRoomTemplate.html is a copy of
            // apps/web-viewer/dist/room-template.html. Recopy after rebuilding
            // the viewer (apps/web-viewer/README.md).
            var asset = Resources.Load<TextAsset>(ScannerHudController.RoomTemplateResource);
            Assert.IsNotNull(asset, "Resources/GhostMapRoomTemplate.html");

            string built = File.ReadAllText(Path.Combine(RepoRoot, "apps", "web-viewer", "dist", "room-template.html"));
            Assert.AreEqual(built, asset.text, "the scanner's copy is stale");
            Assert.AreEqual(1, CountOccurrences(asset.text, ExportBundleBuilder.Placeholder));
        }

        [Test]
        public void ShippedTemplate_BuildsABundle()
        {
            string template = Resources.Load<TextAsset>(ScannerHudController.RoomTemplateResource).text;
            Assert.IsTrue(ExportBundleBuilder.TryBuild(Fixture(), template, Now, out byte[] zip, out _, out string error), error);
            Assert.Less(zip.Length, 2 * 1024 * 1024, "plan section 24: room.html under 2 MB");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }
    }
}
