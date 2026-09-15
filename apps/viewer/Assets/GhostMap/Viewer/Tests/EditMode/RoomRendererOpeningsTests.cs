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
    /// Task V3's <see cref="RoomRenderer"/> behavior: each wall is now a
    /// container of solid segments rather than one cuboid, and the live-update
    /// guarantees from V2 (no accumulation, no duplication, stale snapshots
    /// ignored, a rejected snapshot leaves the last good room on screen) must
    /// still hold now that a wall can be many GameObjects.
    /// </summary>
    public sealed class RoomRendererOpeningsTests
    {
        private GameObject _hostGo;
        private RoomRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _hostGo = new GameObject("RoomRendererOpeningsTestHost");
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

        private static Transform FindChild(Transform parent, string name)
            => parent.Cast<Transform>().FirstOrDefault(t => t.name == name);

        private Transform Walls()
            => FindChild(_renderer.Root.transform, "Walls");

        private Transform Wall(string startCornerId, string endCornerId)
            => Walls().Cast<Transform>().First(t => t.name.EndsWith($"_{startCornerId}_{endCornerId}"));

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

        private static OpeningModel Door(float offsetM = 1.2f, string id = "door-1")
        {
            return new OpeningModel
            {
                id = id,
                type = "door",
                wallStartCornerId = "c0",
                wallEndCornerId = "c1",
                offsetM = offsetM,
                widthM = 0.9f,
                sillHeightM = 0f,
                heightM = 2.05f
            };
        }

        private static SceneSnapshot BuildSnapshot(
            string sessionId,
            int revision,
            OpeningModel[] openings)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = sessionId,
                revision = revision,
                scanPhase = "AddOpenings",
                finalized = false,
                closureErrorM = 0.05f,
                room = new RoomModel
                {
                    id = "room-1",
                    name = "Bedroom",
                    heightM = 2.5f,
                    corners = RectangularCorners(),
                    openings = openings ?? new OpeningModel[0],
                    objects = new SceneObjectModel[0]
                }
            };
        }

        [Test]
        public void WallWithoutOpeningsIsOneSegment()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            Assert.IsTrue(store.TryApplyScannerSnapshot(BuildSnapshot("s", 0, null), out string error), error);

            Assert.AreEqual(4, Walls().childCount);

            foreach (Transform wall in Walls())
            {
                Assert.AreEqual(1, wall.childCount, $"{wall.name} should be one solid segment.");
            }
        }

        [Test]
        public void DoorSegmentsOnlyItsOwnWall()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            Assert.IsTrue(
                store.TryApplyScannerSnapshot(BuildSnapshot("s", 0, new[] { Door() }), out string error), error);

            Assert.Greater(Wall("c0", "c1").childCount, 1);
            Assert.AreEqual(1, Wall("c1", "c2").childCount);
            Assert.AreEqual(1, Wall("c2", "c3").childCount);
            Assert.AreEqual(1, Wall("c3", "c0").childCount);
        }

        [Test]
        public void EverySegmentHasAMeshAndACollider()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 0, new[] { Door() }), out _);

            foreach (Transform wall in Walls())
            {
                Assert.Greater(wall.childCount, 0);

                foreach (Transform segment in wall)
                {
                    var filter = segment.GetComponent<MeshFilter>();
                    var collider = segment.GetComponent<BoxCollider>();

                    Assert.IsTrue(filter != null && filter.sharedMesh != null, $"{segment.name} has no mesh.");
                    Assert.IsTrue(collider != null, $"{segment.name} has no collider.");
                }
            }
        }

        [Test]
        public void NoSegmentSitsInsideTheDoorway()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 0, new[] { Door() }), out _);

            // Wall c0->c1 runs along +X at z = 0; the door spans x 1.2-2.1,
            // y 0-2.05.
            foreach (Transform segment in Wall("c0", "c1"))
            {
                Vector3 p = segment.position;
                bool insideDoor = p.x > 1.2f && p.x < 2.1f && p.y < 2.05f;
                Assert.IsFalse(insideDoor, $"{segment.name} at {p} is inside the doorway.");
            }
        }

        [Test]
        public void AddingAnOpeningReplacesThePriorWallSegmentation()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 0, null), out _);
            Assert.AreEqual(1, Wall("c0", "c1").childCount);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 1, new[] { Door() }), out string error);

            Assert.AreEqual(4, Walls().childCount, "Walls must not accumulate.");
            Assert.Greater(Wall("c0", "c1").childCount, 1, error);
        }

        [Test]
        public void RemovingAnOpeningRestoresTheSolidWall()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 0, new[] { Door() }), out _);
            Assert.Greater(Wall("c0", "c1").childCount, 1);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 1, null), out _);

            Assert.AreEqual(4, Walls().childCount);
            Assert.AreEqual(1, Wall("c0", "c1").childCount, "Old segments must not survive the rebuild.");
        }

        [Test]
        public void DuplicateSnapshotDoesNotDuplicateSegments()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            SceneSnapshot snapshot = BuildSnapshot("s", 3, new[] { Door() });
            store.TryApplyScannerSnapshot(snapshot, out _);

            int before = Wall("c0", "c1").childCount;
            int totalBefore = Walls().Cast<Transform>().Sum(w => w.childCount);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 3, new[] { Door() }), out _);

            Assert.AreEqual(4, Walls().childCount);
            Assert.AreEqual(before, Wall("c0", "c1").childCount);
            Assert.AreEqual(totalBefore, Walls().Cast<Transform>().Sum(w => w.childCount));
        }

        [Test]
        public void StaleSnapshotDoesNotAlterCurrentGeometry()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 5, new[] { Door() }), out _);
            int segmented = Wall("c0", "c1").childCount;
            Assert.Greater(segmented, 1);

            bool applied = store.TryApplyScannerSnapshot(BuildSnapshot("s", 2, null), out _);

            Assert.IsFalse(applied, "A stale revision must not be applied.");
            Assert.AreEqual(segmented, Wall("c0", "c1").childCount,
                "A stale snapshot must not overwrite newer geometry.");
        }

        [Test]
        public void RejectedOpeningLeavesThePreviousRoomOnScreen()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 1, new[] { Door() }), out _);
            GameObject rootBefore = _renderer.Root;
            int segmented = Wall("c0", "c1").childCount;

            // offset 3.8 + width 0.9 overruns the 4 m wall: OpeningValidator
            // rejects the whole snapshot before RoomRenderer ever sees it.
            OpeningModel bad = Door(offsetM: 3.8f);
            bool applied = store.TryApplyScannerSnapshot(BuildSnapshot("s", 2, new[] { bad }), out string error);

            Assert.IsFalse(applied, "An invalid opening must not be accepted.");
            Assert.IsNotNull(error);
            Assert.AreSame(rootBefore, _renderer.Root, "The last valid room must stay rendered.");
            Assert.AreEqual(segmented, Wall("c0", "c1").childCount);
        }

        [Test]
        public void PartialScanWithoutHeightRendersNoWalls()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            SceneSnapshot snapshot = BuildSnapshot("s", 0, null);
            snapshot.room.heightM = 0f;
            snapshot.room.corners = RectangularCorners().Take(3).ToArray();

            store.TryApplyScannerSnapshot(snapshot, out _);

            Assert.IsNotNull(_renderer.Root);
            Assert.AreEqual(0, Walls().childCount);
        }

        [Test]
        public void DoorWindowFixtureRendersBothOpenings()
        {
            bool loaded = FixtureLoader.TryLoadFromFile(
                FixturePath("room-with-door-window-v1.json"), out SceneSnapshot snapshot, out string loadError);
            Assert.IsTrue(loaded, loadError);

            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            Assert.IsTrue(store.TryApplyScannerSnapshot(snapshot, out string error), error);

            Assert.AreEqual(4, Walls().childCount);
            Assert.Greater(Wall("c0", "c1").childCount, 1, "The door wall must be segmented.");
            Assert.Greater(Wall("c1", "c2").childCount, 1, "The window wall must be segmented.");
            Assert.AreEqual(1, Wall("c2", "c3").childCount);
            Assert.AreEqual(1, Wall("c3", "c0").childCount);

            // The window sill segment must sit below the 0.9 m sill, and a
            // header segment above the 2.0 m top.
            var heights = Wall("c1", "c2").Cast<Transform>().Select(t => t.position.y).ToArray();
            Assert.IsTrue(heights.Any(y => y < 0.9f), "Expected a segment beneath the window sill.");
            Assert.IsTrue(heights.Any(y => y > 2.0f), "Expected a segment above the window head.");
        }

        [Test]
        public void SegmentedWallKeepsItsWallName()
        {
            var store = new ViewerSceneStore();
            _renderer.Attach(store);

            store.TryApplyScannerSnapshot(BuildSnapshot("s", 0, new[] { Door() }), out _);

            Assert.AreEqual("Wall_0_c0_c1", Walls().GetChild(0).name);
            Assert.IsTrue(Walls().GetChild(0).GetChild(0).name.StartsWith("Segment_"));
        }
    }
}
