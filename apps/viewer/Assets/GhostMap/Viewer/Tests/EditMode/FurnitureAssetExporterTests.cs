using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using GhostMap.Viewer.Export;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// ADR-0006 asset export, file writing. The `.glb` bytes themselves are
    /// tested in the shared package's GlbWriterTests (moved there in task R6).
    /// </summary>
    public sealed class FurnitureAssetExporterTests
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
