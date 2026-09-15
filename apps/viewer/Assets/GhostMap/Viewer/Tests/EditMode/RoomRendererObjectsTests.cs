using System.IO;
using System.Linq;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Shared.Validation;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V4's furniture inside <see cref="RoomRenderer"/>: objects must
    /// appear, update and disappear with the accepted scene, and must obey the
    /// same live-update guarantees V2 and V3 already hold to — nothing
    /// accumulates, a duplicate revision changes nothing, a stale revision
    /// never overwrites newer geometry, and a disconnect leaves the last
    /// rendered room alone.
    /// </summary>
    public sealed class RoomRendererObjectsTests
    {
        private GameObject _hostGo;
        private RoomRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _hostGo = new GameObject("RoomRendererObjectsTestHost");
            _renderer = _hostGo.AddComponent<RoomRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.Detach();
            Object.DestroyImmediate(_hostGo);
        }

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static Transform FindChild(Transform parent, string name)
            => parent.Cast<Transform>().FirstOrDefault(t => t.name == name);

        private Transform Objects() => FindChild(_renderer.Root.transform, "Objects");

        private Transform ObjectById(string id)
            => Objects().Cast<Transform>()
                .First(t => t.GetComponent<SceneObjectBinding>().ObjectId == id);

        private static SceneObjectModel Obj(
            string id,
            string type = "generic",
            float x = 1f,
            float z = 1f,
            float yawDeg = 0f,
            float widthM = 1f,
            float depthM = 1f,
            float heightM = 1f)
        {
            return new SceneObjectModel
            {
                id = id,
                type = type,
                center = new Vec3Dto(x, 0f, z),
                yawDeg = yawDeg,
                widthM = widthM,
                depthM = depthM,
                heightM = heightM
            };
        }

        private static SceneSnapshot Snapshot(int revision, params SceneObjectModel[] objects)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = "session-1",
                revision = revision,
                scanPhase = "AddObjects",
                finalized = false,
                closureErrorM = 0.05f,
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
                    openings = new OpeningModel[0],
                    objects = objects ?? new SceneObjectModel[0]
                }
            };
        }

        [Test]
        public void ZeroObjectsRendersNoFurniture()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            Assert.IsTrue(store.TryApplyScannerSnapshot(Snapshot(0), out string error), error);

            Assert.IsNotNull(Objects());
            Assert.AreEqual(0, Objects().childCount);
        }

        [Test]
        public void OneObjectRendersOneLogicalRoot()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(0, Obj("a")), out _);

            Assert.AreEqual(1, Objects().childCount);
            Assert.IsNotNull(Objects().GetChild(0).GetComponent<SceneObjectBinding>());
        }

        [Test]
        public void MultipleObjectsRenderIndependently()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(
                Snapshot(0,
                    Obj("a", "bed", x: 1f, z: 2.2f),
                    Obj("b", "desk", x: 3.2f, z: 0.5f),
                    Obj("c", "chair", x: 3.2f, z: 1.3f)),
                out _);

            Assert.AreEqual(3, Objects().childCount);
            Assert.AreEqual(1f, ObjectById("a").position.x, 1e-3f);
            Assert.AreEqual(3.2f, ObjectById("b").position.x, 1e-3f);
            Assert.AreEqual(1.3f, ObjectById("c").position.z, 1e-3f);
        }

        [Test]
        public void EverySupportedTypeRendersInsideTheRoom()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            SceneObjectModel[] models = FurnitureValidator.SupportedTypes
                .Select((type, i) => Obj($"o{i}", type, x: 0.5f + i * 0.4f, z: 1.5f, heightM: 0.8f))
                .ToArray();

            Assert.IsTrue(store.TryApplyScannerSnapshot(Snapshot(0, models), out string error), error);

            Assert.AreEqual(models.Length, Objects().childCount);
        }

        [Test]
        public void NewerSnapshotUpdatesAnObjectTransform()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(0, Obj("a", x: 1f, z: 1f, yawDeg: 0f)), out _);
            Assert.AreEqual(1f, ObjectById("a").position.x, 1e-3f);

            store.TryApplyScannerSnapshot(
                Snapshot(1, Obj("a", x: 2.5f, z: 2f, yawDeg: 45f, widthM: 2f)), out _);

            Transform updated = ObjectById("a");
            Assert.AreEqual(2.5f, updated.position.x, 1e-3f);
            Assert.AreEqual(2f, updated.position.z, 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(45f, updated.rotation.eulerAngles.y), 1e-3f);
            Assert.AreEqual(1, Objects().childCount, "Updating must not add a second copy.");
        }

        [Test]
        public void RemovedObjectDisappears()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(0, Obj("a"), Obj("b", x: 2f)), out _);
            Assert.AreEqual(2, Objects().childCount);

            store.TryApplyScannerSnapshot(Snapshot(1, Obj("a")), out _);

            Assert.AreEqual(1, Objects().childCount);
            Assert.AreEqual("a", Objects().GetChild(0).GetComponent<SceneObjectBinding>().ObjectId);
        }

        [Test]
        public void NewObjectAppearsDuringAddObjects()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(0), out _);
            Assert.AreEqual(0, Objects().childCount);

            store.TryApplyScannerSnapshot(Snapshot(1, Obj("a")), out _);
            Assert.AreEqual(1, Objects().childCount);

            store.TryApplyScannerSnapshot(Snapshot(2, Obj("a"), Obj("b", x: 2f)), out _);
            Assert.AreEqual(2, Objects().childCount);
        }

        [Test]
        public void DuplicateSnapshotDoesNotDuplicateObjects()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(3, Obj("a"), Obj("b", x: 2f)), out _);
            Assert.AreEqual(2, Objects().childCount);

            store.TryApplyScannerSnapshot(Snapshot(3, Obj("a"), Obj("b", x: 2f)), out _);

            Assert.AreEqual(2, Objects().childCount);
        }

        [Test]
        public void StaleSnapshotDoesNotModifyRenderedObjects()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(5, Obj("a", x: 3f), Obj("b", x: 2f)), out _);
            Assert.AreEqual(2, Objects().childCount);

            bool applied = store.TryApplyScannerSnapshot(Snapshot(2, Obj("a", x: 0.5f)), out _);

            Assert.IsFalse(applied, "A stale revision must not be applied.");
            Assert.AreEqual(2, Objects().childCount);
            Assert.AreEqual(3f, ObjectById("a").position.x, 1e-3f);
        }

        [Test]
        public void ReconnectWithIdenticalStateCreatesNoDuplicates()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(7, Obj("a"), Obj("b", x: 2f)), out _);
            int before = Objects().childCount;

            // A reconnect re-attaches the renderer to the same store; the store's
            // Current is unchanged, so the room is rebuilt once, not twice.
            _renderer.Detach();
            _renderer.Attach(store);

            Assert.AreEqual(before, Objects().childCount);
            Assert.AreEqual(1, _hostGo.transform.childCount, "Only one RenderedRoom may exist.");
        }

        [Test]
        public void ObjectsNeverAccumulateAcrossManyRebuilds()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            for (int i = 0; i < 10; i++)
            {
                store.TryApplyScannerSnapshot(Snapshot(i, Obj("a"), Obj("b", x: 2f)), out _);
            }

            Assert.AreEqual(2, Objects().childCount);
            Assert.AreEqual(1, _hostGo.transform.childCount);
        }

        [Test]
        public void FurnitureDoesNotDisturbTheV2AndV3RoomShell()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(0, Obj("a", "bed")), out _);

            Transform root = _renderer.Root.transform;
            Assert.IsNotNull(FindChild(root, "Floor").GetComponent<MeshFilter>().sharedMesh);
            Assert.IsNotNull(FindChild(root, "Ceiling").GetComponent<MeshFilter>().sharedMesh);
            Assert.AreEqual(4, FindChild(root, "Walls").childCount);
            Assert.AreEqual(1, FindChild(root, "Objects").childCount);
        }

        [Test]
        public void GeometryBeforeFurnitureRendersTheShellAlone()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            SceneSnapshot partial = Snapshot(0);
            partial.scanPhase = "Scanning";

            store.TryApplyScannerSnapshot(partial, out _);

            Assert.AreEqual(4, FindChild(_renderer.Root.transform, "Walls").childCount);
            Assert.AreEqual(0, Objects().childCount);
        }

        [Test]
        public void DoorWindowFixtureRendersItsThreeObjectsAlongsideTheOpenings()
        {
            bool loaded = FixtureLoader.TryLoadFromFile(
                Path.Combine(RepoRoot(), "fixtures", "room-with-door-window-v1.json"),
                out SceneSnapshot snapshot,
                out string loadError);
            Assert.IsTrue(loaded, loadError);

            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            Assert.IsTrue(store.TryApplyScannerSnapshot(snapshot, out string error), error);

            Assert.AreEqual(3, Objects().childCount);

            Transform walls = FindChild(_renderer.Root.transform, "Walls");
            Assert.AreEqual(4, walls.childCount);
            Assert.Greater(
                walls.Cast<Transform>().First(w => w.name.EndsWith("_c0_c1")).childCount, 1,
                "V3's door must survive V4.");
            Assert.Greater(
                walls.Cast<Transform>().First(w => w.name.EndsWith("_c1_c2")).childCount, 1,
                "V3's window must survive V4.");
        }

        [Test]
        public void CeilingCanBeHiddenAndRestoredForDollhouseInspection()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(0, Obj("a")), out _);

            Transform ceiling = FindChild(_renderer.Root.transform, "Ceiling");
            Assert.IsTrue(ceiling.GetComponent<MeshRenderer>().enabled);
            Assert.IsTrue(_renderer.CeilingVisible);

            _renderer.SetCeilingVisible(false);

            Assert.IsFalse(_renderer.CeilingVisible);
            Assert.IsFalse(ceiling.GetComponent<MeshRenderer>().enabled);
            Assert.IsFalse(ceiling.GetComponent<MeshCollider>().enabled,
                "Section 12.2: dollhouse hides the ceiling renderer AND collider.");

            _renderer.SetCeilingVisible(true);

            Assert.IsTrue(_renderer.CeilingVisible);
            Assert.IsTrue(ceiling.GetComponent<MeshRenderer>().enabled);
        }

        [Test]
        public void HiddenCeilingStaysHiddenAcrossARebuild()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(Snapshot(0, Obj("a")), out _);
            _renderer.SetCeilingVisible(false);

            store.TryApplyScannerSnapshot(Snapshot(1, Obj("a"), Obj("b", x: 2f)), out _);

            Transform ceiling = FindChild(_renderer.Root.transform, "Ceiling");
            Assert.IsFalse(_renderer.CeilingVisible);
            Assert.IsFalse(ceiling.GetComponent<MeshRenderer>().enabled,
                "A new snapshot must not silently bring the ceiling back while in dollhouse mode.");
        }
    }
}
