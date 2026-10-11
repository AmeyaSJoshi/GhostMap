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
    /// The seam between <c>ADR-0005</c> wall sweeping and <c>ADR-0006</c>
    /// furniture detection, which were developed on separate branches and merged.
    ///
    /// <para><b>Why this file exists.</b> Detection's "is this surface inside the
    /// room" gate reads the corner store, and <see cref="RoomGeometry.ContainsPointXZ"/>
    /// needs a complete footprint. On the swept path corners are never captured
    /// directly — they are <em>derived</em> by intersecting fitted wall lines and
    /// then installed through
    /// <see cref="CornerCaptureController.TryAdoptDerivedCorners"/>. So detection
    /// depends on a store that the swept path fills by a completely different
    /// route than the walked one, and nothing in either branch's own tests
    /// exercises that combination.</para>
    ///
    /// <para>It works because both features were deliberately built around the
    /// same single corner store and the same single object store. These tests are
    /// what make that an asserted fact rather than a design intention.</para>
    /// </summary>
    public sealed class ScanWorkflowSweepAndDetectionTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);

        private const float YawDeg = 40f;

        private static ulong nextSurfaceId = 9000;

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
            var wallSweep = new WallSweepController(provider, floorLock);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var openingCapture = new OpeningCaptureController(provider, floorLock, corners, height);
            var objectPlacement = new ObjectPlacementController(provider, floorLock);
            var furnitureDetection = new FurnitureDetectionController(provider, floorLock, corners);

            return new ScanWorkflowController(
                floorLock, wallSweep, corners, height, openingCapture, objectPlacement,
                furnitureDetection);
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

        private static void SweepWall(
            FakeSpatialProvider provider,
            ScanWorkflowController workflow,
            Vector2 from,
            Vector2 to,
            int samples = 20)
        {
            Assert.IsTrue(workflow.TryBeginWallSweep(out WallSweepRejection begin), begin.ToString());

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / (samples - 1);

                AimAtGhost(
                    provider,
                    workflow.Frame,
                    Mathf.Lerp(from.x, to.x, t),
                    Mathf.Lerp(from.y, to.y, t));

                workflow.TryAddWallSweepSample(out _);
            }

            Assert.IsTrue(
                workflow.TryCompleteWallSweep(out WallSweepRejection complete),
                $"{complete}: {workflow.WallSweep.LastError}");
        }

        /// <summary>
        /// Locks the floor, sweeps a 4.0 m x 3.0 m room, derives its corners and
        /// verifies closure — i.e. produces a complete footprint via ADR-0005
        /// without a single corner ever having been aimed at.
        /// </summary>
        private static ScanWorkflowController SweptRoom(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = Workflow(provider);

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out FloorLockRejection floorRejection),
                floorRejection.ToString());
            Assert.IsTrue(workflow.BeginWallSweeping());

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, workflow, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));
            SweepWall(provider, workflow, new Vector2(0.2f, 3f), new Vector2(3.8f, 3f));
            SweepWall(provider, workflow, new Vector2(0f, 0.2f), new Vector2(0f, 2.8f));

            Assert.IsTrue(
                workflow.TryDeriveRoomFromSweeps(
                    out WallSweepRejection sweepRejection,
                    out CornerCaptureRejection cornerRejection),
                $"{sweepRejection} / {cornerRejection}: {workflow.Corners.LastError}");

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);

            return workflow;
        }

        /// <summary>Carries a swept room through to AddObjects, where detection runs.</summary>
        private static ScanWorkflowController SweptRoomAtAddObjects(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = SweptRoom(provider);

            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out HeightCaptureRejection rejection),
                rejection.ToString());
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);

            return workflow;
        }

        private static DetectedSurface Surface(
            GhostCoordinateFrame frame,
            float ghostX,
            float ghostZ,
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
        // The combined happy path
        // -------------------------------------------------------------------

        /// <summary>
        /// Both features in one session: a room captured by standing and turning,
        /// and furniture found and measured rather than typed in. Neither a
        /// corner nor a furniture dimension is entered by hand anywhere in this
        /// test.
        /// </summary>
        [Test]
        public void SweptRoomAndDetectedFurniture_CompleteOneScanTogether()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoomAtAddObjects(provider);

            provider.DetectedSurfaces.Add(Surface(
                workflow.Frame, 2f, 1.5f, ghostY: 0.73f, widthM: 1.4f, depthM: 0.7f));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out FurnitureDetectionRejection refresh),
                refresh.ToString());
            Assert.AreEqual(1, workflow.FurnitureDetection.CandidateCount);

            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(
                workflow.TryAcceptDetectedFurniture(out _, out ObjectPlacementRejection placement),
                $"{placement}: {workflow.Objects.LastError}");

            Assert.IsTrue(workflow.FinishAddingObjects());
            Assert.IsTrue(workflow.TryFinalize(out FinalizeRejection finalize), finalize.ToString());

            SceneSnapshot snapshot = workflow.Snapshot;

            Assert.IsTrue(snapshot.finalized);
            Assert.AreEqual("Finalized", snapshot.scanPhase);

            // Four corners, from sweeping.
            Assert.AreEqual(4, snapshot.room.corners.Length);
            Assert.AreEqual(0f, snapshot.room.corners[0].position.x, Tolerance);
            Assert.AreEqual(0f, snapshot.room.corners[0].position.z, Tolerance);
            Assert.AreEqual(4f, snapshot.room.corners[2].position.x, Tolerance);
            Assert.AreEqual(3f, snapshot.room.corners[2].position.z, Tolerance);

            // One object, from detection, with its measured dimensions.
            Assert.AreEqual(1, snapshot.room.objects.Length);
            Assert.AreEqual("desk", snapshot.room.objects[0].type);
            Assert.AreEqual(1.4f, snapshot.room.objects[0].widthM, Tolerance);
            Assert.AreEqual(0.73f, snapshot.room.objects[0].heightM, Tolerance);

            // And the whole thing is a legal room by the unchanged shared rules.
            ValidationResult result = RoomValidator.ValidateRoom(snapshot.room);
            Assert.IsTrue(result.IsValid, result.Error);

            ValidationResult furniture = FurnitureValidator.Validate(snapshot.room.objects[0]);
            Assert.IsTrue(furniture.IsValid, furniture.Error);
        }

        // -------------------------------------------------------------------
        // The actual integration risk: the in-room gate on a derived footprint
        // -------------------------------------------------------------------

        /// <summary>
        /// The gate that could have silently broken on merge. Detection rejects
        /// a surface outside the room by testing it against the corner store —
        /// and on the swept path that store was filled by
        /// <c>TryAdoptDerivedCorners</c>, from line intersections, not by any
        /// corner the user aimed at.
        /// </summary>
        [Test]
        public void DetectionInRoomGate_WorksAgainstASweptFootprint()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoomAtAddObjects(provider);

            // Inside the swept 4 x 3 room, and well outside it.
            provider.DetectedSurfaces.Add(Surface(workflow.Frame, 2f, 1.5f));
            provider.DetectedSurfaces.Add(Surface(workflow.Frame, 9f, 1.5f));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            Assert.AreEqual(2, workflow.FurnitureDetection.LastSurfaceCount);
            Assert.AreEqual(
                1,
                workflow.FurnitureDetection.CandidateCount,
                "the surface outside the swept footprint must be rejected");

            FurnitureCandidate accepted = workflow.FurnitureDetection.Candidates[0];
            Assert.AreEqual(2f, accepted.CenterGhost.x, Tolerance);
        }

        /// <summary>
        /// The derived corners really are the footprint detection tests against,
        /// asserted directly rather than inferred from the gate's behaviour.
        /// </summary>
        [Test]
        public void SweptCorners_PopulateTheStoreDetectionReads()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoomAtAddObjects(provider);

            Assert.IsTrue(workflow.Corners.IsComplete, "detection requires a complete footprint");
            Assert.AreEqual(4, workflow.Corners.CornerCount);
            Assert.IsTrue(workflow.FurnitureDetection.CanDetect);

            // Not a single corner was captured from the crosshair in this flow.
            Assert.AreEqual(4, workflow.WallSweep.WallCount);
        }

        [Test]
        public void Detection_IsRefusedWhileStillSweeping()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = Workflow(provider);

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Assert.IsTrue(workflow.BeginWallSweeping());

            provider.DetectedSurfaces.Add(Surface(workflow.Frame, 2f, 1.5f));

            // Wrong phase, and there is no footprint to test containment against.
            Assert.IsFalse(workflow.TryRefreshFurnitureDetection(out _));
            Assert.IsFalse(workflow.FurnitureDetection.CanDetect);
            Assert.AreEqual(0, workflow.FurnitureDetection.CandidateCount);
        }

        // -------------------------------------------------------------------
        // The walked fallback also reaches detection
        // -------------------------------------------------------------------

        /// <summary>
        /// ADR-0005 keeps the walked path as a fallback. It must arrive at
        /// detection in exactly the same state the swept path does, or the
        /// fallback is only half a fallback.
        /// </summary>
        [Test]
        public void WalkedFallback_AlsoReachesWorkingDetection()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = Workflow(provider);

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Assert.IsTrue(workflow.BeginWallSweeping());

            // Give up on sweeping and walk the corners instead.
            Assert.IsTrue(workflow.FallBackToWalkedCorners());
            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);

            var walked = new[]
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f),
                new Vector2(4f, 3f), new Vector2(0f, 3f)
            };

            for (int i = 0; i < walked.Length; i++)
            {
                AimAtGhost(provider, workflow.Frame, walked[i].x, walked[i].y);
                Assert.IsTrue(
                    workflow.TryCaptureCorner(out CornerCaptureRejection rejection),
                    $"corner {i}: {rejection} {workflow.Corners.LastError}");
            }

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);

            provider.DetectedSurfaces.Add(Surface(workflow.Frame, 2f, 1.5f));
            provider.DetectedSurfaces.Add(Surface(workflow.Frame, 9f, 1.5f));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            Assert.AreEqual(
                1,
                workflow.FurnitureDetection.CandidateCount,
                "the in-room gate must behave identically on a walked footprint");

            Assert.IsTrue(workflow.SetDetectedFurnitureType("table"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));
            Assert.AreEqual(1, workflow.Snapshot.room.objects.Length);
        }

        // -------------------------------------------------------------------
        // Revisions stay monotonic across both features
        // -------------------------------------------------------------------

        /// <summary>
        /// Protocol v1 requires a strictly monotonic revision per session, and
        /// the viewer is contractually obliged to drop anything that is not. Two
        /// features now publish into the same counter.
        /// </summary>
        [Test]
        public void Revision_IsStrictlyMonotonicAcrossSweepingAndDetecting()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = Workflow(provider);

            int previous = workflow.Revision;

            void Advance(string what)
            {
                Assert.Greater(workflow.Revision, previous, $"revision must advance on {what}");
                previous = workflow.Revision;
            }

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Advance("floor lock");

            Assert.IsTrue(workflow.BeginWallSweeping());
            Advance("entering sweep");

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            Advance("wall 1");
            SweepWall(provider, workflow, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));
            Advance("wall 2");
            SweepWall(provider, workflow, new Vector2(0.2f, 3f), new Vector2(3.8f, 3f));
            Advance("wall 3");
            SweepWall(provider, workflow, new Vector2(0f, 0.2f), new Vector2(0f, 2.8f));
            Advance("wall 4");

            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));
            Advance("deriving the room");

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Advance("closure");

            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Advance("height");

            Assert.IsTrue(workflow.FinishAddingOpenings());
            Advance("finishing openings");

            provider.DetectedSurfaces.Add(Surface(workflow.Frame, 2f, 1.5f));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            // Refreshing is not a mutation and must not publish.
            Assert.AreEqual(previous, workflow.Revision, "a detection refresh must not publish");

            Assert.IsTrue(workflow.SetDetectedFurnitureType("desk"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));
            Advance("accepting detected furniture");

            Assert.IsTrue(workflow.FinishAddingObjects());
            Advance("finishing objects");

            Assert.IsTrue(workflow.TryFinalize(out _));
            Advance("finalization");

            Assert.AreEqual(previous, workflow.Snapshot.revision);
        }

        // -------------------------------------------------------------------
        // Re-sweeping a room detection has already seen
        // -------------------------------------------------------------------

        /// <summary>
        /// <c>RedoWallSweeps</c> clears the corner store, which is the footprint
        /// detection tests against. Detection must then refuse rather than
        /// silently using a footprint that no longer exists.
        /// </summary>
        [Test]
        public void RedoingSweeps_LeavesDetectionWithoutAStaleFootprint()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoom(provider);

            Assert.IsTrue(workflow.Corners.IsComplete);

            // Back out to re-measure the room.
            Assert.IsTrue(workflow.RedoWallSweeps());
            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.IsFalse(workflow.Corners.IsComplete);

            Assert.IsFalse(workflow.FurnitureDetection.CanDetect);
            Assert.IsFalse(workflow.TryRefreshFurnitureDetection(out _));
            Assert.AreEqual(0, workflow.FurnitureDetection.CandidateCount);
        }

        /// <summary>
        /// And the second sweep's footprint is the one detection then uses — a
        /// re-measured room must gate against its own corners, not the discarded
        /// ones.
        /// </summary>
        [Test]
        public void ReSweptRoom_GatesDetectionAgainstTheNewFootprint()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweptRoom(provider);

            Assert.IsTrue(workflow.RedoWallSweeps());

            // Re-sweep as a deliberately smaller room: 2.0 m x 2.0 m.
            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(1.8f, 0f));
            SweepWall(provider, workflow, new Vector2(2f, 0.2f), new Vector2(2f, 1.8f));
            SweepWall(provider, workflow, new Vector2(0.2f, 2f), new Vector2(1.8f, 2f));
            SweepWall(provider, workflow, new Vector2(0f, 0.2f), new Vector2(0f, 1.8f));

            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out CornerCaptureRejection corner),
                $"{corner}: {workflow.Corners.LastError}");

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());

            // (3, 1.5) was inside the original 4 x 3 room but is outside the
            // re-swept 2 x 2 one.
            provider.DetectedSurfaces.Add(Surface(workflow.Frame, 3f, 1.5f));
            provider.DetectedSurfaces.Add(Surface(
                workflow.Frame, 1f, 1f, widthM: 0.8f, depthM: 0.8f));

            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            Assert.AreEqual(2, workflow.FurnitureDetection.LastSurfaceCount);
            Assert.AreEqual(
                1,
                workflow.FurnitureDetection.CandidateCount,
                "containment must use the re-swept footprint, not the discarded one");
            Assert.AreEqual(1f, workflow.FurnitureDetection.Candidates[0].CenterGhost.x, Tolerance);
        }

        // -------------------------------------------------------------------
        // Both HUD-facing controllers are reachable from one workflow
        // -------------------------------------------------------------------

        /// <summary>
        /// The merged constructor wires seven collaborators. Both new ones must
        /// be the live instances, not nulls that would only fail on a phone.
        /// </summary>
        [Test]
        public void MergedWorkflow_ExposesBothNewControllers()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = Workflow(provider);

            Assert.IsNotNull(workflow.WallSweep, "ADR-0005 sweep controller");
            Assert.IsNotNull(workflow.FurnitureDetection, "ADR-0006 detection controller");
            Assert.IsNotNull(workflow.Corners);
            Assert.IsNotNull(workflow.Objects);

            // And they share the frame, so neither can be looking at a different
            // room than the other.
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            Assert.AreSame(workflow.Frame, workflow.WallSweep.Frame);
            Assert.AreSame(workflow.Frame, workflow.FurnitureDetection.Frame);
            Assert.AreSame(workflow.Frame, workflow.Corners.Frame);
        }
    }
}
