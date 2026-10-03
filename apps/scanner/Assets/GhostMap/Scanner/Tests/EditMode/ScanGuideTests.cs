using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.UI;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// The plain-language guide: every step names itself, says what to do,
    /// says where to point when pointing matters, and surfaces the
    /// controllers' own refusals instead of hiding them behind Details.
    /// Also covers <see cref="ScanWorkflowController.RedoRoom"/>, which the
    /// guide's "Redo Walls" wording depends on.
    /// </summary>
    public sealed class ScanGuideTests
    {
        private static readonly Vector3 FloorOrigin = new Vector3(1.1f, -1.3f, 0.4f);

        private static readonly ScanGuideContext Tracking =
            new ScanGuideContext(true, FloorLockRejection.None, connectedToComputer: false);

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
                FloorHitAlignment = PlaneAlignment.HorizontalUp,
                FloorHitWorldPosition = FloorOrigin,
                CameraPose = new Pose(FloorOrigin + new Vector3(0f, 1.5f, 0f), Quaternion.Euler(30f, 25f, 0f))
            };
        }

        private static ScanWorkflowController Workflow(FakeSpatialProvider provider)
        {
            var floorLock = new FloorLockController(provider);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);

            return new ScanWorkflowController(
                floorLock,
                new WallSweepController(provider, floorLock),
                corners,
                height,
                new OpeningCaptureController(provider, floorLock, corners, height),
                new ObjectPlacementController(provider, floorLock),
                new FurnitureDetectionController(provider, floorLock, corners));
        }

        private static ScanWorkflowController SweepingWorkflow(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = Workflow(provider);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Assert.IsTrue(workflow.BeginWallSweeping());
            return workflow;
        }

        private static void AimAtGhost(FakeSpatialProvider provider, GhostCoordinateFrame frame, float x, float z)
        {
            Vector3 target = frame.GhostToWorld(new Vector3(x, 0f, z));
            Vector3 eye = frame.GhostToWorld(new Vector3(x, 1.5f, z - 1f));
            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        private static void SweepWall(
            FakeSpatialProvider provider, ScanWorkflowController workflow, Vector2 from, Vector2 to)
        {
            Assert.IsTrue(workflow.TryBeginWallSweep(out _));

            for (int i = 0; i < 20; i++)
            {
                float t = i / 19f;
                AimAtGhost(provider, workflow.Frame, Mathf.Lerp(from.x, to.x, t), Mathf.Lerp(from.y, to.y, t));
                workflow.TryAddWallSweepSample(out _);
            }

            Assert.IsTrue(workflow.TryCompleteWallSweep(out _), workflow.WallSweep.LastError);
        }

        private static void SweepLegalRoom(FakeSpatialProvider provider, ScanWorkflowController workflow)
        {
            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, workflow, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));
            SweepWall(provider, workflow, new Vector2(0.2f, 3f), new Vector2(3.8f, 3f));
            SweepWall(provider, workflow, new Vector2(0f, 0.2f), new Vector2(0f, 2.8f));
        }

        // -------------------------------------------------------------------
        // Step 1
        // -------------------------------------------------------------------

        [Test]
        public void BeforeTracking_AsksTheUserToMoveThePhone_WithNothingToAimAt()
        {
            ScanWorkflowController workflow = Workflow(Provider());
            workflow.Tick(false);

            ScanGuideStep step = ScanGuide.Describe(
                workflow, new ScanGuideContext(false, FloorLockRejection.None, false));

            Assert.AreEqual(1, step.Number);
            StringAssert.Contains("move your phone", step.Instruction);
            Assert.IsNull(step.AimHint);
        }

        [Test]
        public void FindFloor_PointsAtTheFloor_AndExplainsAWrongSurface()
        {
            ScanWorkflowController workflow = Workflow(Provider());
            workflow.Tick(true);

            ScanGuideStep plain = ScanGuide.Describe(workflow, Tracking);
            Assert.AreEqual(1, plain.Number);
            Assert.AreEqual("Aim at the floor", plain.AimHint);
            StringAssert.Contains("Lock Floor", plain.Instruction);

            ScanGuideStep refused = ScanGuide.Describe(
                workflow, new ScanGuideContext(true, FloorLockRejection.PlaneNotHorizontalUp, false));
            Assert.AreEqual(GuideMessageKind.Warning, refused.MessageKind);
            StringAssert.Contains("not the floor", refused.Message);
        }

        // -------------------------------------------------------------------
        // Step 2
        // -------------------------------------------------------------------

        [Test]
        public void Sweeping_NamesTheFloorWallJoin_AndCountsWalls()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            ScanGuideStep first = ScanGuide.Describe(workflow, Tracking);
            Assert.AreEqual(2, first.Number);
            Assert.AreEqual("Where floor meets wall", first.AimHint);

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));

            ScanGuideStep second = ScanGuide.Describe(workflow, Tracking);
            StringAssert.Contains("1 of 4 done", second.Instruction);
        }

        [Test]
        public void ARefusedSweep_ShowsTheControllersReasonAsAnError()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            // A 10 cm sweep: far below the fit's minimum span.
            Assert.IsTrue(workflow.TryBeginWallSweep(out _));
            for (int i = 0; i < 12; i++)
            {
                AimAtGhost(provider, workflow.Frame, i * 0.01f, 0f);
                workflow.TryAddWallSweepSample(out _);
            }

            Assert.IsFalse(workflow.TryCompleteWallSweep(out _));

            ScanGuideStep step = ScanGuide.Describe(workflow, Tracking);
            Assert.AreEqual(GuideMessageKind.Error, step.MessageKind);
            Assert.AreEqual(workflow.WallSweep.LastError, step.Message);
        }

        [Test]
        public void FourWalls_AskForBuildRoom_WithNoAimTarget()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);
            SweepLegalRoom(provider, workflow);

            ScanGuideStep step = ScanGuide.Describe(workflow, Tracking);
            StringAssert.Contains("Build Room", step.Instruction);
            Assert.IsNull(step.AimHint);
        }

        // -------------------------------------------------------------------
        // Steps 3-7 and RedoRoom
        // -------------------------------------------------------------------

        [Test]
        public void ARejectedCornerCheck_OnASweptRoom_SaysRedoWalls_AndRedoRoomReturnsToSweeping()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);
            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));
            Assert.IsTrue(workflow.RoomWasSwept);

            AimAtGhost(provider, workflow.Frame, 0.3f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));

            ScanGuideStep step = ScanGuide.Describe(workflow, Tracking);
            Assert.AreEqual(3, step.Number);
            Assert.AreEqual(GuideMessageKind.Error, step.MessageKind);
            StringAssert.Contains("Redo Walls", step.Instruction);
            StringAssert.Contains("30 cm", step.Message);

            Assert.IsTrue(workflow.RedoRoom());
            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreEqual(0, workflow.WallSweep.WallCount);
            Assert.AreEqual(0, workflow.Corners.CornerCount);
        }

        [Test]
        public void RedoRoom_OnAWalkedRoom_ReturnsToWalkingCorners()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);
            Assert.IsTrue(workflow.FallBackToWalkedCorners());

            foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 3f), new Vector2(0f, 3f) })
            {
                AimAtGhost(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _), workflow.Corners.LastError);
            }

            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);
            Assert.IsFalse(workflow.RoomWasSwept);
            StringAssert.Contains("Redo Corners", Describe3Rejected(provider, workflow));

            Assert.IsTrue(workflow.RedoRoom());
            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);
        }

        private static string Describe3Rejected(FakeSpatialProvider provider, ScanWorkflowController workflow)
        {
            AimAtGhost(provider, workflow.Frame, 0.3f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            return ScanGuide.Describe(workflow, Tracking).Instruction;
        }

        [Test]
        public void AFullScan_WalksTheStepsInOrder_AndTheEndDependsOnTheComputerConnection()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);
            int last = ScanGuide.Describe(workflow, Tracking).Number;

            void Expect(int number)
            {
                ScanGuideStep step = ScanGuide.Describe(workflow, Tracking);
                Assert.AreEqual(number, step.Number, workflow.Phase.ToString());
                Assert.GreaterOrEqual(step.Number, last);
                Assert.IsFalse(string.IsNullOrEmpty(step.Title));
                Assert.IsFalse(string.IsNullOrEmpty(step.Instruction));
                last = step.Number;
            }

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));
            Expect(3);

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Expect(4);
            Assert.AreEqual(GuideMessageKind.Success, ScanGuide.Describe(workflow, Tracking).MessageKind);
            Assert.AreEqual("Where wall meets ceiling", ScanGuide.Describe(workflow, Tracking).AimHint);

            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Expect(5);
            StringAssert.Contains("door", ScanGuide.Describe(workflow, Tracking).AimHint);

            Assert.IsTrue(workflow.FinishAddingOpenings());
            Expect(6);

            Assert.IsTrue(workflow.FinishAddingObjects());
            Expect(7);

            StringAssert.Contains("Connect to your computer", ScanGuide.Describe(workflow, Tracking).Instruction);

            ScanGuideStep connected = ScanGuide.Describe(
                workflow, new ScanGuideContext(true, FloorLockRejection.None, connectedToComputer: true));
            StringAssert.Contains("Finish & Send", connected.Instruction);
            Assert.AreEqual(GuideMessageKind.Success, connected.MessageKind);
        }

        // -------------------------------------------------------------------
        // Step 6 — detected furniture (ADR-0006)
        // -------------------------------------------------------------------

        private static ScanWorkflowController SweptRoomAtAddObjects(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = SweepingWorkflow(provider);
            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));
            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);
            return workflow;
        }

        private static DetectedSurface DeskTop(GhostCoordinateFrame frame, ulong id, float x, float z)
        {
            Quaternion rotation =
                Quaternion.LookRotation(frame.GhostDirectionToWorld(Vector3.forward), Vector3.up)
                * Quaternion.Euler(0f, -90f, 0f);

            return new DetectedSurface(
                new TrackableId(id, 0),
                frame.GhostToWorld(new Vector3(x, 0.74f, z)),
                rotation,
                new Vector2(1.2f, 0.6f),
                PlaneAlignment.HorizontalUp);
        }

        [Test]
        public void Step6_WithNothingDetected_KeepsTheManualPlacementWording()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoomAtAddObjects(provider);
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            ScanGuideStep step = ScanGuide.Describe(workflow, Tracking);

            Assert.AreEqual(6, step.Number);
            StringAssert.Contains("tap Place", step.Instruction);
            Assert.IsFalse(string.IsNullOrEmpty(step.AimHint));
            StringAssert.Contains("Next", step.Message);
        }

        [Test]
        public void Step6_WithADetectedSurface_OffersItWithItsMeasurements()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoomAtAddObjects(provider);
            provider.DetectedSurfaces.Add(DeskTop(workflow.Frame, 7001, 2f, 1.5f));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.FurnitureDetection.HasSelection);

            ScanGuideStep step = ScanGuide.Describe(workflow, Tracking);

            Assert.AreEqual(6, step.Number);
            StringAssert.Contains("Add", step.Instruction);
            StringAssert.Contains("Skip", step.Instruction);
            Assert.IsNull(step.AimHint, "a detected surface needs no aiming");
            StringAssert.Contains("Found a surface", step.Message);
            StringAssert.Contains("top 0.74 m", step.Message);
        }

        [Test]
        public void Step6_WithSeveralSurfaces_SaysWhichOneIsOffered()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoomAtAddObjects(provider);
            provider.DetectedSurfaces.Add(DeskTop(workflow.Frame, 7002, 1f, 1f));
            provider.DetectedSurfaces.Add(DeskTop(workflow.Frame, 7003, 3f, 2f));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            StringAssert.Contains("Surface 1 of 2", ScanGuide.Describe(workflow, Tracking).Message);
        }

        [Test]
        public void Step6_AfterTheOnlySurfaceIsAdded_ReturnsToManualWording()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoomAtAddObjects(provider);
            provider.DetectedSurfaces.Add(DeskTop(workflow.Frame, 7004, 2f, 1.5f));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            ScanGuideStep step = ScanGuide.Describe(workflow, Tracking);

            Assert.IsFalse(workflow.FurnitureDetection.HasSelection, "an added surface must not be offered again");
            StringAssert.Contains("tap Place", step.Instruction);
            Assert.AreEqual(1, workflow.Objects.ObjectCount);
        }

        // -------------------------------------------------------------------
        // Demo mode
        // -------------------------------------------------------------------

        [Test]
        public void DemoMode_NeverRunsOnARealDevice_OrWhenARKitIsLoaded()
        {
            // Editor or Simulator without ARKit: demo.
            Assert.IsTrue(AR.SimulatedRoom.ShouldSimulate(isEditor: true, isIosSimulator: false, hasActiveXrLoader: false));
            Assert.IsTrue(AR.SimulatedRoom.ShouldSimulate(isEditor: false, isIosSimulator: true, hasActiveXrLoader: false));

            // A physical iPhone — even one whose ARKit loader failed — never.
            Assert.IsFalse(AR.SimulatedRoom.ShouldSimulate(isEditor: false, isIosSimulator: false, hasActiveXrLoader: false));
            Assert.IsFalse(AR.SimulatedRoom.ShouldSimulate(isEditor: false, isIosSimulator: false, hasActiveXrLoader: true));

            // ARKit running anywhere wins.
            Assert.IsFalse(AR.SimulatedRoom.ShouldSimulate(isEditor: true, isIosSimulator: false, hasActiveXrLoader: true));
            Assert.IsFalse(AR.SimulatedRoom.ShouldSimulate(isEditor: false, isIosSimulator: true, hasActiveXrLoader: true));

            // This EditMode test itself runs on a Mac, not in the Simulator.
            Assert.IsFalse(AR.SimulatedRoom.IsRunningInIosSimulator());
        }

        [Test]
        public void DemoFloor_IsHitInsideTheRoomOnly()
        {
            var eye = new Vector3(0f, 1.5f, 0f);

            Assert.IsTrue(AR.SimulatedRoom.TryRaycastFloor(
                new Ray(eye, new Vector3(0f, -1f, 1f).normalized), out Vector3 hit));
            Assert.AreEqual(0f, hit.y, 1e-5f);
            Assert.AreEqual(1.5f, hit.z, 1e-4f);

            Assert.IsFalse(AR.SimulatedRoom.TryRaycastFloor(new Ray(eye, Vector3.forward), out _));
            Assert.IsFalse(AR.SimulatedRoom.TryRaycastFloor(new Ray(eye, Vector3.up), out _));
            Assert.IsFalse(AR.SimulatedRoom.TryRaycastFloor(
                new Ray(eye, new Vector3(0f, -0.1f, 1f).normalized), out _));
        }
    }
}
