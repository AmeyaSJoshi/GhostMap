using System.Collections.Generic;
using System.Linq;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
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
    /// Task R9 regression: demo mode's automatic room scan never finished,
    /// because demo mode reported no AR planes at all (it asked ARKit's plane
    /// manager, which has none without ARKit). The demo room now reports its
    /// own planes; these tests run them through the real scan.
    /// </summary>
    public sealed class SimulatedPlanesTests
    {
        private static readonly Vector3 Eye = new Vector3(0.25f, 1.45f, -0.35f);

        private static FakeSpatialProvider Provider() => new FakeSpatialProvider
        {
            IsTrackingGood = true,
            HasCameraPose = true,
            HasFloorHit = true,
            HasPlaneDetection = true,
            FloorHitAlignment = PlaneAlignment.HorizontalUp,
            FloorHitWorldPosition = new Vector3(0.25f, 0f, 0.6f),
            CameraPose = new Pose(Eye, Quaternion.Euler(30f, 0f, 0f))
        };

        private static ScanWorkflowController LockedWorkflow(FakeSpatialProvider provider)
        {
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
                new FurnitureDetectionController(provider, floorLock, corners),
                new AutoRoomScanController(provider, floorLock));

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out FloorLockRejection rejection), rejection.ToString());
            return workflow;
        }

        /// <summary>A horizontal pinhole camera at the demo eye, turned to a yaw.</summary>
        private static System.Func<Vector3, Vector3> ViewAt(float yawDeg)
        {
            Quaternion inverse = Quaternion.Inverse(Quaternion.Euler(0f, yawDeg, 0f));
            return world =>
            {
                Vector3 local = inverse * (world - Eye);
                if (local.z <= 0f)
                {
                    return new Vector3(0.5f, 0.5f, local.z);
                }

                // About 62 degrees vertical and 32 horizontal (a portrait phone).
                return new Vector3(0.5f + local.x / local.z / 0.57f, 0.5f + local.y / local.z / 1.2f, local.z);
            };
        }

        [Test]
        public void APlane_IsReportedOnlyOnceTheCameraHasLookedAtIt()
        {
            var planes = new SimulatedPlanes();
            var seen = new List<DetectedSurface>();

            planes.CopySeen(seen);
            Assert.AreEqual(0, seen.Count);

            // Facing +Z: the north wall is ahead; the south wall is behind.
            planes.MarkSeen(ViewAt(0f));
            planes.CopySeen(seen);
            Assert.IsTrue(seen.Any(s => s.Alignment == PlaneAlignment.Vertical && s.WorldCenter.z > 1f), "north wall");
            Assert.IsFalse(seen.Any(s => s.Alignment == PlaneAlignment.Vertical && s.WorldCenter.z < -1f), "south wall");

            for (int yaw = 0; yaw < 360; yaw += 15)
            {
                planes.MarkSeen(ViewAt(yaw));
            }

            planes.CopySeen(seen);
            Assert.AreEqual(4, seen.Count(s => (s.Classifications & PlaneClassifications.WallFace) != 0));
        }

        [Test]
        public void TheDemoRoomsPlanes_FinishTheAutomaticScan_AtTheDemoRoomsSize()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            provider.DetectedSurfaces.AddRange(SimulatedPlanes.All());

            workflow.BeginAutoScan(0f);
            float now = 0f;
            for (int yaw = 0; yaw <= 360; yaw += 5)
            {
                now += 0.1f;
                provider.CameraPose = new Pose(Eye, Quaternion.Euler(0f, yaw, 0f));
                workflow.TickAutoScan(now);
            }

            Assert.IsTrue(workflow.Auto.IsReadyToFinish, workflow.Auto.Message);
            Assert.IsTrue(workflow.TryFinishAutoScan(out AutoScanRejection rejection), rejection.ToString());

            RoomModel room = workflow.Snapshot.room;
            Assert.AreEqual(4, room.corners.Length);
            Assert.AreEqual(SimulatedRoom.RoomHeightM, room.heightM, 0.01f, "the ceiling plane gives the height");
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);

            List<Vector3> footprint = room.corners.Select(c => c.position.ToVector3()).ToList();
            float area = RoomGeometry.PolygonAreaXZ(footprint);
            Assert.AreEqual(SimulatedRoom.RoomWidthM * SimulatedRoom.RoomDepthM, area, 0.05f);
            Assert.IsTrue(RoomValidator.ValidateRoom(room).IsValid);

            Assert.AreEqual(1, room.openings.Count(o => o.type == "door"), "the door frame becomes a door");
            Assert.AreEqual(1, room.openings.Count(o => o.type == "window"), "the window frame becomes a window");
        }

        [Test]
        public void WithoutTheCeiling_DetectedDoorsAndWindows_AreAddedOnceTheHeightIsTyped()
        {
            // Regression (found in the Simulator): labelled openings were only
            // added when ARKit also saw the ceiling, and were lost otherwise.
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            provider.DetectedSurfaces.AddRange(SimulatedPlanes.All()
                .Where(s => (s.Classifications & PlaneClassifications.Ceiling) == 0));

            workflow.BeginAutoScan(0f);
            float now = 0f;
            for (int yaw = 0; yaw <= 360; yaw += 5)
            {
                now += 0.1f;
                provider.CameraPose = new Pose(Eye, Quaternion.Euler(0f, yaw, 0f));
                workflow.TickAutoScan(now);
            }

            Assert.IsTrue(workflow.TryFinishAutoScan(out _));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase, "no ceiling seen");
            Assert.AreEqual(0, workflow.Openings.OpeningCount);

            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));

            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);
            Assert.AreEqual(2, workflow.Openings.OpeningCount, "the door and the window");
            Assert.AreEqual(2, workflow.Snapshot.room.openings.Length);
        }

        [Test]
        public void ADetectedBed_CoversTheBedsRealFootprint_NotAQuarterTurnOfIt()
        {
            // Regression (found in the Simulator): a plane's width runs along
            // its local X, but the yaw put the object's width a quarter turn
            // from it, so a 1.4 x 2.0 m bed came out 2.0 x 1.4 and through a wall.
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            provider.DetectedSurfaces.AddRange(SimulatedPlanes.All());

            workflow.BeginAutoScan(0f);
            float now = 0f;
            for (int yaw = 0; yaw <= 360; yaw += 5)
            {
                now += 0.1f;
                provider.CameraPose = new Pose(Eye, Quaternion.Euler(0f, yaw, 0f));
                workflow.TickAutoScan(now);
            }

            Assert.IsTrue(workflow.TryFinishAutoScan(out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(2, workflow.TryAcceptAllDetectedFurniture());

            SceneObjectModel bed = workflow.Snapshot.room.objects
                .OrderBy(o => Mathf.Abs(o.heightM - SimulatedLayout.Bed.max.y)).First();

            // The object's footprint, built the way every renderer and the
            // exporter build it: width along Euler(yaw) * right, depth along forward.
            Quaternion rotation = Quaternion.Euler(0f, bed.yawDeg, 0f);
            Vector3 halfWidth = rotation * Vector3.right * (bed.widthM * 0.5f);
            Vector3 halfDepth = rotation * Vector3.forward * (bed.depthM * 0.5f);
            Vector3 centerWorld = workflow.Frame.GhostToWorld(bed.center.ToVector3());
            Vector3 xAxis = workflow.Frame.GhostDirectionToWorld(halfWidth);
            Vector3 zAxis = workflow.Frame.GhostDirectionToWorld(halfDepth);
            float extentX = Mathf.Abs(xAxis.x) + Mathf.Abs(zAxis.x);
            float extentZ = Mathf.Abs(xAxis.z) + Mathf.Abs(zAxis.z);

            Assert.AreEqual(SimulatedLayout.Bed.center.x, centerWorld.x, 0.01f);
            Assert.AreEqual(SimulatedLayout.Bed.center.z, centerWorld.z, 0.01f);
            Assert.AreEqual(SimulatedLayout.Bed.extents.x, extentX, 0.01f, "half the bed's x size");
            Assert.AreEqual(SimulatedLayout.Bed.extents.z, extentZ, 0.01f, "half the bed's z size");
        }

        [Test]
        public void TheDemoFurnitureTops_AreDetectedAsSurfaces()
        {
            IReadOnlyList<DetectedSurface> all = SimulatedPlanes.All();
            List<DetectedSurface> tops = all
                .Where(s => s.Alignment == PlaneAlignment.HorizontalUp && s.WorldCenter.y > 0.2f && s.WorldCenter.y < 1.4f)
                .ToList();

            Assert.AreEqual(2, tops.Count);
            Assert.IsTrue(tops.Any(t => Mathf.Approximately(t.WorldCenter.y, SimulatedLayout.Bed.max.y)), "bed top");
            Assert.IsTrue(tops.Any(t => Mathf.Approximately(t.WorldCenter.y, SimulatedLayout.Desk.max.y)), "desk top");
        }
    }
}
