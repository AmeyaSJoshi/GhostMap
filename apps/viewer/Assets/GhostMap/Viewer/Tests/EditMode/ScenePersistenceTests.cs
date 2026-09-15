using System;
using System.IO;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Persistence;
using GhostMap.Viewer.Scene;
using NUnit.Framework;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V6's persistence core. Every test round-trips a real, fully
    /// validated <see cref="SceneSnapshot"/> through <see cref="ScenePersistence"/>
    /// on a real temp file — never a mock file system — so a save/load bug
    /// in the actual <c>JsonUtility</c>/<see cref="File"/> path is caught.
    /// </summary>
    public sealed class ScenePersistenceTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ghostmap-persistence-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        private string TempFile(string name = "scene.json") => Path.Combine(_tempDir, name);

        private static SceneSnapshot BuildSnapshot(
            string sessionId = "save-load-session-1",
            int revision = 3,
            bool finalized = true)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = sessionId,
                revision = revision,
                scanPhase = finalized ? "Finalized" : "AddObjects",
                finalized = finalized,
                closureErrorM = 0.033f,
                room = new RoomModel
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
                    openings = new[]
                    {
                        new OpeningModel
                        {
                            id = "door-1",
                            type = "door",
                            wallStartCornerId = "c0",
                            wallEndCornerId = "c1",
                            offsetM = 1.0f,
                            widthM = 0.9f,
                            sillHeightM = 0f,
                            heightM = 2.0f
                        }
                    },
                    objects = new[]
                    {
                        new SceneObjectModel
                        {
                            id = "bed-1",
                            type = "bed",
                            center = new Vec3Dto(1.5f, 0f, 1.5f),
                            yawDeg = 45f,
                            widthM = 1.52f,
                            depthM = 2.03f,
                            heightM = 0.6f
                        }
                    }
                }
            };
        }

        private static void AssertSemanticallyEqual(SceneSnapshot expected, SceneSnapshot actual)
        {
            Assert.AreEqual(expected.schemaVersion, actual.schemaVersion);
            Assert.AreEqual(expected.sessionId, actual.sessionId);
            Assert.AreEqual(expected.revision, actual.revision);
            Assert.AreEqual(expected.scanPhase, actual.scanPhase);
            Assert.AreEqual(expected.finalized, actual.finalized);
            Assert.AreEqual(expected.closureErrorM, actual.closureErrorM, 1e-5f);

            Assert.AreEqual(expected.room.id, actual.room.id);
            Assert.AreEqual(expected.room.name, actual.room.name);
            Assert.AreEqual(expected.room.heightM, actual.room.heightM, 1e-5f);

            Assert.AreEqual(expected.room.corners.Length, actual.room.corners.Length);
            for (int i = 0; i < expected.room.corners.Length; i++)
            {
                CornerModel e = expected.room.corners[i];
                CornerModel a = actual.room.corners[i];
                Assert.AreEqual(e.id, a.id, $"corner {i} id");
                Assert.AreEqual(e.position.x, a.position.x, 1e-5f, $"corner {i} x");
                Assert.AreEqual(e.position.y, a.position.y, 1e-5f, $"corner {i} y");
                Assert.AreEqual(e.position.z, a.position.z, 1e-5f, $"corner {i} z");
            }

            Assert.AreEqual(expected.room.openings.Length, actual.room.openings.Length);
            for (int i = 0; i < expected.room.openings.Length; i++)
            {
                OpeningModel e = expected.room.openings[i];
                OpeningModel a = actual.room.openings[i];
                Assert.AreEqual(e.id, a.id, $"opening {i} id");
                Assert.AreEqual(e.type, a.type, $"opening {i} type");
                Assert.AreEqual(e.wallStartCornerId, a.wallStartCornerId, $"opening {i} wallStartCornerId");
                Assert.AreEqual(e.wallEndCornerId, a.wallEndCornerId, $"opening {i} wallEndCornerId");
                Assert.AreEqual(e.offsetM, a.offsetM, 1e-5f, $"opening {i} offsetM");
                Assert.AreEqual(e.widthM, a.widthM, 1e-5f, $"opening {i} widthM");
                Assert.AreEqual(e.sillHeightM, a.sillHeightM, 1e-5f, $"opening {i} sillHeightM");
                Assert.AreEqual(e.heightM, a.heightM, 1e-5f, $"opening {i} heightM");
            }

            Assert.AreEqual(expected.room.objects.Length, actual.room.objects.Length);
            for (int i = 0; i < expected.room.objects.Length; i++)
            {
                SceneObjectModel e = expected.room.objects[i];
                SceneObjectModel a = actual.room.objects[i];
                Assert.AreEqual(e.id, a.id, $"object {i} id");
                Assert.AreEqual(e.type, a.type, $"object {i} type");
                Assert.AreEqual(e.center.x, a.center.x, 1e-5f, $"object {i} center.x");
                Assert.AreEqual(e.center.y, a.center.y, 1e-5f, $"object {i} center.y");
                Assert.AreEqual(e.center.z, a.center.z, 1e-5f, $"object {i} center.z");
                Assert.AreEqual(e.yawDeg, a.yawDeg, 1e-5f, $"object {i} yawDeg");
                Assert.AreEqual(e.widthM, a.widthM, 1e-5f, $"object {i} widthM");
                Assert.AreEqual(e.depthM, a.depthM, 1e-5f, $"object {i} depthM");
                Assert.AreEqual(e.heightM, a.heightM, 1e-5f, $"object {i} heightM");
            }
        }

        // ---- 1-2: save fixture, load fixture ----

        [Test]
        public void SavesAValidSnapshotToDisk()
        {
            bool saved = ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out string error);

            Assert.IsTrue(saved, error);
            Assert.IsTrue(File.Exists(TempFile()));
        }

        [Test]
        public void LoadsBackAPreviouslySavedSnapshot()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);

            bool loaded = ScenePersistence.TryLoad(TempFile(), out SceneSnapshot snapshot, out string error);

            Assert.IsTrue(loaded, error);
            Assert.IsNotNull(snapshot);
        }

        // ---- 3: save -> load semantic equality (covers 4-11 together) ----

        [Test]
        public void SaveThenLoadPreservesEverySemanticField()
        {
            SceneSnapshot original = BuildSnapshot();
            ScenePersistence.TrySave(original, TempFile(), out string saveError);

            bool loaded = ScenePersistence.TryLoad(TempFile(), out SceneSnapshot roundTripped, out string loadError);

            Assert.IsTrue(loaded, loadError ?? saveError);
            AssertSemanticallyEqual(original, roundTripped);
        }

        // ---- 4-10: individually named per the task brief's checklist ----

        [Test]
        public void CornersArePreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            Assert.AreEqual(4, loaded.room.corners.Length, error);
            Assert.AreEqual("c2", loaded.room.corners[2].id);
            Assert.AreEqual(4f, loaded.room.corners[2].position.x, 1e-5f);
            Assert.AreEqual(3f, loaded.room.corners[2].position.z, 1e-5f);
        }

        [Test]
        public void HeightIsPreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            Assert.AreEqual(2.5f, loaded.room.heightM, 1e-5f, error);
        }

        [Test]
        public void OpeningsArePreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            Assert.AreEqual(1, loaded.room.openings.Length, error);
            Assert.AreEqual("door-1", loaded.room.openings[0].id);
            Assert.AreEqual("door", loaded.room.openings[0].type);
        }

        [Test]
        public void FurnitureIsPreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            Assert.AreEqual(1, loaded.room.objects.Length, error);
            Assert.AreEqual("bed-1", loaded.room.objects[0].id);
            Assert.AreEqual("bed", loaded.room.objects[0].type);
        }

        [Test]
        public void FurniturePositionIsPreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            Vec3Dto center = loaded.room.objects[0].center;
            Assert.AreEqual(1.5f, center.x, 1e-5f, error);
            Assert.AreEqual(1.5f, center.z, 1e-5f, error);
        }

        [Test]
        public void FurnitureDimensionsArePreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            SceneObjectModel obj = loaded.room.objects[0];
            Assert.AreEqual(1.52f, obj.widthM, 1e-5f, error);
            Assert.AreEqual(2.03f, obj.depthM, 1e-5f);
            Assert.AreEqual(0.6f, obj.heightM, 1e-5f);
        }

        [Test]
        public void FurnitureYawIsPreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            Assert.AreEqual(45f, loaded.room.objects[0].yawDeg, 1e-5f, error);
        }

        [Test]
        public void FinalizedFlagIsPreserved()
        {
            ScenePersistence.TrySave(BuildSnapshot(finalized: true), TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string error);

            Assert.IsTrue(loaded.finalized, error);
        }

        // ---- 12: Viewer edits are what get saved, not the stale scanner state ----

        [Test]
        public void SavingTheEditableSceneSavesTheEditNotTheOriginalScannerData()
        {
            var store = new ViewerSceneStore();
            var editable = new ViewerEditableScene();
            editable.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot(finalized: true), out _);
            Assert.IsTrue(editable.EditingEnabled);

            var edited = new SceneObjectModel
            {
                id = "bed-1",
                type = "bed",
                center = new Vec3Dto(3.0f, 0f, 0.5f),
                yawDeg = 180f,
                widthM = 1.52f,
                depthM = 2.03f,
                heightM = 0.6f
            };
            Assert.IsTrue(editable.TryApplyLocalEdit(edited, out string editError), editError);

            // The scanner's own stale copy still has the pre-edit position —
            // saving must persist ViewerEditableScene.Current, never
            // ViewerSceneStore.Current.
            Assert.AreEqual(1.5f, store.Current.room.objects[0].center.x, 1e-5f);

            bool saved = ScenePersistence.TrySave(editable.Current, TempFile(), out string saveError);
            Assert.IsTrue(saved, saveError);

            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string loadError);

            Assert.AreEqual(3.0f, loaded.room.objects[0].center.x, 1e-5f, loadError);
            Assert.AreEqual(180f, loaded.room.objects[0].yawDeg, 1e-5f);
        }

        // ---- 13-15: rejection paths ----

        [Test]
        public void InvalidJsonIsRejected()
        {
            File.WriteAllText(TempFile(), "{ this is not valid json");

            bool loaded = ScenePersistence.TryLoad(TempFile(), out SceneSnapshot snapshot, out string error);

            Assert.IsFalse(loaded);
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void UnsupportedSchemaVersionIsRejected()
        {
            SceneSnapshot future = BuildSnapshot();
            future.schemaVersion = ProtocolConstants.SchemaVersion + 1;
            File.WriteAllText(TempFile(), UnityEngine.JsonUtility.ToJson(future));

            bool loaded = ScenePersistence.TryLoad(TempFile(), out SceneSnapshot snapshot, out string error);

            Assert.IsFalse(loaded);
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void MissingRequiredSceneDataIsRejected()
        {
            // Well-formed JSON with no "room" key at all — schema-conformant
            // but missing the required scene data entirely (protocol v1
            // section 4: "scene.snapshot with no room" is a rejection case).
            const string json = "{\"schemaVersion\":1,\"sessionId\":\"no-room\",\"revision\":0," +
                                 "\"scanPhase\":\"AddObjects\",\"finalized\":false,\"closureErrorM\":0.0}";
            File.WriteAllText(TempFile(), json);

            bool loaded = ScenePersistence.TryLoad(TempFile(), out SceneSnapshot snapshot, out string error);

            Assert.IsFalse(loaded);
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void AnIllegalRoomFailingSharedValidationIsRejected()
        {
            // Well-formed JSON with a present, structurally complete room
            // that nonetheless breaks a RoomValidator rule (height outside
            // 2.0-4.0 m) — the same domain validation every scanner
            // snapshot goes through, one layer past FixtureLoader's own
            // well-formedness check.
            SceneSnapshot broken = BuildSnapshot();
            broken.room.heightM = 5.2f;
            File.WriteAllText(TempFile(), UnityEngine.JsonUtility.ToJson(broken));

            bool loaded = ScenePersistence.TryLoad(TempFile(), out SceneSnapshot snapshot, out string error);

            Assert.IsFalse(loaded);
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(error);
        }

        // ---- 16: failed load leaves the current Viewer scene unchanged ----

        [Test]
        public void FailedLoadNeverInstallsOverTheCurrentScene()
        {
            var editable = new ViewerEditableScene();
            var store = new ViewerSceneStore();
            editable.Attach(store);
            store.TryApplyScannerSnapshot(BuildSnapshot(sessionId: "good-session", finalized: true), out _);
            SceneObjectModel before = editable.Current.room.objects[0];

            File.WriteAllText(TempFile(), "not json at all");
            bool loaded = ScenePersistence.TryLoad(TempFile(), out SceneSnapshot badSnapshot, out string error);

            Assert.IsFalse(loaded, error);
            Assert.IsNull(badSnapshot);
            // The caller (ViewerHudController) never calls LoadExternalSnapshot
            // when TryLoad fails, so Current is provably untouched here too.
            Assert.AreSame(before, editable.Current.room.objects[0]);
            Assert.AreEqual("good-session", editable.Current.sessionId);
        }

        // ---- 17: file-not-found ----

        [Test]
        public void FileNotFoundIsHandledCleanly()
        {
            bool loaded = ScenePersistence.TryLoad(TempFile("does-not-exist.json"), out SceneSnapshot snapshot, out string error);

            Assert.IsFalse(loaded);
            Assert.IsNull(snapshot);
            Assert.IsNotEmpty(error);
        }

        // ---- 18: repeated save/load does not drift semantic values ----

        [Test]
        public void RepeatedSaveLoadDoesNotDriftValues()
        {
            SceneSnapshot original = BuildSnapshot();

            ScenePersistence.TrySave(original, TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot firstRoundTrip, out string firstError);
            Assert.IsNotNull(firstRoundTrip, firstError);

            ScenePersistence.TrySave(firstRoundTrip, TempFile(), out _);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot secondRoundTrip, out string secondError);
            Assert.IsNotNull(secondRoundTrip, secondError);

            AssertSemanticallyEqual(original, secondRoundTrip);
        }

        // ---- Safe writes: save never leaves a half-written file behind ----

        [Test]
        public void SaveOverwritesAnExistingFileIntentionally()
        {
            ScenePersistence.TrySave(BuildSnapshot(revision: 1), TempFile(), out _);
            bool savedAgain = ScenePersistence.TrySave(BuildSnapshot(revision: 2), TempFile(), out string error);

            Assert.IsTrue(savedAgain, error);
            ScenePersistence.TryLoad(TempFile(), out SceneSnapshot loaded, out string loadError);
            Assert.AreEqual(2, loaded.revision, loadError);
        }

        [Test]
        public void SaveDoesNotLeaveATemporaryFileBehindOnSuccess()
        {
            ScenePersistence.TrySave(BuildSnapshot(), TempFile(), out string error);

            Assert.IsFalse(File.Exists(TempFile() + ".tmp"), error);
        }
    }
}
