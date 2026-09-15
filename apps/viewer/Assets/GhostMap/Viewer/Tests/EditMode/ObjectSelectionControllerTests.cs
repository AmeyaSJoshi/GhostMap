using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Rendering;
using GhostMap.Viewer.Scene;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V5's object selection: a real <see cref="RoomRenderer"/> renders
    /// a real room with real colliders, and selection is driven by real
    /// <see cref="Physics.Raycast"/> calls against it — never a mock, exactly
    /// like production. Confirms deterministic identity via
    /// <see cref="SceneObjectBinding"/>, that floor/walls are never
    /// accidentally selectable, single-selection semantics, and safe
    /// clearing when the authoritative scene drops the selected object.
    /// </summary>
    public sealed class ObjectSelectionControllerTests
    {
        private GameObject _rendererGo;
        private RoomRenderer _renderer;
        private GameObject _selectionGo;
        private ObjectSelectionController _selection;
        private ViewerSceneStore _store;

        [SetUp]
        public void SetUp()
        {
            _rendererGo = new GameObject("RoomRendererHost");
            _renderer = _rendererGo.AddComponent<RoomRenderer>();

            _selectionGo = new GameObject("SelectionHost");
            _selection = _selectionGo.AddComponent<ObjectSelectionController>();
            _selection.SetRoomRenderer(_renderer);

            _store = new ViewerSceneStore();
            _renderer.Attach(_store);
            _selection.Attach();
        }

        [TearDown]
        public void TearDown()
        {
            _selection.Detach();
            _renderer.Detach();
            Object.DestroyImmediate(_selectionGo);
            Object.DestroyImmediate(_rendererGo);
        }

        private static SceneObjectModel Obj(
            string id,
            string type = "generic",
            float x = 1f,
            float z = 1f,
            float widthM = 1f,
            float depthM = 1f,
            float heightM = 1f)
        {
            return new SceneObjectModel
            {
                id = id,
                type = type,
                center = new Vec3Dto(x, 0f, z),
                yawDeg = 0f,
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

        private static Ray DownRayAt(float x, float z) => new Ray(new Vector3(x, 10f, z), Vector3.down);

        [Test]
        public void ClickingAnObjectSelectsItsModelId()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, Obj("bed-1", "bed", x: 1f, z: 1f, heightM: 0.6f)), out _);

            bool selected = _selection.TrySelectAt(DownRayAt(1f, 1f));

            Assert.IsTrue(selected);
            Assert.AreEqual("bed-1", _selection.SelectedObjectId);
        }

        [Test]
        public void SelectionUsesSceneObjectBindingNeverGameObjectNameParsing()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, Obj("weird id with spaces", x: 1f, z: 1f)), out _);

            _selection.TrySelectAt(DownRayAt(1f, 1f));

            Assert.AreEqual("weird id with spaces", _selection.SelectedObjectId);
        }

        [Test]
        public void OnlyOneObjectIsSelectedAtATime()
        {
            _store.TryApplyScannerSnapshot(
                Snapshot(0, Obj("a", x: 1f, z: 1f), Obj("b", x: 3f, z: 1f)), out _);

            _selection.TrySelectAt(DownRayAt(1f, 1f));
            Assert.AreEqual("a", _selection.SelectedObjectId);

            _selection.TrySelectAt(DownRayAt(3f, 1f));
            Assert.AreEqual("b", _selection.SelectedObjectId, "Selecting a new object must replace the old one.");
        }

        [Test]
        public void ClickingEmptySpaceClearsSelection()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, Obj("a", x: 1f, z: 1f)), out _);
            _selection.TrySelectAt(DownRayAt(1f, 1f));
            Assert.IsTrue(_selection.HasSelection);

            bool selected = _selection.TrySelectAt(DownRayAt(100f, 100f));

            Assert.IsFalse(selected);
            Assert.IsFalse(_selection.HasSelection);
            Assert.IsNull(_selection.SelectedObjectId);
        }

        [Test]
        public void ClickingTheFloorDoesNotSelectFurniture()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, Obj("a", x: 1f, z: 1f)), out _);

            // Away from the furniture footprint but still inside the room, so
            // the ray hits the floor's MeshCollider, not the object.
            bool selected = _selection.TrySelectAt(DownRayAt(3.5f, 2.5f));

            Assert.IsFalse(selected);
            Assert.IsFalse(_selection.HasSelection, "Floor must never become a furniture selection.");
        }

        [Test]
        public void RemovedSelectedObjectClearsSelectionSafely()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, Obj("a", x: 1f, z: 1f), Obj("b", x: 3f, z: 1f)), out _);
            _selection.TrySelectAt(DownRayAt(1f, 1f));
            Assert.AreEqual("a", _selection.SelectedObjectId);

            int clearedCount = 0;
            _selection.SelectionChanged += id =>
            {
                if (id == null)
                {
                    clearedCount++;
                }
            };

            // "a" is dropped by a new authoritative (pre-finalization) snapshot.
            _store.TryApplyScannerSnapshot(Snapshot(1, Obj("b", x: 3f, z: 1f)), out _);

            Assert.IsFalse(_selection.HasSelection);
            Assert.AreEqual(1, clearedCount);
        }

        [Test]
        public void SelectionSurvivesARebuildWhenTheObjectStillExists()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, Obj("a", x: 1f, z: 1f)), out _);
            _selection.TrySelectAt(DownRayAt(1f, 1f));

            // A newer snapshot moves "a" but does not remove it.
            _store.TryApplyScannerSnapshot(Snapshot(1, Obj("a", x: 1.5f, z: 1f)), out _);

            Assert.AreEqual("a", _selection.SelectedObjectId);
            Assert.AreEqual(1.5f, _selection.GetSelectedModel().center.x, 1e-4f);
        }

        [Test]
        public void GetSelectedModelReturnsNullWhenNothingIsSelected()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, Obj("a")), out _);

            Assert.IsNull(_selection.GetSelectedModel());
        }

        [Test]
        public void IsPointerOverSelectedDistinguishesTheSelectedObjectFromOthers()
        {
            _store.TryApplyScannerSnapshot(
                Snapshot(0, Obj("a", x: 1f, z: 1f), Obj("b", x: 3f, z: 1f)), out _);
            _selection.TrySelectAt(DownRayAt(1f, 1f));

            Assert.IsTrue(_selection.IsPointerOverSelected(DownRayAt(1f, 1f)));
            Assert.IsFalse(_selection.IsPointerOverSelected(DownRayAt(3f, 1f)), "Must not report true for a different object.");
            Assert.IsFalse(_selection.IsPointerOverSelected(DownRayAt(100f, 100f)));
        }

        [Test]
        public void ClearSelectionIsIdempotentAndSafeWithNothingSelected()
        {
            Assert.DoesNotThrow(() => _selection.ClearSelection());
            Assert.IsFalse(_selection.HasSelection);
        }
    }
}
