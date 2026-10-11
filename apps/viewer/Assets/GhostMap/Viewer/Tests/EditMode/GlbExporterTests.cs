using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using GhostMap.Viewer.Export;
using GhostMap.Viewer.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// ADR-0006 asset export: the `.glb` container's bytes, the glTF JSON it
    /// carries, and that the geometry really is the renderer's.
    ///
    /// <para>The container is asserted byte by byte rather than by round-tripping
    /// through a glTF library, because there is no library — the writer is the
    /// implementation and these tests are the only thing standing between it and
    /// a file no other tool can open.</para>
    /// </summary>
    public sealed class GlbExporterTests
    {
        private const float Tolerance = 1e-4f;

        private static SceneObjectModel Desk(
            float widthM = 1.4f,
            float depthM = 0.7f,
            float heightM = 0.73f)
        {
            return new SceneObjectModel
            {
                id = "11112222-3333-4444-5555-666677778888",
                type = "desk",
                center = new Vec3Dto(2f, 0f, 1.5f),
                yawDeg = 30f,
                widthM = widthM,
                depthM = depthM,
                heightM = heightM
            };
        }

        // -------------------------------------------------------------------
        // Container
        // -------------------------------------------------------------------

        [Test]
        public void Export_ProducesAValidGlbHeader()
        {
            Assert.IsTrue(GlbExporter.TryExportObject(Desk(), out byte[] glb, out string error), error);

            Assert.Greater(glb.Length, 12 + 8 + 8);

            Assert.AreEqual("glTF", Encoding.ASCII.GetString(glb, 0, 4));
            Assert.AreEqual(2u, ReadUInt(glb, 4), "glTF version");
            Assert.AreEqual((uint)glb.Length, ReadUInt(glb, 8), "declared length must equal real length");
        }

        [Test]
        public void Export_ProducesTwoCorrectlyFramedChunks()
        {
            Assert.IsTrue(GlbExporter.TryExportObject(Desk(), out byte[] glb, out _));

            uint jsonLength = ReadUInt(glb, 12);
            Assert.AreEqual("JSON", Encoding.ASCII.GetString(glb, 16, 4));

            int binHeader = 20 + (int)jsonLength;
            uint binLength = ReadUInt(glb, binHeader);

            Assert.AreEqual("BIN", Encoding.ASCII.GetString(glb, binHeader + 4, 3));
            Assert.AreEqual(0, glb[binHeader + 7], "the BIN chunk tag is 'BIN\\0'");

            Assert.AreEqual(
                glb.Length,
                12 + 8 + (int)jsonLength + 8 + (int)binLength,
                "the two chunks must exactly fill the file");
        }

        /// <summary>
        /// Chunk padding is load-bearing, not cosmetic: a reader locates the
        /// binary chunk by adding the JSON chunk's declared length.
        /// </summary>
        [Test]
        public void Export_PadsBothChunksToFourByteBoundaries()
        {
            Assert.IsTrue(GlbExporter.TryExportObject(Desk(), out byte[] glb, out _));

            uint jsonLength = ReadUInt(glb, 12);
            int binHeader = 20 + (int)jsonLength;
            uint binLength = ReadUInt(glb, binHeader);

            Assert.AreEqual(0, jsonLength % 4, "JSON chunk length");
            Assert.AreEqual(0, binLength % 4, "BIN chunk length");
            Assert.AreEqual(0, glb.Length % 4, "total length");
        }

        [Test]
        public void Export_PadsTheJsonChunkWithSpacesSoItStillParses()
        {
            Assert.IsTrue(GlbExporter.TryExportObject(Desk(), out byte[] glb, out _));

            string json = ReadJsonChunk(glb);

            // Whatever follows the closing brace must be spaces, never nulls:
            // a null-padded JSON chunk fails to parse in most glTF readers.
            Assert.IsFalse(json.Contains("\0"), "the JSON chunk must not be null-padded");
            StringAssert.EndsWith("}", json.TrimEnd(' '), "padding must be spaces only");
            Assert.AreEqual('{', json[0]);
        }

        // -------------------------------------------------------------------
        // glTF JSON
        // -------------------------------------------------------------------

        [Test]
        public void Export_JsonDeclaresOneMeshOneMaterialAndThreeAccessors()
        {
            Assert.IsTrue(GlbExporter.TryExportObject(Desk(), out byte[] glb, out _));

            string json = ReadJsonChunk(glb).TrimEnd(' ');

            StringAssert.Contains("\"version\":\"2.0\"", json);
            StringAssert.Contains("\"POSITION\":0", json);
            StringAssert.Contains("\"NORMAL\":1", json);
            StringAssert.Contains("\"indices\":2", json);
            StringAssert.Contains("\"material\":0", json);
            StringAssert.Contains("\"pbrMetallicRoughness\"", json);

            Assert.AreEqual(3, CountOccurrences(json, "\"bufferView\":"), "three accessors");
            Assert.AreEqual(3, CountOccurrences(json, "\"byteLength\":") - 1, "three bufferViews plus one buffer");
            Assert.AreEqual(1, CountOccurrences(json, "\"primitives\":"));
        }

        /// <summary>
        /// A comma-decimal locale would otherwise emit `0,8` and produce JSON no
        /// glTF reader can parse — a bug invisible on an English machine.
        /// </summary>
        [Test]
        public void Export_FormatsNumbersInvariantly()
        {
            Assert.IsTrue(GlbExporter.TryExportObject(Desk(), out byte[] glb, out _));

            string json = ReadJsonChunk(glb).TrimEnd(' ');

            // The colour components and the min/max bounds all go through the
            // number formatter, so assert one of those rather than a literal
            // the writer hardcodes. A desk's red channel is 0.58.
            Color desk = FurnitureFactory.ColorFor("desk");
            Assert.AreEqual(0.58f, desk.r, 1e-3f, "fixture assumption");

            StringAssert.Contains("0.58", json);
            StringAssert.DoesNotContain("0,58", json);
            StringAssert.DoesNotContain("0,7", json);
        }

        [Test]
        public void Export_PositionBoundsAreTheObjectsRealSizeInMeters()
        {
            SceneObjectModel desk = Desk(widthM: 1.4f, depthM: 0.7f, heightM: 0.73f);

            Assert.IsTrue(GlbExporter.TryExportObject(desk, out byte[] glb, out _));

            string json = ReadJsonChunk(glb).TrimEnd(' ');

            float[] min = ReadVec3After(json, "\"min\":[");
            float[] max = ReadVec3After(json, "\"max\":[");

            // Parts are laid out in the object's own frame: centred on x and z,
            // sitting on y = 0.
            Assert.AreEqual(-0.7f, min[0], 0.01f, "min x");
            Assert.AreEqual(0.7f, max[0], 0.01f, "max x");

            Assert.AreEqual(0f, min[1], 0.01f, "min y — the object sits on the floor");
            Assert.AreEqual(0.73f, max[1], 0.01f, "max y — the measured height");

            Assert.AreEqual(0.7f, max[2] - min[2], 0.02f, "depth");
        }

        /// <summary>
        /// The measured dimensions must reach the asset. A detected desk is not
        /// the type default, and exporting the default would silently throw away
        /// the entire point of ADR-0006.
        /// </summary>
        [Test]
        public void Export_CarriesMeasuredDimensionsNotTypeDefaults()
        {
            Assert.IsTrue(FurnitureValidator.TryGetDefaultDimensions(
                "desk", out float defaultW, out _, out float defaultH));

            // A deliberately unusual real desk.
            SceneObjectModel measured = Desk(widthM: 1.83f, depthM: 0.62f, heightM: 0.69f);

            Assert.AreNotEqual(defaultW, measured.widthM, "fixture must differ from the default");
            Assert.AreNotEqual(defaultH, measured.heightM, "fixture must differ from the default");

            Assert.IsTrue(GlbExporter.TryExportObject(measured, out byte[] glb, out _));

            string json = ReadJsonChunk(glb).TrimEnd(' ');

            float[] min = ReadVec3After(json, "\"min\":[");
            float[] max = ReadVec3After(json, "\"max\":[");

            Assert.AreEqual(1.83f, max[0] - min[0], 0.02f, "exported width");
            Assert.AreEqual(0.69f, max[1], 0.02f, "exported height");
        }

        // -------------------------------------------------------------------
        // Geometry is the renderer's
        // -------------------------------------------------------------------

        /// <summary>
        /// ADR-0006 forbids the exporter from describing furniture itself. A box
        /// is 24 vertices and 36 indices, so the counts must track
        /// <see cref="FurnitureFactory.BuildParts"/> exactly, for every type.
        /// </summary>
        [Test]
        public void Export_GeometryCountsTrackTheRenderersPartTable()
        {
            for (int i = 0; i < FurnitureValidator.SupportedTypes.Count; i++)
            {
                string type = FurnitureValidator.SupportedTypes[i];

                Assert.IsTrue(FurnitureValidator.TryGetDefaultDimensions(
                    type, out float w, out float d, out float h), type);

                var model = new SceneObjectModel
                {
                    id = $"abcd1234-{i}",
                    type = type,
                    center = new Vec3Dto(1f, 0f, 1f),
                    yawDeg = 0f,
                    widthM = w,
                    depthM = d,
                    heightM = h
                };

                int partCount = FurnitureFactory.BuildParts(model).Count;
                Assert.Greater(partCount, 0, type);

                Assert.IsTrue(
                    GlbExporter.TryExportObject(model, out byte[] glb, out string error),
                    $"{type}: {error}");

                string json = ReadJsonChunk(glb).TrimEnd(' ');

                int vertexCount = ReadIntAfter(json, "\"count\":");
                Assert.AreEqual(partCount * 24, vertexCount, $"{type} vertex count");

                int lastCount = ReadLastIntAfter(json, "\"count\":");
                Assert.AreEqual(partCount * 36, lastCount, $"{type} index count");
            }
        }

        [Test]
        public void Export_UsesTheRenderersColourForTheType()
        {
            Assert.IsTrue(GlbExporter.TryExportObject(Desk(), out byte[] glb, out _));

            string json = ReadJsonChunk(glb).TrimEnd(' ');
            float[] baseColor = ReadVec3After(json, "\"baseColorFactor\":[");

            Color expected = FurnitureFactory.ColorFor("desk");

            Assert.AreEqual(expected.r, baseColor[0], 1e-3f);
            Assert.AreEqual(expected.g, baseColor[1], 1e-3f);
            Assert.AreEqual(expected.b, baseColor[2], 1e-3f);
        }

        // -------------------------------------------------------------------
        // Refusals
        // -------------------------------------------------------------------

        [Test]
        public void Export_RefusesANullModel()
        {
            Assert.IsFalse(GlbExporter.TryExportObject(null, out byte[] glb, out string error));
            Assert.IsNull(glb);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Export_RefusesAModelThatProducesNoGeometry()
        {
            // Zero dimensions: the part table rejects it, so there is nothing
            // to write.
            var degenerate = new SceneObjectModel
            {
                id = "dead0000-0000",
                type = "desk",
                center = new Vec3Dto(0f, 0f, 0f),
                yawDeg = 0f,
                widthM = 0f,
                depthM = 0f,
                heightM = 0f
            };

            Assert.IsFalse(GlbExporter.TryExportObject(degenerate, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        /// <summary>
        /// The renderer refuses a type outside the shared schema rather than
        /// inventing geometry for it, and the exporter must follow that rather
        /// than second-guessing it — otherwise a `.glb` could exist for an
        /// object the viewer will not draw.
        /// </summary>
        [Test]
        public void Export_RefusesATypeOutsideTheSharedSchema()
        {
            var odd = new SceneObjectModel
            {
                id = "feed0000-0000",
                type = "piano",
                center = new Vec3Dto(1f, 0f, 1f),
                yawDeg = 0f,
                widthM = 1.5f,
                depthM = 0.6f,
                heightM = 1.1f
            };

            Assert.IsFalse(FurnitureValidator.IsSupportedType("piano"));
            Assert.AreEqual(0, FurnitureFactory.BuildParts(odd).Count);

            Assert.IsFalse(GlbExporter.TryExportObject(odd, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        // -------------------------------------------------------------------
        // File writing
        // -------------------------------------------------------------------

        [Test]
        public void ExportRoomObjects_WritesOneFilePerObject()
        {
            string directory = NewTempDirectory();

            try
            {
                RoomModel room = RoomWith(
                    Desk(),
                    Bed("aaaa1111-2222"),
                    Bed("bbbb3333-4444"));

                Assert.IsTrue(FurnitureAssetExporter.TryExportRoomObjects(
                    room, directory,
                    out IReadOnlyList<AssetExportResult> results,
                    out string error), error);

                Assert.AreEqual(3, results.Count);

                for (int i = 0; i < results.Count; i++)
                {
                    Assert.IsTrue(results[i].Succeeded, results[i].Error);
                    Assert.IsTrue(File.Exists(results[i].Path), results[i].Path);
                }

                string[] written = Directory.GetFiles(directory, "*.glb");
                Assert.AreEqual(3, written.Length, "one asset per object, no leftovers");

                // And no temp files survived.
                Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ExportRoomObjects_FileNamesAreUniqueAndReadable()
        {
            string directory = NewTempDirectory();

            try
            {
                RoomModel room = RoomWith(Desk(), Bed("aaaa1111-2222"));

                Assert.IsTrue(FurnitureAssetExporter.TryExportRoomObjects(
                    room, directory, out IReadOnlyList<AssetExportResult> results, out _));

                string deskName = Path.GetFileName(results[0].Path);
                string bedName = Path.GetFileName(results[1].Path);

                StringAssert.StartsWith("desk-", deskName);
                StringAssert.StartsWith("bed-", bedName);
                StringAssert.EndsWith(".glb", deskName);
                Assert.AreNotEqual(deskName, bedName);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        /// <summary>
        /// Two objects of the same type whose ids share a first segment must not
        /// overwrite one another.
        /// </summary>
        [Test]
        public void ExportRoomObjects_DoesNotCollideOnDuplicateShortIds()
        {
            string directory = NewTempDirectory();

            try
            {
                RoomModel room = RoomWith(Bed("same0000-1111"), Bed("same0000-2222"));

                Assert.IsTrue(FurnitureAssetExporter.TryExportRoomObjects(
                    room, directory, out IReadOnlyList<AssetExportResult> results, out _));

                Assert.AreNotEqual(results[0].Path, results[1].Path);
                Assert.AreEqual(2, Directory.GetFiles(directory, "*.glb").Length);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ExportRoomObjects_RefusesARoomWithNoFurniture()
        {
            string directory = NewTempDirectory();

            try
            {
                RoomModel room = RoomWith();

                Assert.IsFalse(FurnitureAssetExporter.TryExportRoomObjects(
                    room, directory, out IReadOnlyList<AssetExportResult> results, out string error));

                Assert.IsEmpty(results);
                Assert.IsNotEmpty(error);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        /// <summary>
        /// One bad object must not cost the user the other good ones.
        /// </summary>
        [Test]
        public void ExportRoomObjects_ReportsPerObjectFailuresWithoutAbortingTheRest()
        {
            string directory = NewTempDirectory();

            try
            {
                var broken = new SceneObjectModel
                {
                    id = "0bad0000-0000",
                    type = "desk",
                    center = new Vec3Dto(0f, 0f, 0f),
                    yawDeg = 0f,
                    widthM = 0f,
                    depthM = 0f,
                    heightM = 0f
                };

                RoomModel room = RoomWith(Desk(), broken, Bed("cccc5555-6666"));

                Assert.IsTrue(FurnitureAssetExporter.TryExportRoomObjects(
                    room, directory, out IReadOnlyList<AssetExportResult> results, out _));

                Assert.AreEqual(3, results.Count);
                Assert.IsTrue(results[0].Succeeded);
                Assert.IsFalse(results[1].Succeeded);
                Assert.IsNotEmpty(results[1].Error);
                Assert.IsTrue(results[2].Succeeded);

                Assert.AreEqual(2, Directory.GetFiles(directory, "*.glb").Length);
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ExportOne_OverwritesAnExistingAssetAtomically()
        {
            string directory = NewTempDirectory();

            try
            {
                string path = Path.Combine(directory, "desk.glb");

                Assert.IsTrue(FurnitureAssetExporter.TryExportOne(
                    Desk(heightM: 0.70f), path, out string first), first);
                byte[] before = File.ReadAllBytes(path);

                Assert.IsTrue(FurnitureAssetExporter.TryExportOne(
                    Desk(heightM: 0.75f), path, out string second), second);
                byte[] after = File.ReadAllBytes(path);

                Assert.IsTrue(File.Exists(path));
                Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length,
                    "an atomic write must leave no temp file behind");

                // A different measured height must actually reach the asset.
                // File length may legitimately differ, since the bounds are
                // written as decimal text.
                Assert.AreNotEqual(
                    Convert.ToBase64String(before),
                    Convert.ToBase64String(after),
                    "the re-export must have replaced the file's contents");
            }
            finally
            {
                DeleteTempDirectory(directory);
            }
        }

        [Test]
        public void ExportOne_RefusesAnEmptyPath()
        {
            Assert.IsFalse(FurnitureAssetExporter.TryExportOne(Desk(), string.Empty, out string error));
            Assert.IsNotEmpty(error);
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static SceneObjectModel Bed(string id)
        {
            return new SceneObjectModel
            {
                id = id,
                type = "bed",
                center = new Vec3Dto(1f, 0f, 2f),
                yawDeg = 90f,
                widthM = 1.52f,
                depthM = 2.03f,
                heightM = 0.6f
            };
        }

        private static RoomModel RoomWith(params SceneObjectModel[] objects)
        {
            return new RoomModel
            {
                id = "room-1",
                name = "Bedroom",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
                },
                openings = new OpeningModel[0],
                objects = objects ?? new SceneObjectModel[0]
            };
        }

        private static uint ReadUInt(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }

        private static string ReadJsonChunk(byte[] glb)
        {
            uint jsonLength = ReadUInt(glb, 12);
            return Encoding.UTF8.GetString(glb, 20, (int)jsonLength);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;

            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static float[] ReadVec3After(string json, string marker)
        {
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, $"'{marker}' not found in glTF JSON");

            start += marker.Length;
            int end = json.IndexOf(']', start);
            Assert.Greater(end, start);

            string[] parts = json.Substring(start, end - start).Split(',');
            Assert.GreaterOrEqual(parts.Length, 3);

            var values = new float[3];

            for (int i = 0; i < 3; i++)
            {
                values[i] = float.Parse(
                    parts[i], System.Globalization.CultureInfo.InvariantCulture);
            }

            return values;
        }

        private static int ReadIntAfter(string json, string marker)
        {
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, $"'{marker}' not found");

            return ParseIntAt(json, start + marker.Length);
        }

        private static int ReadLastIntAfter(string json, string marker)
        {
            int start = json.LastIndexOf(marker, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, $"'{marker}' not found");

            return ParseIntAt(json, start + marker.Length);
        }

        private static int ParseIntAt(string json, int index)
        {
            int end = index;

            while (end < json.Length && char.IsDigit(json[end]))
            {
                end++;
            }

            Assert.Greater(end, index, "expected a number");

            return int.Parse(
                json.Substring(index, end - index),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string NewTempDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(), "ghostmap-glb-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTempDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception)
            {
                // A leaked temp directory must not fail a test that passed.
            }
        }
    }
}
