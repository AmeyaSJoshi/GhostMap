using System;
using System.IO;
using System.Linq;
using GhostMap.Scanner.Export;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>Task R6: saved rooms on the phone.</summary>
    public sealed class SavedScanStoreTests
    {
        private string directory;
        private SavedScanStore store;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ghostmap-scans-" + Guid.NewGuid().ToString("N"));
            store = new SavedScanStore(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Save_WritesTheBytesAndLeavesNoTempFile()
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            Assert.IsTrue(store.TrySave(bytes, "GhostMap-Room-20261011-1605.zip", out SavedScan saved, out string error), error);

            Assert.AreEqual("GhostMap-Room-20261011-1605.zip", saved.FileName);
            Assert.AreEqual(4, saved.SizeBytes);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(saved.Path));
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }

        [Test]
        public void Save_CreatesTheFolderWhenMissing()
        {
            Assert.IsFalse(Directory.Exists(directory));
            Assert.IsTrue(store.TrySave(new byte[] { 1 }, "a.zip", out _, out _));
            Assert.IsTrue(Directory.Exists(directory));
        }

        [Test]
        public void Save_TwiceInOneMinuteKeepsBoth()
        {
            Assert.IsTrue(store.TrySave(new byte[] { 1 }, "GhostMap-Room-x.zip", out SavedScan first, out _));
            Assert.IsTrue(store.TrySave(new byte[] { 2 }, "GhostMap-Room-x.zip", out SavedScan second, out _));

            Assert.AreEqual("GhostMap-Room-x.zip", first.FileName);
            Assert.AreEqual("GhostMap-Room-x-2.zip", second.FileName);
            Assert.AreEqual(2, store.List().Count);
        }

        [Test]
        public void Save_RefusesEmptyBytesAndNamesThatEscapeTheFolder()
        {
            Assert.IsFalse(store.TrySave(Array.Empty<byte>(), "a.zip", out _, out string error));
            Assert.IsNotEmpty(error);

            foreach (string bad in new[] { null, "", "../a.zip", "sub/a.zip", "a.txt", ".." })
            {
                Assert.IsFalse(store.TrySave(new byte[] { 1 }, bad, out _, out error), bad ?? "null");
                Assert.IsNotEmpty(error);
            }

            Assert.IsFalse(Directory.Exists(directory) && Directory.GetFiles(directory).Any());
        }

        [Test]
        public void List_IsNewestFirstAndIgnoresOtherFiles()
        {
            Assert.IsTrue(store.TrySave(new byte[] { 1 }, "old.zip", out SavedScan old, out _));
            Assert.IsTrue(store.TrySave(new byte[] { 2 }, "new.zip", out SavedScan latest, out _));
            File.SetLastWriteTimeUtc(old.Path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(latest.Path, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "not a scan");

            CollectionAssert.AreEqual(new[] { "new.zip", "old.zip" }, store.List().Select(s => s.FileName));
        }

        [Test]
        public void List_OfAMissingFolderIsEmpty()
        {
            Assert.AreEqual(0, store.List().Count);
        }

        [Test]
        public void Delete_RemovesOnlyThatScan()
        {
            Assert.IsTrue(store.TrySave(new byte[] { 1 }, "a.zip", out _, out _));
            Assert.IsTrue(store.TrySave(new byte[] { 2 }, "b.zip", out _, out _));

            Assert.IsTrue(store.Delete("a.zip"));
            Assert.IsFalse(store.Delete("a.zip"), "already gone");
            Assert.IsFalse(store.Delete("../b.zip"), "no path escapes");
            CollectionAssert.AreEqual(new[] { "b.zip" }, store.List().Select(s => s.FileName));
        }
    }
}
