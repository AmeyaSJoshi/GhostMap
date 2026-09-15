using System.Linq;
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
    /// Task V4's <see cref="OrbitCameraController"/>: the glue between the
    /// accepted scene, the pure <see cref="OrbitCameraRig"/> and the camera
    /// transform. The mouse/keyboard reading itself is a two-line
    /// <c>Update</c> that EditMode cannot drive, so these tests exercise every
    /// command the input maps onto.
    /// </summary>
    public sealed class OrbitCameraControllerTests
    {
        private GameObject _cameraGo;
        private OrbitCameraController _controller;
        private GameObject _rendererGo;
        private RoomRenderer _roomRenderer;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("TestCamera", typeof(Camera));
            _controller = _cameraGo.AddComponent<OrbitCameraController>();

            _rendererGo = new GameObject("TestRoomRenderer");
            _roomRenderer = _rendererGo.AddComponent<RoomRenderer>();
            _controller.SetRoomRenderer(_roomRenderer);
        }

        [TearDown]
        public void TearDown()
        {
            _controller.Detach();
            _roomRenderer.Detach();
            Object.DestroyImmediate(_rendererGo);
            Object.DestroyImmediate(_cameraGo);
        }

        private static SceneSnapshot Snapshot(int revision, float width, float depth, float height = 2.5f)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = "session-1",
                revision = revision,
                scanPhase = "Finalized",
                finalized = true,
                closureErrorM = 0.05f,
                room = new RoomModel
                {
                    id = "room-1",
                    name = "Room",
                    heightM = height,
                    corners = new[]
                    {
                        new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                        new CornerModel { id = "c1", position = new Vec3Dto(width, 0f, 0f) },
                        new CornerModel { id = "c2", position = new Vec3Dto(width, 0f, depth) },
                        new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, depth) }
                    },
                    openings = new OpeningModel[0],
                    objects = new SceneObjectModel[0]
                }
            };
        }

        [Test]
        public void AcceptedSceneAutomaticallyFramesTheRoom()
        {
            var store = new ViewerSceneStore();
            _controller.Attach(store);

            Assert.IsTrue(store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out string error), error);

            Assert.AreEqual(2f, _controller.Rig.Target.x, 1e-3f);
            Assert.AreEqual(1.5f, _controller.Rig.Target.z, 1e-3f);
            Assert.IsTrue(_controller.Rig.IsValid);
        }

        [Test]
        public void DifferentRoomSizesProduceDifferentFramingDistances()
        {
            var smallStore = new ViewerSceneStore();
            _controller.Attach(smallStore);
            smallStore.TryApplyScannerSnapshot(Snapshot(0, 2f, 2f, 2.2f), out _);
            float smallDistance = _controller.Rig.Distance;

            var largeStore = new ViewerSceneStore();
            _controller.Attach(largeStore);
            largeStore.TryApplyScannerSnapshot(Snapshot(0, 9f, 7f, 3.6f), out _);
            float largeDistance = _controller.Rig.Distance;

            Assert.Less(smallDistance, largeDistance,
                "Framing must follow the actual room size, not a fixed 4 m assumption.");
        }

        [Test]
        public void ANewSnapshotDoesNotYankTheCameraAwayFromTheUsersView()
        {
            var store = new ViewerSceneStore();
            _controller.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _);

            _controller.Rig.Orbit(60f, 10f);
            float yaw = _controller.Rig.YawDeg;
            float pitch = _controller.Rig.PitchDeg;

            store.TryApplyScannerSnapshot(Snapshot(1, 4f, 3f), out _);

            Assert.AreEqual(yaw, _controller.Rig.YawDeg, 1e-3f,
                "Only the first accepted scene auto-frames; later ones must not fight the user.");
            Assert.AreEqual(pitch, _controller.Rig.PitchDeg, 1e-3f);
        }

        [Test]
        public void FrameRoomReFramesOnDemand()
        {
            var store = new ViewerSceneStore();
            _controller.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _);

            _controller.Rig.Orbit(120f, 20f);
            _controller.Rig.Zoom(6f);

            _controller.FrameRoom();

            Assert.AreEqual(OrbitCameraRig.HomeYawDeg, _controller.Rig.YawDeg, 1e-3f);
            Assert.AreEqual(OrbitCameraRig.HomePitchDeg, _controller.Rig.PitchDeg, 1e-3f);
            Assert.AreEqual(2f, _controller.Rig.Target.x, 1e-3f);
        }

        [Test]
        public void FrameRoomWithoutASceneDoesNotThrowOrProduceNaN()
        {
            Assert.DoesNotThrow(() => _controller.FrameRoom());
            Assert.IsTrue(_controller.Rig.IsValid);
        }

        [Test]
        public void ApplyToTransformDrivesTheCamera()
        {
            var store = new ViewerSceneStore();
            _controller.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _);

            _controller.ApplyToTransform();

            Assert.AreEqual(_controller.Rig.Position.x, _cameraGo.transform.position.x, 1e-3f);
            Assert.AreEqual(_controller.Rig.Position.y, _cameraGo.transform.position.y, 1e-3f);
            Assert.AreEqual(_controller.Rig.Position.z, _cameraGo.transform.position.z, 1e-3f);
            Assert.AreEqual(0f, Quaternion.Angle(_controller.Rig.Rotation, _cameraGo.transform.rotation), 1e-2f);
            Assert.Greater(_cameraGo.transform.position.y, 0f, "The camera must stay above the floor.");
        }

        [Test]
        public void DollhouseHidesTheCeilingAndSteepensTheView()
        {
            var store = new ViewerSceneStore();
            _roomRenderer.Attach(store);
            _controller.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _);

            Assert.IsFalse(_controller.DollhouseEnabled);
            Assert.IsTrue(_roomRenderer.CeilingVisible);

            _controller.SetDollhouse(true);

            Assert.IsTrue(_controller.DollhouseEnabled);
            Assert.IsFalse(_roomRenderer.CeilingVisible, "Dollhouse must reveal the interior.");
            Assert.AreEqual(OrbitCameraRig.DollhousePitchDeg, _controller.Rig.PitchDeg, 1e-3f);
        }

        [Test]
        public void LeavingDollhouseRestoresTheCeiling()
        {
            var store = new ViewerSceneStore();
            _roomRenderer.Attach(store);
            _controller.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _);

            _controller.SetDollhouse(true);
            _controller.SetDollhouse(false);

            Assert.IsFalse(_controller.DollhouseEnabled);
            Assert.IsTrue(_roomRenderer.CeilingVisible);
        }

        [Test]
        public void ToggleDollhouseFlipsTheMode()
        {
            var store = new ViewerSceneStore();
            _roomRenderer.Attach(store);
            _controller.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _);

            _controller.ToggleDollhouse();
            Assert.IsTrue(_controller.DollhouseEnabled);

            _controller.ToggleDollhouse();
            Assert.IsFalse(_controller.DollhouseEnabled);
        }

        [Test]
        public void DollhouseWithoutARoomRendererDoesNotThrow()
        {
            _controller.SetRoomRenderer(null);

            Assert.DoesNotThrow(() => _controller.SetDollhouse(true));
            Assert.IsTrue(_controller.DollhouseEnabled);
        }

        [Test]
        public void DetachStopsRespondingToTheStore()
        {
            var store = new ViewerSceneStore();
            _controller.Attach(store);
            store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _);

            float distance = _controller.Rig.Distance;
            _controller.Detach();

            store.TryApplyScannerSnapshot(Snapshot(1, 12f, 10f), out _);

            Assert.AreEqual(distance, _controller.Rig.Distance, 1e-3f);
        }

        [Test]
        public void AttachingToAStoreThatAlreadyHasASceneFramesItImmediately()
        {
            var store = new ViewerSceneStore();
            store.TryApplyScannerSnapshot(Snapshot(0, 6f, 5f), out _);

            _controller.Attach(store);

            Assert.AreEqual(3f, _controller.Rig.Target.x, 1e-3f);
            Assert.AreEqual(2.5f, _controller.Rig.Target.z, 1e-3f);
        }

        [Test]
        public void AttachTwiceDoesNotDoubleSubscribe()
        {
            var store = new ViewerSceneStore();
            _controller.Attach(store);
            _controller.Attach(store);

            Assert.DoesNotThrow(() => store.TryApplyScannerSnapshot(Snapshot(0, 4f, 3f), out _));
            Assert.IsTrue(_controller.Rig.IsValid);
        }
    }
}
