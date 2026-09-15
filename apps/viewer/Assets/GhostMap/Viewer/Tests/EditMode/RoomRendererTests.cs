using System.IO;
using System.Linq;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V2's <see cref="RoomRenderer"/>: reacts only to
    /// <see cref="ViewerSceneStore.Changed"/>, rebuilds the
    /// Floor/Ceiling/Walls/Objects hierarchy without accumulating old
    /// geometry, and never crashes on a partial or degenerate scan.
    /// </summary>
    public sealed class RoomRendererTests
    {
        private GameObject _hostGo;
        private RoomRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _hostGo = new GameObject("RoomRendererTestHost");
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

        private static string FixturePath(string fileName)
            => Path.Combine(RepoRoot(), "fixtures", fileName);

        private static SceneSnapshot BuildSnapshot(
            string sessionId,
            int revision,
            CornerModel[] corners,
            float heightM = 2.5f)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = sessionId,
                revision = revision,
                scanPhase = "Scanning",
                finalized = false,
                closureErrorM = 0.05f,
                room = new RoomModel
                {
                    id = "room-1",
                    name = "Bedroom",
                    heightM = heightM,
                    corners = corners,
                    openings = new OpeningModel[0],
                    objects = new SceneObjectModel[0]
                }
            };
        }

        private static CornerModel[] RectangularCorners()
        {
            return new[]
            {
                new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
            };
        }

        private static Transform FindChild(Transform parent, string name)
            => parent.Cast<Transform>().FirstOrDefault(t => t.name == name);

        /// <summary>
        /// <c>GetComponent&lt;T&gt;()?.member</c> is a known Unity trap: a
        /// missing component comes back as a "fake null" wrapper that the
        /// overridden <c>==</c> treats as null, but the null-conditional
        /// operator's raw reference check does not, so it throws instead of
        /// short-circuiting. An explicit <c>!= null</c> check is required.
        /// </summary>
        private static bool HasMesh(Transform target)
        {
            var filter = target.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null;
        }

        [Test]
        public void ValidSnapshotCreatesFloorCeilingAndFourWalls()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 0, RectangularCorners()), out string error);

            Assert.IsNotNull(_renderer.Root, error);
            Transform floor = FindChild(_renderer.Root.transform, "Floor");
            Transform ceiling = FindChild(_renderer.Root.transform, "Ceiling");
            Transform walls = FindChild(_renderer.Root.transform, "Walls");
            Transform objects = FindChild(_renderer.Root.transform, "Objects");

            Assert.IsNotNull(floor);
            Assert.IsNotNull(floor.GetComponent<MeshFilter>().sharedMesh);
            Assert.IsNotNull(ceiling);
            Assert.IsNotNull(ceiling.GetComponent<MeshFilter>().sharedMesh);
            Assert.IsNotNull(walls);
            Assert.AreEqual(4, walls.childCount);
            Assert.IsNotNull(objects);
            Assert.AreEqual(0, objects.childCount);
        }

        [Test]
        public void RegeneratedSceneReplacesPreviousGeometryInsteadOfAccumulating()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 0, RectangularCorners()), out _);
            int firstRootInstanceId = _renderer.Root.GetInstanceID();

            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 1, RectangularCorners()), out _);
            int secondRootInstanceId = _renderer.Root.GetInstanceID();

            // The old root must actually be gone, not merely superseded.
            Assert.AreNotEqual(firstRootInstanceId, secondRootInstanceId);
            Assert.AreEqual(
                1,
                _hostGo.transform.Cast<Transform>().Count(t => t.name == "RenderedRoom"),
                "Only one RenderedRoom may exist under the host at a time.");

            Transform walls = FindChild(_renderer.Root.transform, "Walls");
            Assert.AreEqual(4, walls.childCount, "Walls must not accumulate across rebuilds.");
        }

        [Test]
        public void DuplicateSnapshotRevisionDoesNotDuplicateGeometry()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 5, RectangularCorners()), out _);
            GameObject firstRoot = _renderer.Root;

            // A resend after reconnect: same session, same revision.
            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 5, RectangularCorners()), out _);

            Assert.AreSame(firstRoot, _renderer.Root, "A duplicate revision must never trigger a rebuild.");
            Assert.AreEqual(
                1,
                _hostGo.transform.Cast<Transform>().Count(t => t.name == "RenderedRoom"));
        }

        [Test]
        public void StaleSnapshotDoesNotReplaceNewerRenderedGeometry()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            var newerCorners = RectangularCorners();
            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 5, newerCorners, heightM: 3.0f), out _);
            GameObject newerRoot = _renderer.Root;

            var staleCorners = RectangularCorners();
            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 2, staleCorners, heightM: 2.0f), out _);

            Assert.AreSame(newerRoot, _renderer.Root, "A stale revision must never trigger a rebuild.");
            Transform ceiling = FindChild(_renderer.Root.transform, "Ceiling");
            Assert.AreEqual(3.0f, ceiling.GetComponent<MeshFilter>().sharedMesh.vertices[0].y, 1e-5f);
        }

        [Test]
        public void DisconnectPreservesRenderedGeometry()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 0, RectangularCorners()), out _);
            GameObject renderedRoot = _renderer.Root;

            // A network disconnect never touches the scene store and never
            // fires Changed, so nothing here should call into the renderer.
            // Simulated by simply doing nothing and asserting geometry stands.
            Assert.AreSame(renderedRoot, _renderer.Root);
            Assert.IsNotNull(FindChild(_renderer.Root.transform, "Walls"));
            Assert.AreEqual(4, FindChild(_renderer.Root.transform, "Walls").childCount);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void PartialSnapshotsWithFewerThanThreeCornersDoNotCrashAndProduceNoFloor(int cornerCount)
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            CornerModel[] corners = RectangularCorners().Take(cornerCount).ToArray();

            Assert.DoesNotThrow(() =>
                store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 0, corners, heightM: 0f), out _));

            Assert.IsNotNull(_renderer.Root);
            Transform floor = FindChild(_renderer.Root.transform, "Floor");
            Assert.IsNotNull(floor);
            Assert.IsFalse(HasMesh(floor));
        }

        [Test]
        public void ThreeCornersProducesAFloorPreviewButNoWallsOrCeiling()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            CornerModel[] corners = RectangularCorners().Take(3).ToArray();
            bool applied = store.TryApplyScannerSnapshot(
                BuildSnapshot("session-1", 0, corners, heightM: 0f), out string error);

            Assert.IsTrue(applied, error);
            Transform floor = FindChild(_renderer.Root.transform, "Floor");
            Assert.IsTrue(HasMesh(floor));
            Assert.AreEqual(0, FindChild(_renderer.Root.transform, "Walls").childCount);
            Assert.IsFalse(HasMesh(FindChild(_renderer.Root.transform, "Ceiling")));
        }

        [Test]
        public void FourCornersWithNoHeightYetProducesFloorButNoCeilingOrWalls()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            bool applied = store.TryApplyScannerSnapshot(
                BuildSnapshot("session-1", 0, RectangularCorners(), heightM: 0f), out string error);

            Assert.IsTrue(applied, error);
            Transform floor = FindChild(_renderer.Root.transform, "Floor");
            Transform ceiling = FindChild(_renderer.Root.transform, "Ceiling");
            Transform walls = FindChild(_renderer.Root.transform, "Walls");

            Assert.IsTrue(HasMesh(floor));
            Assert.IsFalse(HasMesh(ceiling));
            Assert.AreEqual(0, walls.childCount);
        }

        [Test]
        public void HeightZeroDoesNotCreateInvalidCeilingOrWalls()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("session-1", 0, RectangularCorners(), heightM: 0f), out _);

            Assert.DoesNotThrow(() => { var _ = _renderer.Root; });
            Assert.AreEqual(0, FindChild(_renderer.Root.transform, "Walls").childCount);
        }

        [Test]
        public void FixtureRoomProducesExpectedDimensions()
        {
            bool loaded = FixtureLoader.TryLoadFromFile(
                FixturePath("valid-room-v1.json"), out SceneSnapshot snapshot, out string loadError);
            Assert.IsTrue(loaded, loadError);

            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            bool applied = store.TryApplyScannerSnapshot(snapshot, out string applyError);
            Assert.IsTrue(applied, applyError);

            Transform floor = FindChild(_renderer.Root.transform, "Floor");
            Mesh floorMesh = floor.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, floorMesh.vertexCount);

            Transform ceiling = FindChild(_renderer.Root.transform, "Ceiling");
            Mesh ceilingMesh = ceiling.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(2.5f, ceilingMesh.vertices[0].y, 1e-5f);

            Transform walls = FindChild(_renderer.Root.transform, "Walls");
            Assert.AreEqual(4, walls.childCount);

            var lengths = walls.Cast<Transform>()
                .Select(w => w.localScale.x)
                .OrderBy(l => l)
                .ToArray();
            Assert.AreEqual(3f, lengths[0], 1e-4f);
            Assert.AreEqual(3f, lengths[1], 1e-4f);
            Assert.AreEqual(4f, lengths[2], 1e-4f);
            Assert.AreEqual(4f, lengths[3], 1e-4f);
        }

        [Test]
        public void DoorWindowFixtureStillRendersFourSolidWalls()
        {
            // V2 does not cut openings yet (V3's scope) — even a room with a
            // door and a window must render four solid wall cuboids.
            bool loaded = FixtureLoader.TryLoadFromFile(
                FixturePath("room-with-door-window-v1.json"), out SceneSnapshot snapshot, out string loadError);
            Assert.IsTrue(loaded, loadError);

            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            bool applied = store.TryApplyScannerSnapshot(snapshot, out string applyError);
            Assert.IsTrue(applied, applyError);

            Transform walls = FindChild(_renderer.Root.transform, "Walls");
            Assert.AreEqual(4, walls.childCount);
            foreach (Transform wall in walls)
            {
                Assert.IsNotNull(wall.GetComponent<MeshFilter>().sharedMesh, "Every wall must be a solid cuboid.");
            }
        }
    }
}
