using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Drives a real <see cref="ScanWorkflowController"/> through a complete
    /// walked-corner scan (3.0 x 2.5 m room, 2.5 m high, one door, one bed) to
    /// ReadyToFinalize, for tests that need a genuine finished scan. Same
    /// steps as ScanWorkflowControllerTests' private helpers.
    /// </summary>
    internal static class ScanWorkflowTestRoom
    {
        private static readonly Vector2[] Corners =
        {
            new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 2.5f), new Vector2(0f, 2.5f)
        };

        public static ScanWorkflowController BuildToReadyToFinalize()
        {
            var provider = new FakeSpatialProvider
            {
                IsTrackingGood = true,
                HasCameraPose = true,
                HasFloorHit = true,
                FloorHitAlignment = PlaneAlignment.HorizontalUp,
                FloorHitWorldPosition = new Vector3(0f, -1.4f, 1.8f),
                CameraPose = new Pose(Vector3.zero, Quaternion.Euler(30f, 20f, 0f))
            };

            var floorLock = new FloorLockController(provider);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var workflow = new ScanWorkflowController(
                floorLock,
                new WallSweepController(provider, floorLock),
                corners,
                height,
                new OpeningCaptureController(provider, floorLock, corners, height),
                new ObjectPlacementController(provider, floorLock),
                new FurnitureDetectionController(provider, floorLock, corners));

            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Assert.IsTrue(workflow.BeginCornerCapture());

            foreach (Vector2 corner in Corners)
            {
                AimAtFloor(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _), workflow.Corners.LastError);
            }

            AimAtFloor(provider, workflow.Frame, 0.05f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWall(provider, workflow.Frame, 1.5f, 2.5f);
            Assert.IsTrue(workflow.TryCaptureHeight(out _));

            Assert.IsTrue(workflow.SelectOpeningWall(0));
            AimAtWall(provider, workflow.Frame, 0.5f, 0f);
            Assert.IsTrue(workflow.TryCaptureOpeningStartPoint(out _));
            AimAtWall(provider, workflow.Frame, 1.5f, 2.05f);
            Assert.IsTrue(workflow.TryCaptureOpeningEndPoint(out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());

            Assert.IsTrue(workflow.SetObjectType("bed"));
            AimAtFloor(provider, workflow.Frame, 1.5f, 1.25f);
            Assert.IsTrue(workflow.TryPlaceObject(out _));
            Assert.IsTrue(workflow.FinishAddingObjects());

            Assert.AreEqual(ScanPhase.ReadyToFinalize, workflow.Phase);
            return workflow;
        }

        internal static void AimAtFloor(FakeSpatialProvider provider, GhostCoordinateFrame frame, float x, float z)
        {
            Vector3 target = frame.GhostToWorld(new Vector3(x, 0f, z));
            Vector3 eye = frame.GhostToWorld(new Vector3(x, 1.5f, z - 1f));
            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        internal static void AimAtWall(FakeSpatialProvider provider, GhostCoordinateFrame frame, float wallX, float heightM)
        {
            Vector3 eye = frame.GhostToWorld(new Vector3(1.5f, 1.5f, 1.25f));
            Vector3 target = frame.GhostToWorld(new Vector3(wallX, heightM, 0f));
            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }
    }
}
