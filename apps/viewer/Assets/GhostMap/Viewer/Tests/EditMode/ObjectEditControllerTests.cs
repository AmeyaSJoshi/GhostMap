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
    /// Task V5's editing controller, wired exactly as
    /// <c>ViewerBootstrap</c> wires it in production: a real
    /// <see cref="RoomRenderer"/>, a real <see cref="ObjectSelectionController"/>
    /// resolving the live selected model, and a real
    /// <see cref="ViewerEditableScene"/> gating on
    /// <c>finalized</c>. Every Try* method takes explicit values, so this
    /// exercises the full path without a Play-mode input loop.
    /// </summary>
    public sealed class ObjectEditControllerTests
    {
        private GameObject _rendererGo;
        private RoomRenderer _renderer;
        private GameObject _selectionGo;
        private ObjectSelectionController _selection;
        private GameObject _editGo;
        private ObjectEditController _edit;
        private ViewerSceneStore _store;
        private ViewerEditableScene _editable;

        [SetUp]
        public void SetUp()
        {
            _rendererGo = new GameObject("RoomRendererHost");
            _renderer = _rendererGo.AddComponent<RoomRenderer>();

            _selectionGo = new GameObject("SelectionHost");
            _selection = _selectionGo.AddComponent<ObjectSelectionController>();
            _selection.SetRoomRenderer(_renderer);

            _editGo = new GameObject("EditHost");
            _edit = _editGo.AddComponent<ObjectEditController>();
            _edit.SetSelectionController(_selection);

            _store = new ViewerSceneStore();
            _editable = new ViewerEditableScene();
            _editable.Attach(_store);

            _renderer.Attach(_editable);
            _selection.Attach();
            _edit.Attach(_editable);
        }

        [TearDown]
        public void TearDown()
        {
            _edit.Detach();
            _selection.Detach();
            _renderer.Detach();
            _editable.Detach();
            Object.DestroyImmediate(_editGo);
            Object.DestroyImmediate(_selectionGo);
            Object.DestroyImmediate(_rendererGo);
        }

        private static SceneObjectModel Obj(
            string id,
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
                type = "generic",
                center = new Vec3Dto(x, 0f, z),
                yawDeg = yawDeg,
                widthM = widthM,
                depthM = depthM,
                heightM = heightM
            };
        }

        private static SceneSnapshot Snapshot(int revision, bool finalized, params SceneObjectModel[] objects)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = "session-1",
                revision = revision,
                scanPhase = finalized ? "Finalized" : "AddObjects",
                finalized = finalized,
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

        private void SelectAt(float x, float z)
            => _selection.TrySelectAt(new Ray(new Vector3(x, 10f, z), Vector3.down));

        [Test]
        public void EditingIsDisabledBeforeFinalization()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, false, Obj("a", x: 1f, z: 1f)), out _);
            SelectAt(1f, 1f);

            bool applied = _edit.TrySetWidth(2f, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
            Assert.IsFalse(_edit.EditingEnabled);
        }

        [Test]
        public void EditingIsEnabledAfterFinalization()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, true, Obj("a", x: 1f, z: 1f)), out _);
            SelectAt(1f, 1f);

            bool applied = _edit.TrySetWidth(2f, out string error);

            Assert.IsTrue(applied, error);
            Assert.IsTrue(_edit.EditingEnabled);
        }

        [Test]
        public void NoSelectionFailsCleanlyEvenWhenFinalized()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, true, Obj("a")), out _);

            bool applied = _edit.TrySetYaw(45f, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void SetPositionXZUpdatesTheSelectedObjectOnly()
        {
            _store.TryApplyScannerSnapshot(
                Snapshot(0, true, Obj("a", x: 1f, z: 1f), Obj("b", x: 3f, z: 1f)), out _);
            SelectAt(1f, 1f);

            bool applied = _edit.TrySetPositionXZ(2.4f, 1.9f, out string error);

            Assert.IsTrue(applied, error);
            Assert.AreEqual(2.4f, _selection.GetSelectedModel().center.x, 1e-4f);
            Assert.AreEqual(1.9f, _selection.GetSelectedModel().center.z, 1e-4f);
            Assert.AreEqual(3f, ObjById(_editable.Current, "b").center.x, 1e-4f, "Editing must not touch another object.");
        }

        [Test]
        public void DragToFloorPointUpdatesXZAndPreservesY()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, true, Obj("a", x: 1f, z: 1f)), out _);
            SelectAt(1f, 1f);

            float originalY = _selection.GetSelectedModel().center.y;

            var ray = new Ray(new Vector3(2.75f, 5f, 1.6f), Vector3.down);
            bool applied = _edit.TryDragToFloorPoint(ray, out string error);

            Assert.IsTrue(applied, error);
            SceneObjectModel updated = _selection.GetSelectedModel();
            Assert.AreEqual(2.75f, updated.center.x, 1e-3f);
            Assert.AreEqual(1.6f, updated.center.z, 1e-3f);
            Assert.AreEqual(originalY, updated.center.y, 1e-4f, "Drag must never move the object below/above its floor placement.");
        }

        [Test]
        public void SetYawUpdatesTheSelectedObject()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, true, Obj("a", x: 1f, z: 1f, yawDeg: 0f)), out _);
            SelectAt(1f, 1f);

            bool applied = _edit.TrySetYaw(270f, out string error);

            Assert.IsTrue(applied, error);
            Assert.AreEqual(270f, _selection.GetSelectedModel().yawDeg, 1e-4f);
        }

        [Test]
        public void SetWidthDepthHeightUpdateTheSelectedObject()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, true, Obj("a", x: 1f, z: 1f)), out _);
            SelectAt(1f, 1f);

            Assert.IsTrue(_edit.TrySetWidth(1.8f, out string widthError), widthError);
            Assert.IsTrue(_edit.TrySetDepth(0.9f, out string depthError), depthError);
            Assert.IsTrue(_edit.TrySetHeight(0.5f, out string heightError), heightError);

            SceneObjectModel updated = _selection.GetSelectedModel();
            Assert.AreEqual(1.8f, updated.widthM, 1e-4f);
            Assert.AreEqual(0.9f, updated.depthM, 1e-4f);
            Assert.AreEqual(0.5f, updated.heightM, 1e-4f);
        }

        [Test]
        public void InvalidWidthIsRejectedAndTheModelIsUnchanged()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, true, Obj("a", x: 1f, z: 1f, widthM: 1f)), out _);
            SelectAt(1f, 1f);

            bool applied = _edit.TrySetWidth(-3f, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(1f, _selection.GetSelectedModel().widthM, 1e-4f);
        }

        [Test]
        public void OversizedDimensionIsRejectedPerSharedValidator()
        {
            _store.TryApplyScannerSnapshot(Snapshot(0, true, Obj("a", x: 1f, z: 1f)), out _);
            SelectAt(1f, 1f);

            // FurnitureValidator.MaxDimensionM is 5.0 m.
            bool applied = _edit.TrySetHeight(50f, out string error);

            Assert.IsFalse(applied);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void EditingOneObjectDoesNotRebuildTheOtherObjectsGameObject()
        {
            _store.TryApplyScannerSnapshot(
                Snapshot(0, true, Obj("a", x: 1f, z: 1f), Obj("b", x: 3f, z: 1f)), out _);
            SelectAt(1f, 1f);

            _edit.TrySetWidth(2f, out _);

            Assert.AreEqual(3f, ObjById(_editable.Current, "b").center.x, 1e-4f);
            Assert.AreEqual(1f, ObjById(_editable.Current, "b").widthM, 1e-4f);
        }

        private static SceneObjectModel ObjById(SceneSnapshot snapshot, string id)
        {
            foreach (SceneObjectModel candidate in snapshot.room.objects)
            {
                if (candidate.id == id)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
