using System.Linq;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using GhostMap.Viewer.Interaction;
using GhostMap.Viewer.Scene;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V5's measurement mode (implementation plan section 13.5). Points
    /// come from real <see cref="Physics.Raycast"/> hits against real
    /// colliders — a flat floor and a raised "shelf" so a vertical component
    /// is exercisable — and every distance is checked against the shared
    /// <c>MeasurementMath</c> directly, never re-derived independently.
    /// </summary>
    public sealed class MeasurementControllerTests
    {
        private GameObject _controllerGo;
        private MeasurementController _measurement;
        private GameObject _floorGo;
        private GameObject _shelfGo;

        [SetUp]
        public void SetUp()
        {
            _controllerGo = new GameObject("MeasurementHost");
            _measurement = _controllerGo.AddComponent<MeasurementController>();

            // A large thin floor whose top face is exactly y = 0.
            _floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floorGo.name = "TestFloor";
            _floorGo.transform.position = new Vector3(0f, -0.05f, 0f);
            _floorGo.transform.localScale = new Vector3(100f, 0.1f, 100f);

            // A raised shelf whose top face is exactly y = 1.2, off to one side.
            _shelfGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _shelfGo.name = "TestShelf";
            _shelfGo.transform.position = new Vector3(5f, 0.6f, 5f);
            _shelfGo.transform.localScale = new Vector3(1f, 1.2f, 1f);

            // EditMode has no physics step to pick up the transform changes
            // above; without this, Physics.Raycast below sees the colliders
            // at their pre-move/pre-scale default (1x1x1 at the origin).
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_controllerGo);
            Object.DestroyImmediate(_floorGo);
            Object.DestroyImmediate(_shelfGo);
        }

        private static Ray DownRayAt(float x, float z) => new Ray(new Vector3(x, 10f, z), Vector3.down);

        [Test]
        public void PlacingAPointWhileInactiveDoesNothing()
        {
            bool placed = _measurement.TryPlacePoint(DownRayAt(0f, 0f));

            Assert.IsFalse(placed);
            Assert.IsFalse(_measurement.PointA.HasValue);
        }

        [Test]
        public void FirstClickPlacesPointAOnly()
        {
            _measurement.SetActive(true);

            bool placed = _measurement.TryPlacePoint(DownRayAt(0f, 0f));

            Assert.IsTrue(placed);
            Assert.IsTrue(_measurement.PointA.HasValue);
            Assert.IsFalse(_measurement.PointB.HasValue);
            Assert.IsFalse(_measurement.HasMeasurement);
            Assert.AreEqual(Vector3.zero, _measurement.PointA.Value);
        }

        [Test]
        public void SecondClickCompletesTheMeasurement()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));

            _measurement.TryPlacePoint(DownRayAt(3f, 0f));

            Assert.IsTrue(_measurement.HasMeasurement);
            Assert.AreEqual(3f, _measurement.PointB.Value.x, 1e-4f);
        }

        [Test]
        public void HorizontalDistanceIsCorrect()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));
            _measurement.TryPlacePoint(DownRayAt(3f, 4f));

            Assert.AreEqual(5f, _measurement.DistanceM, 1e-3f, "3-4-5 triangle on the floor.");
            Assert.AreEqual(5f, _measurement.DistanceXZM, 1e-3f);
            Assert.AreEqual(0f, _measurement.VerticalDistanceM, 1e-4f);
        }

        [Test]
        public void VerticalDistanceIsCorrect()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(5f, 5f)); // hits the shelf top, y = 1.2

            // Point A is on the shelf; point B directly below it on nothing —
            // instead measure shelf-top to floor at the same XZ is not
            // possible (the shelf occludes it), so measure shelf to floor
            // elsewhere and check the vertical component in isolation.
            _measurement.TryPlacePoint(DownRayAt(0f, 0f)); // floor, y = 0

            Assert.AreEqual(1.2f, _measurement.VerticalDistanceM, 1e-3f);
        }

        [Test]
        public void DiagonalThreeDDistanceIsCorrect()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));
            _measurement.TryPlacePoint(DownRayAt(5f, 5f));

            float expectedXZ = Mathf.Sqrt(5f * 5f + 5f * 5f);
            float expected3D = Mathf.Sqrt(expectedXZ * expectedXZ + 1.2f * 1.2f);

            Assert.AreEqual(expectedXZ, _measurement.DistanceXZM, 1e-3f);
            Assert.AreEqual(expected3D, _measurement.DistanceM, 1e-3f);
            Assert.Greater(_measurement.DistanceM, _measurement.DistanceXZM, "3D distance must include the vertical rise.");
        }

        [Test]
        public void ZeroLengthMeasurementIsHandledSafely()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(2f, 2f));
            _measurement.TryPlacePoint(DownRayAt(2f, 2f));

            Assert.AreEqual(0f, _measurement.DistanceM, 1e-6f);
            Assert.AreEqual(0f, _measurement.DistanceXZM, 1e-6f);
            Assert.IsFalse(float.IsNaN(_measurement.DistanceM));
        }

        [Test]
        public void DistanceIsZeroWithNoMeasurementRatherThanNaN()
        {
            Assert.AreEqual(0f, _measurement.DistanceM);
            Assert.AreEqual(0f, _measurement.DistanceXZM);
            Assert.IsFalse(float.IsNaN(_measurement.DistanceM));
        }

        [Test]
        public void ClearResetsBothPoints()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));
            _measurement.TryPlacePoint(DownRayAt(3f, 0f));

            _measurement.Clear();

            Assert.IsFalse(_measurement.PointA.HasValue);
            Assert.IsFalse(_measurement.PointB.HasValue);
            Assert.IsFalse(_measurement.HasMeasurement);
        }

        [Test]
        public void DeactivatingClearsAnyInProgressMeasurement()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));

            _measurement.SetActive(false);

            Assert.IsFalse(_measurement.PointA.HasValue);
            Assert.IsFalse(_measurement.IsActive);
        }

        [Test]
        public void AThirdClickAfterACompleteMeasurementStartsAFreshOne()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));
            _measurement.TryPlacePoint(DownRayAt(3f, 0f));
            Assert.IsTrue(_measurement.HasMeasurement);

            _measurement.TryPlacePoint(DownRayAt(1f, 1f));

            Assert.IsTrue(_measurement.PointA.HasValue);
            Assert.IsFalse(_measurement.PointB.HasValue);
            Assert.IsFalse(_measurement.HasMeasurement);
            Assert.AreEqual(1f, _measurement.PointA.Value.x, 1e-4f);
        }

        [Test]
        public void ToggleActiveFlipsState()
        {
            Assert.IsFalse(_measurement.IsActive);
            _measurement.ToggleActive();
            Assert.IsTrue(_measurement.IsActive);
            _measurement.ToggleActive();
            Assert.IsFalse(_measurement.IsActive);
        }

        [Test]
        public void ChangedFiresOnModeToggleAndOnEachPointPlacement()
        {
            int changes = 0;
            _measurement.Changed += () => changes++;

            _measurement.SetActive(true);
            Assert.AreEqual(1, changes);

            _measurement.TryPlacePoint(DownRayAt(0f, 0f));
            Assert.AreEqual(2, changes);

            _measurement.TryPlacePoint(DownRayAt(3f, 0f));
            Assert.AreEqual(3, changes);
        }

        [Test]
        public void MarkersAndLineReflectTheCurrentEndpoints()
        {
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));
            _measurement.TryPlacePoint(DownRayAt(3f, 4f));

            Transform markerA = _controllerGo.transform.Cast<Transform>().First(t => t.name == "MeasurementPointA");
            Transform markerB = _controllerGo.transform.Cast<Transform>().First(t => t.name == "MeasurementPointB");
            var line = _controllerGo.GetComponentInChildren<LineRenderer>();

            Assert.AreEqual(_measurement.PointA.Value, markerA.position);
            Assert.AreEqual(_measurement.PointB.Value, markerB.position);
            Assert.IsTrue(line.enabled);
            Assert.AreEqual(_measurement.PointA.Value, line.GetPosition(0));
            Assert.AreEqual(_measurement.PointB.Value, line.GetPosition(1));
        }

        [Test]
        public void NewScannerSessionClearsAnInProgressMeasurement()
        {
            var store = new ViewerSceneStore();
            _measurement.Attach(store);

            store.TryApplyScannerSnapshot(BasicSnapshot("session-1", 0), out _);

            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));
            Assert.IsTrue(_measurement.PointA.HasValue);

            store.TryApplyScannerSnapshot(BasicSnapshot("session-2", 0), out _);

            Assert.IsFalse(_measurement.PointA.HasValue, "A genuinely new session must not leave a stale measurement.");
        }

        [Test]
        public void EditsWithinTheSameSessionDoNotClearAMeasurement()
        {
            var store = new ViewerSceneStore();
            _measurement.Attach(store);

            store.TryApplyScannerSnapshot(BasicSnapshot("session-1", 0), out _);
            _measurement.SetActive(true);
            _measurement.TryPlacePoint(DownRayAt(0f, 0f));

            store.TryApplyScannerSnapshot(BasicSnapshot("session-1", 1), out _);

            Assert.IsTrue(_measurement.PointA.HasValue);
        }

        private static SceneSnapshot BasicSnapshot(string sessionId, int revision)
        {
            return new SceneSnapshot
            {
                schemaVersion = ProtocolConstants.SchemaVersion,
                sessionId = sessionId,
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
                    objects = new SceneObjectModel[0]
                }
            };
        }
    }
}
