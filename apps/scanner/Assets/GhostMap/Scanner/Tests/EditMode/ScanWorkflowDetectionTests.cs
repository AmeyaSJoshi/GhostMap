using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// ADR-0006 at the workflow level: what detection publishes and when, that
    /// it needs no new scan phase, and that the Task S5 manual path still works
    /// alongside it.
    /// </summary>
    public sealed class ScanWorkflowDetectionTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);

        private const float YawDeg = 40f;

        private static readonly Vector2[] RoomCorners =
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(4f, 3f),
            new Vector2(0f, 3f)
        };

        private static ulong nextSurfaceId = 5000;

        // -------------------------------------------------------------------
        // Fixture
        // -------------------------------------------------------------------

        private static FakeSpatialProvider Provider()
        {
            return new FakeSpatialProvider
            {
                IsTrackingGood = true,
                HasCameraPose = true,
                HasFloorHit = true,
                HasPlaneDetection = true,
                FloorHitAlignment = PlaneAlignment.HorizontalUp,
                FloorHitWorldPosition = FloorOrigin,
                CameraPose = new Pose(
                    FloorOrigin + new Vector3(0f, 1.5f, 0f),
                    Quaternion.Euler(30f, YawDeg, 0f))
            };
        }

        private static ScanWorkflowController Workflow(FakeSpatialProvider provider)
        {
            var floorLock = new FloorLockController(provider);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var openingCapture = new OpeningCaptureController(provider, floorLock, corners, height);
            var objectPlacement = new ObjectPlacementController(provider, floorLock);
            var furnitureDetection = new FurnitureDetectionController(provider, floorLock, corners);

            return new ScanWorkflowController(
                floorLock, corners, height, openingCapture, objectPlacement, furnitureDetection);
        }

        private static void AimAtGhost(
            FakeSpatialProvider provider,
            GhostCoordinateFrame frame,
            float ghostX,
            float ghostZ)
        {
            Vector3 target = frame.GhostToWorld(new Vector3(ghostX, 0f, ghostZ));
            Vector3 eye = frame.GhostToWorld(new Vector3(ghostX, 1.5f, ghostZ - 1f));

            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        /// <summary>
        /// Drives the workflow all the way to <see cref="ScanPhase.AddObjects"/>,
        /// which is the only phase detection runs in.
        /// </summary>
        private static ScanWorkflowController AddObjectsWorkflow(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = Workflow(provider);

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out FloorLockRejection floorRejection), floorRejection.ToString());
            Assert.IsTrue(workflow.BeginCornerCapture());

            for (int i = 0; i < RoomCorners.Length; i++)
            {
                AimAtGhost(provider, workflow.Frame, RoomCorners[i].x, RoomCorners[i].y);
                Assert.IsTrue(
                    workflow.TryCaptureCorner(out CornerCaptureRejection cornerRejection),
                    $"corner {i}: {cornerRejection} {workflow.Corners.LastError}");
            }

            AimAtGhost(provider, workflow.Frame, RoomCorners[0].x, RoomCorners[0].y);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));

            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out HeightCaptureRejection heightRejection),
                heightRejection.ToString());
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);

            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);

            return workflow;
        }

        private static DetectedSurface DeskSurface(
            GhostCoordinateFrame frame,
            float ghostX = 2f,
            float ghostZ = 1.5f,
            float ghostY = 0.73f,
            float widthM = 1.4f,
            float depthM = 0.7f)
        {
            Vector3 worldCenter = frame.GhostToWorld(new Vector3(ghostX, ghostY, ghostZ));
            Vector3 worldAxis = frame.GhostDirectionToWorld(Vector3.forward);

            Quaternion worldRotation =
                Quaternion.LookRotation(worldAxis, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);

            return new DetectedSurface(
                new TrackableId(nextSurfaceId++, 0),
                worldCenter,
                worldRotation,
                new Vector2(widthM, depthM),
                PlaneAlignment.HorizontalUp);
        }

        // -------------------------------------------------------------------
        // No new scan phase
        // -------------------------------------------------------------------

        /// <summary>
        /// ADR-0006 deliberately adds no phase: detection is a second input
        /// method for AddObjects. A new phase would be a wire-visible change to
        /// <c>scanPhase</c> and a change to the state machine, for nothing.
        /// </summary>
        [Test]
        public void Detection_RunsInsideTheExistingAddObjectsPhase()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);
            Assert.AreEqual("AddObjects", workflow.Snapshot.scanPhase);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out FurnitureDetectionRejection rejection),
                rejection.ToString());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase, "detection must not change phase");
        }

        [Test]
        public void DetectionOperations_AreRefusedOutsideAddObjects()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = Workflow(provider);

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Assert.AreEqual(ScanPhase.FloorLocked, workflow.Phase);

            Assert.IsFalse(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsFalse(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsFalse(workflow.SelectNextDetectedCandidate());
            Assert.IsFalse(workflow.TryAcceptDetectedFurniture(out _, out _));
            Assert.IsFalse(workflow.DismissDetectedCandidate());
        }

        // -------------------------------------------------------------------
        // What publishes, and what must not
        // -------------------------------------------------------------------

        /// <summary>
        /// The HUD refreshes on a timer, so publishing here would break
        /// performance target section 24's "snapshots only on mutation" at a
        /// tick rate.
        /// </summary>
        [Test]
        public void RefreshingDetection_PublishesNothing()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));

            int before = workflow.Revision;

            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            }

            Assert.AreEqual(1, workflow.FurnitureDetection.CandidateCount);
            Assert.AreEqual(before, workflow.Revision);
        }

        [Test]
        public void CyclingAndDismissing_PublishNothing()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));
            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame, ghostX: 3.2f, ghostZ: 2.4f));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            int before = workflow.Revision;

            Assert.IsTrue(workflow.SelectNextDetectedCandidate());
            Assert.IsTrue(workflow.SetDetectedFurnitureType("table"));
            Assert.IsTrue(workflow.DismissDetectedCandidate());

            Assert.AreEqual(before, workflow.Revision);
            Assert.AreEqual(0, workflow.Snapshot.room.objects.Length);
        }

        [Test]
        public void AcceptingACandidate_PublishesExactlyOneRevision()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));

            int before = workflow.Revision;

            Assert.IsTrue(
                workflow.TryAcceptDetectedFurniture(
                    out FurnitureDetectionRejection detectionRejection,
                    out ObjectPlacementRejection placementRejection),
                $"{detectionRejection} / {placementRejection}: {workflow.Objects.LastError}");

            Assert.AreEqual(before + 1, workflow.Revision);
            Assert.AreEqual(1, workflow.Snapshot.room.objects.Length);
        }

        /// <summary>
        /// The measured dimensions must survive all the way to the published
        /// snapshot, or detection has achieved nothing.
        /// </summary>
        [Test]
        public void AcceptedCandidate_ReachesTheSnapshotWithItsMeasuredDimensions()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(
                workflow.Frame, ghostX: 2f, ghostZ: 1.5f, ghostY: 0.69f,
                widthM: 1.83f, depthM: 0.62f));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));

            SceneObjectModel published = workflow.Snapshot.room.objects[0];

            Assert.AreEqual("desk", published.type);
            Assert.AreEqual(1.83f, published.widthM, Tolerance);
            Assert.AreEqual(0.62f, published.depthM, Tolerance);
            Assert.AreEqual(0.69f, published.heightM, Tolerance);
            Assert.AreEqual(2f, published.center.x, Tolerance);
            Assert.AreEqual(1.5f, published.center.z, Tolerance);
            Assert.AreEqual(0f, published.center.y, Tolerance, "objects sit on the floor plane");
        }

        [Test]
        public void AcceptingTheSameSurfaceTwice_AddsOnlyOneObject()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));

            // The plane is still detected and keeps being reported.
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.AreEqual(0, workflow.FurnitureDetection.CandidateCount);
            Assert.IsFalse(workflow.TryAcceptDetectedFurniture(out _, out _));

            Assert.AreEqual(1, workflow.Snapshot.room.objects.Length);
        }

        // -------------------------------------------------------------------
        // Detection and the S5 manual path coexist
        // -------------------------------------------------------------------

        /// <summary>
        /// ADR-0006 keeps manual placement, because plane detection fails on
        /// glass, dark upholstery and duvets. Both must feed the same object
        /// list.
        /// </summary>
        [Test]
        public void ManualPlacement_StillWorksAlongsideDetection()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));

            // Now place one by hand, the Task S5 way.
            Assert.IsTrue(workflow.SetObjectType("chair"));
            AimAtGhost(provider, workflow.Frame, 1f, 2f);
            Assert.IsTrue(
                workflow.TryPlaceObject(out ObjectPlacementRejection rejection),
                $"{rejection}: {workflow.Objects.LastError}");

            Assert.AreEqual(2, workflow.Snapshot.room.objects.Length);
            Assert.AreEqual("desk", workflow.Snapshot.room.objects[0].type);
            Assert.AreEqual("chair", workflow.Snapshot.room.objects[1].type);
        }

        /// <summary>
        /// A detected chair's measured height is its seat, not its back, so the
        /// user has to be able to correct it — which only works because detected
        /// objects land in the same store S5's adjustment controls act on.
        /// </summary>
        [Test]
        public void DetectedObject_CanBeAdjustedByTheS5Controls()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(
                workflow.Frame, ghostY: 0.45f, widthM: 0.5f, depthM: 0.5f));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("chair"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));

            Assert.AreEqual(0.45f, workflow.Snapshot.room.objects[0].heightM, Tolerance);

            // The seat was detected; the real chair is taller.
            Assert.IsTrue(
                workflow.TrySetObjectHeight(0, 0.95f, out ObjectPlacementRejection rejection),
                rejection.ToString());

            Assert.AreEqual(0.95f, workflow.Snapshot.room.objects[0].heightM, Tolerance);
        }

        [Test]
        public void FinishAddingObjects_StillWorksAfterDetection()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));

            Assert.IsTrue(workflow.FinishAddingObjects());
            Assert.AreEqual(ScanPhase.ReadyToFinalize, workflow.Phase);
        }

        [Test]
        public void DetectedObject_SurvivesFinalization()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));

            Assert.IsTrue(workflow.FinishAddingObjects());
            Assert.IsTrue(workflow.TryFinalize(out FinalizeRejection rejection), rejection.ToString());

            Assert.IsTrue(workflow.Snapshot.finalized);
            Assert.AreEqual(1, workflow.Snapshot.room.objects.Length);
            Assert.AreEqual("desk", workflow.Snapshot.room.objects[0].type);
        }

        /// <summary>
        /// Every mutation method already refuses outside its own phase, and
        /// Finalized is not one of them, so detection cannot mutate a finalized
        /// scan either.
        /// </summary>
        [Test]
        public void Detection_IsRefusedAfterFinalization()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);

            Assert.IsTrue(workflow.FinishAddingObjects());
            Assert.IsTrue(workflow.TryFinalize(out _));

            provider.DetectedSurfaces.Add(DeskSurface(workflow.Frame));

            int revision = workflow.Revision;

            Assert.IsFalse(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsFalse(workflow.TryAcceptDetectedFurniture(out _, out _));
            Assert.AreEqual(revision, workflow.Revision);
            Assert.AreEqual(0, workflow.Snapshot.room.objects.Length);
        }
    }
}
