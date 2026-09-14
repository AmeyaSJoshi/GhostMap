using System.IO;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Scene;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V1's "Load fixture" developer button, proven against the real
    /// Task F3 fixture files in <c>fixtures/</c> — the same files
    /// <c>tools/send_fixture.py</c> and <c>tools/inspect_snapshot.py</c> use —
    /// rather than an inline JSON string, so a fixture format drift breaks
    /// this test too.
    /// </summary>
    public sealed class FixtureLoaderTests
    {
        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static string FixturePath(string fileName)
            => Path.Combine(RepoRoot(), "fixtures", fileName);

        [Test]
        public void LoadsTheValidRoomFixture()
        {
            bool ok = FixtureLoader.TryLoadFromFile(
                FixturePath("valid-room-v1.json"), out SceneSnapshot snapshot, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("fixture-valid-room-v1", snapshot.sessionId);
            Assert.AreEqual(4, snapshot.room.corners.Length);
            Assert.IsTrue(snapshot.finalized);
        }

        [Test]
        public void LoadsTheDoorAndWindowFixture()
        {
            bool ok = FixtureLoader.TryLoadFromFile(
                FixturePath("room-with-door-window-v1.json"), out SceneSnapshot snapshot, out string error);

            Assert.IsTrue(ok, error);
            Assert.GreaterOrEqual(snapshot.room.openings.Length, 2, "Expected at least the door and the window.");
        }

        [Test]
        public void TheMalformedFixtureParsesStructurallyButFailsSceneValidation()
        {
            // FixtureLoader only checks schema conformance (protocol v1
            // section 4's distinction between "well-formed" and "valid"); the
            // malformed fixture is schema-valid JSON with exactly one broken
            // rule (heightM outside 2.0-4.0 m), so the loader must accept it
            // and ViewerSceneStore's RoomValidator must be what rejects it.
            bool loaded = FixtureLoader.TryLoadFromFile(
                FixturePath("malformed-room-v1.json"), out SceneSnapshot snapshot, out string loadError);
            Assert.IsTrue(loaded, loadError);

            var store = new ViewerSceneStore();
            bool applied = store.TryApplyScannerSnapshot(snapshot, out string applyError);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(applyError);
            Assert.IsNull(store.Current);
        }

        [Test]
        public void MissingFileIsRejectedWithAClearError()
        {
            bool ok = FixtureLoader.TryLoadFromFile(
                FixturePath("does-not-exist.json"), out SceneSnapshot snapshot, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void EmptyJsonIsRejected()
        {
            bool ok = FixtureLoader.TryLoadFromJson(string.Empty, out SceneSnapshot snapshot, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(error);
        }
    }
}
