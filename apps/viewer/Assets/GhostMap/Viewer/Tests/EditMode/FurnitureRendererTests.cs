using System.Collections.Generic;
using System.IO;
using System.Linq;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V4's <see cref="FurnitureFactory.Create"/> and
    /// <see cref="FurnitureRenderer"/>: one logical GameObject root per
    /// <see cref="SceneObjectModel"/>, placed at the model's centre with its
    /// yaw, carrying one bounding-box selection collider and the metadata
    /// component V5's selection will read.
    /// </summary>
    public sealed class FurnitureRendererTests
    {
        private const float Tolerance = 1e-4f;

        private GameObject _parentGo;
        private FurnitureFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _parentGo = new GameObject("ObjectsRoot");
            _factory = new FurnitureFactory();
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            Object.DestroyImmediate(_parentGo);
        }

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static SceneObjectModel Model(
            string type = "generic",
            float x = 0f,
            float z = 0f,
            float yawDeg = 0f,
            float widthM = 1.2f,
            float depthM = 0.8f,
            float heightM = 0.9f,
            string id = "obj-1")
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

        private static RoomModel Room(params SceneObjectModel[] objects)
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

        [Test]
        public void CreateProducesOneRootPerModel()
        {
            GameObject root = _factory.Create(Model(), _parentGo.transform);

            Assert.IsNotNull(root);
            Assert.AreSame(_parentGo.transform, root.transform.parent);
            Assert.AreEqual(1, _parentGo.transform.childCount);
        }

        [Test]
        public void RootIsNamedWithTypeAndId()
        {
            GameObject root = _factory.Create(Model(type: "bed", id: "bed-7"), _parentGo.transform);

            StringAssert.Contains("bed", root.name);
            StringAssert.Contains("bed-7", root.name);
        }

        [Test]
        public void RootSitsAtTheModelCentre()
        {
            GameObject root = _factory.Create(Model(x: 2.4f, z: 1.1f), _parentGo.transform);

            Assert.AreEqual(2.4f, root.transform.position.x, Tolerance);
            Assert.AreEqual(0f, root.transform.position.y, Tolerance, "Objects sit on the floor plane.");
            Assert.AreEqual(1.1f, root.transform.position.z, Tolerance);
        }

        [Test]
        public void RootCarriesTheModelYawAsRotationAboutY()
        {
            GameObject root = _factory.Create(Model(yawDeg: 90f), _parentGo.transform);

            Vector3 euler = root.transform.rotation.eulerAngles;

            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, euler.x), 1e-3f, "No pitch in the MVP.");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, euler.z), 1e-3f, "No roll in the MVP.");
            Assert.AreEqual(0f, Mathf.DeltaAngle(90f, euler.y), 1e-3f);
        }

        [Test]
        public void YawRotatesTheRenderedFootprint()
        {
            // A 2 x 0.5 object at yaw 0 is wide in X; at yaw 90 it is wide in Z.
            GameObject unrotated = _factory.Create(
                Model(widthM: 2f, depthM: 0.5f, id: "a"), _parentGo.transform);
            GameObject rotated = _factory.Create(
                Model(widthM: 2f, depthM: 0.5f, yawDeg: 90f, id: "b"), _parentGo.transform);

            Bounds unrotatedBounds = WorldBounds(unrotated);
            Bounds rotatedBounds = WorldBounds(rotated);

            Assert.AreEqual(2f, unrotatedBounds.size.x, 1e-3f);
            Assert.AreEqual(0.5f, unrotatedBounds.size.z, 1e-3f);
            Assert.AreEqual(0.5f, rotatedBounds.size.x, 1e-3f);
            Assert.AreEqual(2f, rotatedBounds.size.z, 1e-3f);
        }

        private static Bounds WorldBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            Assert.Greater(renderers.Length, 0, "Object rendered no geometry.");

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        [Test]
        public void RenderedObjectMatchesTheDeclaredDimensions()
        {
            GameObject root = _factory.Create(
                Model(widthM: 1.52f, depthM: 2.03f, heightM: 0.6f, type: "bed"), _parentGo.transform);

            Bounds bounds = WorldBounds(root);

            Assert.AreEqual(1.52f, bounds.size.x, 1e-3f);
            Assert.AreEqual(2.03f, bounds.size.z, 1e-3f);
            Assert.AreEqual(0.6f, bounds.size.y, 1e-3f);
        }

        [Test]
        public void RenderedObjectRestsOnTheFloorPlane()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                GameObject root = _factory.Create(Model(type: type, id: type), _parentGo.transform);

                Assert.AreEqual(0f, WorldBounds(root).min.y, 1e-3f, $"'{type}' does not rest on the floor.");
            }
        }

        [Test]
        public void RootHasExactlyOneColliderCoveringItsBoundingBox()
        {
            GameObject root = _factory.Create(
                Model(widthM: 1.4f, depthM: 0.7f, heightM: 0.75f, type: "desk"), _parentGo.transform);

            Collider[] colliders = root.GetComponentsInChildren<Collider>();

            Assert.AreEqual(1, colliders.Length,
                "Section 12.4: every furniture root has ONE collider for its full bounding box.");

            var box = colliders[0] as BoxCollider;
            Assert.IsNotNull(box);
            Assert.AreSame(root, box.gameObject, "The collider belongs on the root, not a part.");
            Assert.AreEqual(new Vector3(1.4f, 0.75f, 0.7f), box.size);
            Assert.AreEqual(new Vector3(0f, 0.375f, 0f), box.center);
        }

        [Test]
        public void RootCarriesTheObjectMetadataComponent()
        {
            SceneObjectModel model = Model(type: "couch", id: "couch-3");

            GameObject root = _factory.Create(model, _parentGo.transform);

            var binding = root.GetComponent<SceneObjectBinding>();
            Assert.IsNotNull(binding, "V5's selection needs the object's identity on the root.");
            Assert.AreEqual("couch-3", binding.ObjectId);
            Assert.AreEqual("couch", binding.ObjectType);
            Assert.AreSame(model, binding.Model);
        }

        [Test]
        public void EverySupportedTypeCreatesVisibleGeometry()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                GameObject root = _factory.Create(Model(type: type, id: type), _parentGo.transform);

                Assert.IsNotNull(root, $"'{type}' created nothing.");
                Assert.Greater(
                    root.GetComponentsInChildren<MeshRenderer>().Length, 0,
                    $"'{type}' has no renderers.");
            }
        }

        [Test]
        public void UnsupportedTypeCreatesNothing()
        {
            GameObject root = _factory.Create(Model(type: "spaceship"), _parentGo.transform);

            Assert.IsNull(root);
            Assert.AreEqual(0, _parentGo.transform.childCount);
        }

        [Test]
        public void NullModelCreatesNothing()
        {
            Assert.IsNull(_factory.Create(null, _parentGo.transform));
            Assert.AreEqual(0, _parentGo.transform.childCount);
        }

        [Test]
        public void PopulateRendersEveryObjectIndependently()
        {
            RoomModel room = Room(
                Model(type: "bed", x: 1f, z: 2.2f, id: "bed-1"),
                Model(type: "desk", x: 3.2f, z: 0.5f, id: "desk-1"),
                Model(type: "chair", x: 3.2f, z: 1.3f, id: "chair-1"));

            int count = FurnitureRenderer.Populate(room, _parentGo.transform, _factory, null);

            Assert.AreEqual(3, count);
            Assert.AreEqual(3, _parentGo.transform.childCount);

            string[] ids = _parentGo.transform.Cast<Transform>()
                .Select(t => t.GetComponent<SceneObjectBinding>().ObjectId)
                .OrderBy(id => id)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "bed-1", "chair-1", "desk-1" }, ids);
        }

        [Test]
        public void PopulateWithZeroObjectsRendersNothingAndDoesNotThrow()
        {
            Assert.AreEqual(0, FurnitureRenderer.Populate(Room(), _parentGo.transform, _factory, null));
            Assert.AreEqual(0, _parentGo.transform.childCount);
        }

        [Test]
        public void PopulateWithNullRoomOrNullObjectArrayDoesNotThrow()
        {
            RoomModel room = Room();
            room.objects = null;

            Assert.DoesNotThrow(() => FurnitureRenderer.Populate(null, _parentGo.transform, _factory, null));
            Assert.DoesNotThrow(() => FurnitureRenderer.Populate(room, _parentGo.transform, _factory, null));
            Assert.AreEqual(0, _parentGo.transform.childCount);
        }

        [Test]
        public void PopulateSkipsInvalidObjectsAndDiagnosesThem()
        {
            var diagnostics = new List<string>();
            RoomModel room = Room(
                Model(type: "bed", id: "good"),
                Model(type: "spaceship", id: "bad"),
                null);

            int count = FurnitureRenderer.Populate(room, _parentGo.transform, _factory, diagnostics);

            Assert.AreEqual(1, count, "Only the valid object may render.");
            Assert.AreEqual(1, _parentGo.transform.childCount);
            Assert.AreEqual(2, diagnostics.Count);
        }

        [Test]
        public void DoorWindowFixtureObjectsRenderDeterministically()
        {
            bool loaded = FixtureLoader.TryLoadFromFile(
                Path.Combine(RepoRoot(), "fixtures", "room-with-door-window-v1.json"),
                out SceneSnapshot snapshot,
                out string error);
            Assert.IsTrue(loaded, error);

            int count = FurnitureRenderer.Populate(snapshot.room, _parentGo.transform, _factory, null);

            Assert.AreEqual(3, count, "The fixture carries a bed, a desk and a chair.");

            Transform bed = _parentGo.transform.Cast<Transform>()
                .First(t => t.GetComponent<SceneObjectBinding>().ObjectId == "bed-1");

            // bed-1: centre (1.0, 0, 2.2), yaw 90, 1.52 x 2.03 x 0.6.
            Assert.AreEqual(1.0f, bed.position.x, 1e-3f);
            Assert.AreEqual(2.2f, bed.position.z, 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(90f, bed.rotation.eulerAngles.y), 1e-3f);

            Bounds bounds = WorldBounds(bed.gameObject);
            Assert.AreEqual(2.03f, bounds.size.x, 1e-3f, "Yaw 90 swaps the footprint axes.");
            Assert.AreEqual(1.52f, bounds.size.z, 1e-3f);
            Assert.AreEqual(0.6f, bounds.size.y, 1e-3f);
        }

        [Test]
        public void FixtureObjectsSitInsideTheRoomFootprint()
        {
            bool loaded = FixtureLoader.TryLoadFromFile(
                Path.Combine(RepoRoot(), "fixtures", "room-with-door-window-v1.json"),
                out SceneSnapshot snapshot,
                out string error);
            Assert.IsTrue(loaded, error);

            FurnitureRenderer.Populate(snapshot.room, _parentGo.transform, _factory, null);

            // The fixture's bed (centre x = 1.0, yaw 90, depth 2.03) overhangs the
            // x = 0 wall centreline by ~1.5 cm. That is the fixture's own data,
            // which this workstream does not own and must not "fix"; the point of
            // this test is that nothing is wildly mislocated, so the tolerance is
            // one wall thickness rather than zero.
            const float slack = 0.10f;

            foreach (Transform child in _parentGo.transform)
            {
                Bounds bounds = WorldBounds(child.gameObject);

                Assert.GreaterOrEqual(bounds.min.x, -slack, $"{child.name} left the room in -X.");
                Assert.LessOrEqual(bounds.max.x, 4f + slack, $"{child.name} left the room in +X.");
                Assert.GreaterOrEqual(bounds.min.z, -slack, $"{child.name} left the room in -Z.");
                Assert.LessOrEqual(bounds.max.z, 3f + slack, $"{child.name} left the room in +Z.");
                Assert.LessOrEqual(bounds.max.y, snapshot.room.heightM + 0.01f, $"{child.name} clips the ceiling.");
            }
        }
    }
}
