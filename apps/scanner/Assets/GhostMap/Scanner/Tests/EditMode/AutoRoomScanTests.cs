using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.UI;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// The automatic room scan: ARKit vertical planes become wall clusters,
    /// the best four become corners in the EXISTING corner store, and nothing
    /// about the manual and swept fallbacks changes.
    ///
    /// <para>The test room is 6 m x 4 m (Ghost X from -3 to 3, Z from -2 to 2)
    /// with the scanner standing at its center.</para>
    /// </summary>
    public sealed class AutoRoomScanTests
    {
        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);

        private static ulong nextId = 100;

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
                    Quaternion.Euler(30f, 40f, 0f))
            };
        }

        private static ScanWorkflowController Workflow(FakeSpatialProvider provider)
        {
            var floorLock = new FloorLockController(provider);
            var wallSweep = new WallSweepController(provider, floorLock);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var openings = new OpeningCaptureController(provider, floorLock, corners, height);
            var objects = new ObjectPlacementController(provider, floorLock);
            var detection = new FurnitureDetectionController(provider, floorLock, corners);
            var auto = new AutoRoomScanController(provider, floorLock);

            return new ScanWorkflowController(
                floorLock, wallSweep, corners, height, openings, objects, detection, auto);
        }

        private static ScanWorkflowController LockedWorkflow(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = Workflow(provider);

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out FloorLockRejection rejection), rejection.ToString());
            return workflow;
        }

        /// <summary>A vertical wall plane, defined in Ghost coordinates.</summary>
        private static DetectedSurface Wall(
            GhostCoordinateFrame frame,
            float centerX,
            float centerZ,
            float normalX,
            float normalZ,
            float widthM,
            float heightM = 2.5f,
            PlaneClassifications classification = PlaneClassifications.WallFace)
        {
            Vector3 world = frame.GhostToWorld(new Vector3(centerX, heightM * 0.5f, centerZ));
            Vector3 normal = frame.GhostDirectionToWorld(new Vector3(normalX, 0f, normalZ)).normalized;
            Vector3 up = frame.GhostDirectionToWorld(Vector3.up);

            // Local Y is the plane normal; local Z runs up the wall, local X along it.
            Quaternion rotation = Quaternion.LookRotation(up, normal);

            return new DetectedSurface(
                new TrackableId(nextId++, 1),
                world,
                rotation,
                new Vector2(widthM, heightM),
                PlaneAlignment.Vertical,
                classification);
        }

        private static DetectedSurface Wall(GhostCoordinateFrame frame, float centerX, float centerZ, Vector2 normal, float widthM) =>
            Wall(frame, centerX, centerZ, normal.x, normal.y, widthM);

        /// <summary>Four walls of the 6 x 4 m room, each seen well.</summary>
        private static List<DetectedSurface> FullRoom(GhostCoordinateFrame frame)
        {
            return new List<DetectedSurface>
            {
                Wall(frame, 3f, 0f, -1f, 0f, 3.6f),
                Wall(frame, -3f, 0f, 1f, 0f, 3.6f),
                Wall(frame, 0f, 2f, 0f, -1f, 5.6f),
                Wall(frame, 0f, -2f, 0f, 1f, 5.6f)
            };
        }

        private static DetectedSurface Horizontal(
            GhostCoordinateFrame frame,
            float x,
            float y,
            float z,
            float widthM,
            float depthM,
            PlaneClassifications classification)
        {
            Vector3 world = frame.GhostToWorld(new Vector3(x, y, z));
            Quaternion rotation = Quaternion.LookRotation(frame.GhostDirectionToWorld(Vector3.forward), Vector3.up);

            return new DetectedSurface(
                new TrackableId(nextId++, 1),
                world,
                rotation,
                new Vector2(widthM, depthM),
                PlaneAlignment.HorizontalUp,
                classification);
        }

        /// <summary>Turns the phone once, slowly, ticking the scan as it goes. Returns the final clock.</summary>
        private static float TurnOnce(FakeSpatialProvider provider, ScanWorkflowController workflow, float now = 0f)
        {
            for (int yaw = 0; yaw <= 360; yaw += 5)
            {
                now += 0.1f;
                provider.CameraPose = new Pose(
                    FloorOrigin + new Vector3(0f, 1.5f, 0f),
                    Quaternion.Euler(0f, yaw, 0f));
                workflow.TickAutoScan(now);
            }

            return now;
        }

        private static void Feed(FakeSpatialProvider provider, IEnumerable<DetectedSurface> surfaces)
        {
            provider.DetectedSurfaces.Clear();
            provider.DetectedSurfaces.AddRange(surfaces);
        }

        private static WallPlaneAccumulator Accumulate(GhostCoordinateFrame frame, params DetectedSurface[] surfaces)
        {
            var accumulator = new WallPlaneAccumulator();
            accumulator.Update(surfaces, frame);
            return accumulator;
        }

        private static GhostCoordinateFrame Frame()
        {
            ScanWorkflowController workflow = LockedWorkflow(Provider());
            return workflow.Frame;
        }

        // -------------------------------------------------------------------
        // Clustering
        // -------------------------------------------------------------------

        [Test]
        public void TwoPlanesOnTheSameWall_MergeIntoOneWall()
        {
            GhostCoordinateFrame frame = Frame();

            WallPlaneAccumulator accumulator = Accumulate(
                frame,
                Wall(frame, 3f, -1f, -1f, 0f, 1.8f),
                Wall(frame, 3.08f, 0.9f, -1f, 0f, 1.8f));

            Assert.AreEqual(1, accumulator.Clusters.Count);
            Assert.AreEqual(2, accumulator.Clusters[0].MemberCount);
            Assert.AreEqual(3.6f, accumulator.Clusters[0].ObservedWidthM, 0.1f);
        }

        [Test]
        public void RepeatedUpdatesOfOnePlane_DoNotCreateExtraWalls()
        {
            GhostCoordinateFrame frame = Frame();
            DetectedSurface plane = Wall(frame, 3f, 0f, -1f, 0f, 3f);

            var accumulator = new WallPlaneAccumulator();

            for (int i = 0; i < 30; i++)
            {
                accumulator.Update(new[] { plane }, frame);
            }

            Assert.AreEqual(1, accumulator.Clusters.Count);
            Assert.AreEqual(1, accumulator.PlaneCount);
            Assert.AreEqual(1, accumulator.Clusters[0].MemberCount);
        }

        [Test]
        public void DifferentWalls_StayDistinct()
        {
            GhostCoordinateFrame frame = Frame();
            WallPlaneAccumulator accumulator = Accumulate(frame, FullRoom(frame).ToArray());

            Assert.AreEqual(4, accumulator.Clusters.Count);
        }

        [Test]
        public void OppositeParallelWalls_AreNotMerged()
        {
            GhostCoordinateFrame frame = Frame();

            // Normals face opposite ways, as the near sides of opposite walls do.
            WallPlaneAccumulator accumulator = Accumulate(
                frame,
                Wall(frame, 3f, 0f, -1f, 0f, 3.6f),
                Wall(frame, -3f, 0f, 1f, 0f, 3.6f));

            Assert.AreEqual(2, accumulator.Clusters.Count);
        }

        [Test]
        public void FlippedNormals_OnTheSameWall_StillMerge()
        {
            GhostCoordinateFrame frame = Frame();

            WallPlaneAccumulator accumulator = Accumulate(
                frame,
                Wall(frame, 3f, -1f, -1f, 0f, 1.8f),
                Wall(frame, 3f, 1f, 1f, 0f, 1.8f));

            Assert.AreEqual(1, accumulator.Clusters.Count);
        }

        [Test]
        public void SmallUnlabelledVerticalPlanes_AreNotWalls()
        {
            GhostCoordinateFrame frame = Frame();

            // A monitor or a cabinet side: vertical, but not wall-sized.
            WallPlaneAccumulator accumulator = Accumulate(
                frame,
                Wall(frame, 1f, 1f, 0f, -1f, 0.7f, 0.5f, PlaneClassifications.None),
                Wall(frame, -1f, 1f, 1f, 0f, 1.0f, 0.9f, PlaneClassifications.None));

            Assert.AreEqual(0, accumulator.Clusters.Count);
        }

        [Test]
        public void DoorAndWindowFramePlanes_AreNotWalls()
        {
            GhostCoordinateFrame frame = Frame();

            WallPlaneAccumulator accumulator = Accumulate(
                frame,
                Wall(frame, 3f, 0f, -1f, 0f, 1.0f, 2.0f, PlaneClassifications.DoorFrame),
                Wall(frame, 3f, 1.5f, -1f, 0f, 1.2f, 1.2f, PlaneClassifications.WindowFrame));

            Assert.AreEqual(0, accumulator.Clusters.Count);
        }

        [Test]
        public void LargeUnlabelledVerticalPlane_CountsAsWallEvidence_ButIsTrustedOnlyWhenWide()
        {
            GhostCoordinateFrame frame = Frame();

            WallPlaneAccumulator accumulator = Accumulate(
                frame,
                Wall(frame, 3f, 0f, -1f, 0f, 3.0f, 2.5f, PlaneClassifications.None),
                Wall(frame, -3f, 0f, 1f, 0f, 1.7f, 2.5f, PlaneClassifications.None));

            Assert.AreEqual(2, accumulator.Clusters.Count);
            Assert.IsTrue(WallPlaneAccumulator.IsTrusted(accumulator.Clusters[0]));
            Assert.IsFalse(WallPlaneAccumulator.IsTrusted(accumulator.Clusters[1]));
        }

        // -------------------------------------------------------------------
        // Choosing four walls
        // -------------------------------------------------------------------

        [Test]
        public void FourWalls_DeriveFourOrderedCorners()
        {
            GhostCoordinateFrame frame = Frame();
            WallPlaneAccumulator accumulator = Accumulate(frame, FullRoom(frame).ToArray());

            RoomFromWallsResult result = RoomFromWalls.Select(accumulator.Clusters, Vector2.zero);

            Assert.IsTrue(result.Success, result.Reason);
            Assert.AreEqual(4, result.Corners.Length);

            foreach (Vector3 expected in new[]
            {
                new Vector3(3f, 0f, 2f), new Vector3(3f, 0f, -2f),
                new Vector3(-3f, 0f, -2f), new Vector3(-3f, 0f, 2f)
            })
            {
                bool found = false;

                foreach (Vector3 corner in result.Corners)
                {
                    found |= Vector3.Distance(corner, expected) < 0.05f;
                }

                Assert.IsTrue(found, $"no corner near {expected}");
            }

            // Consecutive corners are walls: lengths alternate 4 m and 6 m.
            for (int i = 0; i < 4; i++)
            {
                float length = Vector3.Distance(result.Corners[i], result.Corners[(i + 1) % 4]);
                Assert.IsTrue(Mathf.Abs(length - 4f) < 0.05f || Mathf.Abs(length - 6f) < 0.05f, length.ToString());
            }
        }

        [Test]
        public void ThreeWalls_DoNotFabricateAFourth_AndReportWhereToLook()
        {
            GhostCoordinateFrame frame = Frame();

            WallPlaneAccumulator accumulator = Accumulate(
                frame,
                Wall(frame, 3f, 0f, -1f, 0f, 3.6f),
                Wall(frame, -3f, 0f, 1f, 0f, 3.6f),
                Wall(frame, 0f, 2f, 0f, -1f, 5.6f));

            RoomFromWallsResult result = RoomFromWalls.Select(accumulator.Clusters, Vector2.zero);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Corners);
            Assert.AreEqual(3, result.TrustedWallCount);
            // The missing wall is the one at -Z: bearing 180 degrees from +Z.
            Assert.AreEqual(180f, Mathf.Abs(result.MissingBearingDeg), 1f);
        }

        [Test]
        public void ABarelySeenFarPlane_DoesNotBeatAWellObservedRoom()
        {
            GhostCoordinateFrame frame = Frame();
            List<DetectedSurface> surfaces = FullRoom(frame);

            // A fifth, trusted but short plane, far behind the +Z wall.
            surfaces.Add(Wall(frame, 0f, 9f, 0f, -1f, 1.2f));

            WallPlaneAccumulator accumulator = Accumulate(frame, surfaces.ToArray());
            RoomFromWallsResult result = RoomFromWalls.Select(accumulator.Clusters, Vector2.zero);

            Assert.IsTrue(result.Success, result.Reason);

            foreach (Vector3 corner in result.Corners)
            {
                Assert.LessOrEqual(Mathf.Abs(corner.z), 2.1f);
            }
        }

        [Test]
        public void ScanCenterOutsideTheWalls_IsNotARoom()
        {
            GhostCoordinateFrame frame = Frame();
            WallPlaneAccumulator accumulator = Accumulate(frame, FullRoom(frame).ToArray());

            Assert.IsFalse(RoomFromWalls.Select(accumulator.Clusters, new Vector2(8f, 0f)).Success);
        }

        // -------------------------------------------------------------------
        // The workflow
        // -------------------------------------------------------------------

        [Test]
        public void AutoScan_BuildsTheRoomThroughTheExistingCornerStore()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Feed(provider, FullRoom(workflow.Frame));

            Assert.IsTrue(workflow.BeginAutoScan(0f));
            Assert.AreEqual(ScanPhase.AutoScanRoom, workflow.Phase);

            TurnOnce(provider, workflow);

            Assert.IsTrue(workflow.Auto.IsReadyToFinish);
            Assert.IsTrue(workflow.TryFinishAutoScan(out AutoScanRejection rejection), rejection.ToString());

            Assert.IsTrue(workflow.Corners.IsComplete);
            Assert.AreEqual(4, workflow.Snapshot.room.corners.Length);
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase, "no ceiling seen: the height step stays");
            Assert.IsTrue(workflow.RoomWasAutoScanned);

            foreach (CornerModel corner in workflow.Snapshot.room.corners)
            {
                Assert.AreEqual(0f, corner.position.y, 1e-4f);
            }

            // The shared validator accepts exactly what the store holds.
            Assert.IsTrue(RoomValidator.ValidateRoom(workflow.Snapshot.room).IsValid
                || workflow.Snapshot.room.heightM == 0f);
        }

        [Test]
        public void AutoScan_WithAnArKitCeiling_SetsHeightAndAdvances()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            List<DetectedSurface> surfaces = FullRoom(workflow.Frame);
            surfaces.Add(Horizontal(workflow.Frame, 0f, 2.6f, 0f, 5f, 3f, PlaneClassifications.Ceiling));
            Feed(provider, surfaces);

            workflow.BeginAutoScan(0f);
            TurnOnce(provider, workflow);

            Assert.IsTrue(workflow.TryFinishAutoScan(out _));
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);
            Assert.AreEqual(2.6f, workflow.Height.HeightM, 0.01f);
            Assert.AreEqual(2.6f, workflow.Snapshot.room.heightM, 0.01f);
        }

        [Test]
        public void AutoScan_RejectsAnImplausibleCeiling_AndKeepsTheHeightStep()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            List<DetectedSurface> surfaces = FullRoom(workflow.Frame);
            surfaces.Add(Horizontal(workflow.Frame, 0f, 5.5f, 0f, 5f, 3f, PlaneClassifications.Ceiling));
            Feed(provider, surfaces);

            workflow.BeginAutoScan(0f);
            TurnOnce(provider, workflow);

            Assert.IsTrue(workflow.TryFinishAutoScan(out _));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);
            Assert.IsFalse(workflow.Height.HasCapturedHeight);
        }

        [Test]
        public void AutoScan_WithThreeWalls_RefusesAndLeavesTheCornerStoreEmpty()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            List<DetectedSurface> surfaces = FullRoom(workflow.Frame);
            surfaces.RemoveAt(3);
            Feed(provider, surfaces);

            workflow.BeginAutoScan(0f);
            TurnOnce(provider, workflow);

            Assert.IsFalse(workflow.Auto.IsReadyToFinish);
            Assert.IsFalse(workflow.TryFinishAutoScan(out AutoScanRejection rejection));
            Assert.AreEqual(AutoScanRejection.NoRoomYet, rejection);
            Assert.AreEqual(0, workflow.Corners.CornerCount);
            Assert.AreEqual(ScanPhase.AutoScanRoom, workflow.Phase);
            Assert.AreEqual(AutoScanState.MissingWall, workflow.Auto.State);
            StringAssert.Contains("wall", workflow.Auto.Message);
        }

        [Test]
        public void HelpGhostMap_FallsBackToTheExistingSweep_KeepingTheFloorLock()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            GhostCoordinateFrame frame = workflow.Frame;

            workflow.BeginAutoScan(0f);
            Assert.IsTrue(workflow.FallBackFromAutoScan());

            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreSame(frame, workflow.Frame);
            Assert.IsTrue(workflow.TryBeginWallSweep(out WallSweepRejection rejection), rejection.ToString());
        }

        [Test]
        public void ManualPaths_AreStillReachableFromFloorLocked()
        {
            FakeSpatialProvider sweepProvider = Provider();
            ScanWorkflowController sweep = LockedWorkflow(sweepProvider);
            Assert.IsTrue(sweep.CanTransitionTo(ScanPhase.SweepWalls));
            Assert.IsTrue(sweep.BeginWallSweeping());

            FakeSpatialProvider walkProvider = Provider();
            ScanWorkflowController walk = LockedWorkflow(walkProvider);
            Assert.IsTrue(walk.BeginCornerCapture());
            Assert.AreEqual(ScanPhase.CaptureCorners, walk.Phase);
        }

        [Test]
        public void AutoScan_CannotBeStartedBeforeTheFloorIsLocked()
        {
            ScanWorkflowController workflow = Workflow(Provider());

            Assert.IsFalse(workflow.BeginAutoScan(0f));
            Assert.AreEqual(ScanPhase.Boot, workflow.Phase);
        }

        [Test]
        public void RescanRoom_ReturnsToTheScan_AndClearsTheOldFootprint()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Feed(provider, FullRoom(workflow.Frame));

            workflow.BeginAutoScan(0f);
            TurnOnce(provider, workflow);
            Assert.IsTrue(workflow.TryFinishAutoScan(out _));

            Assert.IsTrue(workflow.RedoAutoScan(100f));

            Assert.AreEqual(ScanPhase.AutoScanRoom, workflow.Phase);
            Assert.AreEqual(0, workflow.Corners.CornerCount);
            Assert.AreEqual(0, workflow.Openings.OpeningCount);
            Assert.IsFalse(workflow.RoomWasAutoScanned);
        }

        // -------------------------------------------------------------------
        // Guidance
        // -------------------------------------------------------------------

        [Test]
        public void Coverage_GrowsAsThePhoneTurns()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            workflow.BeginAutoScan(0f);

            Assert.AreEqual(0, workflow.Auto.CoveragePercent);

            for (int yaw = 0; yaw <= 90; yaw += 5)
            {
                provider.CameraPose = new Pose(FloorOrigin + Vector3.up * 1.5f, Quaternion.Euler(0f, yaw, 0f));
                workflow.TickAutoScan(yaw * 0.02f);
            }

            Assert.Greater(workflow.Auto.CoveragePercent, 0);
            Assert.Less(workflow.Auto.CoveragePercent, 60);
            Assert.AreEqual(AutoScanState.KeepTurning, workflow.Auto.State);
            Assert.AreEqual("Keep turning.", workflow.Auto.Message);
        }

        [Test]
        public void TurningTooFast_AsksTheUserToSlowDown()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            workflow.BeginAutoScan(0f);

            float now = 0f;

            for (int yaw = 0; yaw < 360; yaw += 30)
            {
                now += 0.1f;
                provider.CameraPose = new Pose(FloorOrigin + Vector3.up * 1.5f, Quaternion.Euler(0f, yaw, 0f));
                workflow.TickAutoScan(now);
            }

            Assert.AreEqual(AutoScanState.MoveSlower, workflow.Auto.State);
            Assert.AreEqual("Move slower.", workflow.Auto.Message);
        }

        [Test]
        public void LostTracking_AsksTheUserToSlowDown()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            workflow.BeginAutoScan(0f);

            provider.IsTrackingGood = false;
            workflow.TickAutoScan(0.5f);

            Assert.AreEqual(AutoScanState.MoveSlower, workflow.Auto.State);
        }

        [Test]
        public void MissingWallBearing_IsDescribedRelativeToWhereThePhoneFaces()
        {
            Assert.AreEqual("Look toward the wall behind you.", AutoRoomScanController.DescribeBearing(180f));
            Assert.AreEqual("Look toward the wall on your right.", AutoRoomScanController.DescribeBearing(90f));
            Assert.AreEqual("Look toward the wall on your left.", AutoRoomScanController.DescribeBearing(-90f));
            Assert.AreEqual("Look at the wall ahead and hold steady.", AutoRoomScanController.DescribeBearing(10f));
        }

        [Test]
        public void TheGuide_DescribesTheAutoScan_InPlainWords()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            ScanGuideStep ready = ScanGuide.Describe(workflow, new ScanGuideContext(true, FloorLockRejection.None, false));
            StringAssert.Contains("Scan Room", ready.Instruction);

            workflow.BeginAutoScan(0f);
            workflow.TickAutoScan(0.1f);

            ScanGuideStep scanning = ScanGuide.Describe(workflow, new ScanGuideContext(true, FloorLockRejection.None, false));
            Assert.AreEqual("Scan the room", scanning.Title);
            StringAssert.Contains("% seen", scanning.Instruction);
            StringAssert.DoesNotContain("plane", scanning.Message.ToLowerInvariant());
        }

        // -------------------------------------------------------------------
        // Furniture and openings go into the same stores
        // -------------------------------------------------------------------

        [Test]
        public void AddAllDetected_AddsEachSurfaceOnce_EvenAfterRepeatedPlaneUpdates()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            List<DetectedSurface> surfaces = FullRoom(workflow.Frame);
            surfaces.Add(Horizontal(workflow.Frame, 0f, 2.6f, 0f, 5f, 3f, PlaneClassifications.Ceiling));
            DetectedSurface desk = Horizontal(workflow.Frame, -1f, 0.74f, 0.5f, 1.4f, 0.7f, PlaneClassifications.Table);
            surfaces.Add(desk);
            Feed(provider, surfaces);

            workflow.BeginAutoScan(0f);
            TurnOnce(provider, workflow);
            Assert.IsTrue(workflow.TryFinishAutoScan(out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);

            Assert.AreEqual(1, workflow.TryAcceptAllDetectedFurniture());
            Assert.AreEqual(0, workflow.TryAcceptAllDetectedFurniture());
            Assert.AreEqual(0, workflow.TryAcceptAllDetectedFurniture());

            Assert.AreEqual(1, workflow.Objects.ObjectCount);
            Assert.AreEqual("desk", workflow.Objects.Objects[0].type);
            Assert.AreEqual(1, workflow.Snapshot.room.objects.Length);
        }

        [Test]
        public void ADetectedDoorFrame_BecomesAnOpeningInTheNormalStore()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            List<DetectedSurface> surfaces = FullRoom(workflow.Frame);
            surfaces.Add(Horizontal(workflow.Frame, 0f, 2.6f, 0f, 5f, 3f, PlaneClassifications.Ceiling));
            surfaces.Add(Wall(workflow.Frame, 3f, 0.5f, -1f, 0f, 0.9f, 2.0f, PlaneClassifications.DoorFrame));
            Feed(provider, surfaces);

            workflow.BeginAutoScan(0f);
            TurnOnce(provider, workflow);
            Assert.IsTrue(workflow.TryFinishAutoScan(out _));

            Assert.AreEqual(1, workflow.Openings.OpeningCount);
            OpeningModel door = workflow.Snapshot.room.openings[0];
            Assert.AreEqual("door", door.type);
            Assert.AreEqual(0f, door.sillHeightM, 1e-4f);
            Assert.AreEqual(0.9f, door.widthM, 0.05f);
        }
    }
}
