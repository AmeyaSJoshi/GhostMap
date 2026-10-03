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
    /// ADR-0005 at the workflow level: the phase transitions the swept path
    /// adds, what it publishes and when, how it hands corners to the single
    /// corner store, and the walked-corner fallback.
    ///
    /// <para>The existing <see cref="ScanWorkflowControllerTests"/> covers the
    /// walked path and is deliberately untouched: the transition table changed
    /// additively, so every path it asserts still exists.</para>
    /// </summary>
    public sealed class ScanWorkflowSweepTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);

        private const float YawDeg = 40f;

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

        /// <summary>Ticks to FindFloor and locks, leaving the phase at FloorLocked.</summary>
        private static ScanWorkflowController LockedWorkflow(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = Workflow(provider);
            workflow.Tick(true);

            Assert.IsTrue(
                workflow.TryLockFloor(out FloorLockRejection rejection),
                $"The fixture's own floor lock should succeed, got {rejection}.");
            Assert.AreEqual(ScanPhase.FloorLocked, workflow.Phase);

            return workflow;
        }

        /// <summary>Locked, then moved into the swept capture phase.</summary>
        private static ScanWorkflowController SweepingWorkflow(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = LockedWorkflow(provider);

            Assert.IsTrue(workflow.BeginWallSweeping());
            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);

            return workflow;
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
            Assert.IsTrue(
                workflow.TryBeginWallSweep(out WallSweepRejection begin),
                $"Sweep should have started, got {begin}.");

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
                $"Sweep {from} -> {to} should have been accepted, got " +
                $"{complete}: {workflow.WallSweep.LastError}");
        }

        /// <summary>Sweeps the four walls of a legal 4.0 m x 3.0 m room.</summary>
        private static void SweepLegalRoom(
            FakeSpatialProvider provider,
            ScanWorkflowController workflow)
        {
            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, workflow, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));
            SweepWall(provider, workflow, new Vector2(0.2f, 3f), new Vector2(3.8f, 3f));
            SweepWall(provider, workflow, new Vector2(0f, 0.2f), new Vector2(0f, 2.8f));
        }

        /// <summary>Sweeps a 0.6 m x 0.6 m footprint: four clean fits, far too small to be a room.</summary>
        private static void SweepCupboard(
            FakeSpatialProvider provider,
            ScanWorkflowController workflow)
        {
            SweepWall(provider, workflow, new Vector2(0f, 0f), new Vector2(0.6f, 0f));
            SweepWall(provider, workflow, new Vector2(0.6f, 0f), new Vector2(0.6f, 0.6f));
            SweepWall(provider, workflow, new Vector2(0f, 0.6f), new Vector2(0.6f, 0.6f));
            SweepWall(provider, workflow, new Vector2(0f, 0f), new Vector2(0f, 0.6f));
        }

        // -------------------------------------------------------------------
        // Entering the swept path
        // -------------------------------------------------------------------

        [Test]
        public void BeginWallSweeping_IsTheDefaultRouteOutOfFloorLocked()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            int before = workflow.Revision;

            Assert.IsTrue(workflow.BeginWallSweeping());
            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreEqual(before + 1, workflow.Revision);
            Assert.AreEqual("SweepWalls", workflow.Snapshot.scanPhase);
        }

        [Test]
        public void BeginWallSweeping_RefusedBeforeFloorLock()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = Workflow(provider);

            workflow.Tick(true);
            Assert.AreEqual(ScanPhase.FindFloor, workflow.Phase);

            Assert.IsFalse(workflow.BeginWallSweeping());
            Assert.AreEqual(ScanPhase.FindFloor, workflow.Phase);
        }

        /// <summary>
        /// The walked path remains reachable directly from FloorLocked, so every
        /// Task S3 test's route still exists.
        /// </summary>
        [Test]
        public void BeginCornerCapture_StillWorksFromFloorLocked()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            Assert.IsTrue(workflow.BeginCornerCapture());
            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);
        }

        // -------------------------------------------------------------------
        // What publishes, and what must not
        // -------------------------------------------------------------------

        /// <summary>
        /// Performance target section 24 forbids per-frame snapshot generation.
        /// A sweep runs for seconds at frame rate, so this is the one that would
        /// hurt.
        /// </summary>
        [Test]
        public void SweepSampling_PublishesNothing()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            Assert.IsTrue(workflow.TryBeginWallSweep(out _));

            int before = workflow.Revision;

            for (int i = 0; i < 40; i++)
            {
                AimAtGhost(provider, workflow.Frame, 0.2f + (0.09f * i), 0f);
                workflow.TryAddWallSweepSample(out _);
            }

            Assert.Greater(workflow.WallSweep.ActiveSampleCount, 8);
            Assert.AreEqual(before, workflow.Revision);
        }

        [Test]
        public void BeginningASweep_PublishesNothing()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            int before = workflow.Revision;

            Assert.IsTrue(workflow.TryBeginWallSweep(out _));
            Assert.AreEqual(before, workflow.Revision);
        }

        [Test]
        public void CancellingASweep_PublishesNothing()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            Assert.IsTrue(workflow.TryBeginWallSweep(out _));
            int before = workflow.Revision;

            Assert.IsTrue(workflow.CancelWallSweep());
            Assert.AreEqual(before, workflow.Revision);
            Assert.AreEqual(0, workflow.WallSweep.WallCount);
        }

        [Test]
        public void EachAcceptedWall_IncrementsTheRevisionExactlyOnce()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            int baseline = workflow.Revision;

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            Assert.AreEqual(baseline + 1, workflow.Revision);

            SweepWall(provider, workflow, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));
            Assert.AreEqual(baseline + 2, workflow.Revision);
        }

        [Test]
        public void ARefusedSweep_PublishesNothing()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            Assert.IsTrue(workflow.TryBeginWallSweep(out _));

            int before = workflow.Revision;

            // Four distinct samples: too few to fit.
            for (int i = 0; i < 4; i++)
            {
                AimAtGhost(provider, workflow.Frame, 0.5f * i, 0f);
                workflow.TryAddWallSweepSample(out _);
            }

            Assert.IsFalse(workflow.TryCompleteWallSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.FitRejected, rejection);
            Assert.AreEqual(before, workflow.Revision);
            Assert.AreEqual(0, workflow.WallSweep.WallCount);
        }

        [Test]
        public void UndoingAWall_PublishesAndNeverWindsTheRevisionBack()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            int afterWall = workflow.Revision;

            Assert.IsTrue(workflow.TryUndoLastWall(out _));
            Assert.AreEqual(0, workflow.WallSweep.WallCount);
            Assert.AreEqual(afterWall + 1, workflow.Revision);
        }

        // -------------------------------------------------------------------
        // Wrong-phase gates
        // -------------------------------------------------------------------

        [Test]
        public void SweepOperations_AreRefusedOutsideTheSweepPhase()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            Assert.IsFalse(workflow.TryBeginWallSweep(out WallSweepRejection begin));
            Assert.AreEqual(WallSweepRejection.WrongPhase, begin);

            Assert.IsFalse(workflow.TryAddWallSweepSample(out WallSweepRejection sample));
            Assert.AreEqual(WallSweepRejection.WrongPhase, sample);

            Assert.IsFalse(workflow.TryCompleteWallSweep(out WallSweepRejection complete));
            Assert.AreEqual(WallSweepRejection.WrongPhase, complete);

            Assert.IsFalse(workflow.TryUndoLastWall(out WallSweepRejection undo));
            Assert.AreEqual(WallSweepRejection.WrongPhase, undo);

            Assert.IsFalse(workflow.TryDeriveRoomFromSweeps(out WallSweepRejection derive, out _));
            Assert.AreEqual(WallSweepRejection.WrongPhase, derive);

            Assert.IsFalse(workflow.CancelWallSweep());
        }

        // -------------------------------------------------------------------
        // Deriving the room
        // -------------------------------------------------------------------

        [Test]
        public void DerivingTheRoom_InstallsFourCornersAndAdvancesToClosure()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);

            int before = workflow.Revision;

            Assert.IsTrue(
                workflow.TryDeriveRoomFromSweeps(
                    out WallSweepRejection sweepRejection,
                    out CornerCaptureRejection cornerRejection),
                $"{sweepRejection} / {cornerRejection}: {workflow.Corners.LastError}");

            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);
            Assert.AreEqual(before + 1, workflow.Revision);
            Assert.AreEqual("VerifyClosure", workflow.Snapshot.scanPhase);

            CornerModel[] published = workflow.Snapshot.room.corners;
            Assert.AreEqual(4, published.Length);

            Assert.AreEqual(0f, published[0].position.x, Tolerance);
            Assert.AreEqual(0f, published[0].position.z, Tolerance);
            Assert.AreEqual(4f, published[1].position.x, Tolerance);
            Assert.AreEqual(0f, published[1].position.z, Tolerance);
            Assert.AreEqual(4f, published[2].position.x, Tolerance);
            Assert.AreEqual(3f, published[2].position.z, Tolerance);
            Assert.AreEqual(0f, published[3].position.x, Tolerance);
            Assert.AreEqual(3f, published[3].position.z, Tolerance);

            for (int i = 0; i < published.Length; i++)
            {
                Assert.AreEqual(0f, published[i].position.y, 1e-6f, $"corner {i} y");
                Assert.IsNotEmpty(published[i].id);
            }
        }

        [Test]
        public void DerivingTheRoom_RefusedBeforeFourWalls()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));

            int before = workflow.Revision;

            Assert.IsFalse(workflow.TryDeriveRoomFromSweeps(
                out WallSweepRejection rejection, out _));
            Assert.AreEqual(WallSweepRejection.NotFourWalls, rejection);
            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreEqual(before, workflow.Revision);
        }

        /// <summary>
        /// A failed derivation must leave every swept wall intact, so the user
        /// can undo and re-sweep the bad one rather than starting the room
        /// again.
        /// </summary>
        [Test]
        public void FailedDerivation_KeepsEverySweptWall()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, workflow, new Vector2(0.2f, 1f), new Vector2(3.8f, 1f));
            SweepWall(provider, workflow, new Vector2(0.2f, 2f), new Vector2(3.8f, 2f));
            SweepWall(provider, workflow, new Vector2(0.2f, 3f), new Vector2(3.8f, 3f));

            Assert.IsFalse(workflow.TryDeriveRoomFromSweeps(
                out WallSweepRejection rejection, out _));
            Assert.AreEqual(WallSweepRejection.DerivationFailed, rejection);

            Assert.AreEqual(4, workflow.WallSweep.WallCount);
            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreEqual(0, workflow.Snapshot.room.corners.Length);
        }

        /// <summary>
        /// Sweeping is not a licence to skip room rules. Four clean fits over a
        /// cupboard-sized footprint are refused by the unchanged shared
        /// validator, and the scan does not advance.
        /// </summary>
        [Test]
        public void DerivingTheRoom_IsBlockedByTheSharedRoomValidator()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepCupboard(provider, workflow);
            Assert.AreEqual(4, workflow.WallSweep.WallCount);

            int before = workflow.Revision;

            Assert.IsFalse(workflow.TryDeriveRoomFromSweeps(
                out WallSweepRejection sweepRejection,
                out CornerCaptureRejection cornerRejection));

            // The geometry was fine; the room was not.
            Assert.AreEqual(WallSweepRejection.None, sweepRejection);
            Assert.AreEqual(CornerCaptureRejection.ValidationFailed, cornerRejection);
            Assert.IsNotEmpty(workflow.Corners.LastError);

            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreEqual(before, workflow.Revision);
            Assert.AreEqual(0, workflow.Snapshot.room.corners.Length);
        }

        // -------------------------------------------------------------------
        // Closure verification after a sweep
        // -------------------------------------------------------------------

        /// <summary>
        /// ADR-0005 keeps VerifyClosure deliberately. Under the swept path it
        /// compares an independent direct observation of a physical corner
        /// against a corner derived from a different set of measurements, which
        /// is a genuine accuracy check rather than a repeatability one.
        /// </summary>
        [Test]
        public void ClosureVerification_StillRunsAfterASweptRoom()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));

            // Re-aim at the first physical corner, which the sweeps never
            // touched: both walls were swept short of it.
            AimAtGhost(provider, workflow.Frame, 0f, 0f);

            Assert.IsTrue(workflow.TryVerifyClosure(
                out ClosureQuality quality, out CornerCaptureRejection rejection),
                $"{rejection}: {workflow.Corners.LastError}");

            Assert.AreEqual(ClosureQuality.Excellent, quality);
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);
            Assert.Less(workflow.Snapshot.closureErrorM, RoomValidator.ClosureExcellentM);
        }

        /// <summary>
        /// A derived footprint is self-consistent, so closure error would be
        /// zero by construction if it were measured against the walls that
        /// produced it. It is not: aiming 30 cm off the real corner reports
        /// 30 cm and rejects the scan.
        /// </summary>
        [Test]
        public void ClosureVerification_ReportsARealErrorNotZero()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));

            AimAtGhost(provider, workflow.Frame, 0.3f, 0f);

            Assert.IsTrue(workflow.TryVerifyClosure(out ClosureQuality quality, out _));

            Assert.AreEqual(ClosureQuality.Rejected, quality);
            Assert.AreEqual(0.3f, workflow.Snapshot.closureErrorM, 1e-2f);
            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);
        }

        // -------------------------------------------------------------------
        // Redo and fallback
        // -------------------------------------------------------------------

        [Test]
        public void RedoWallSweeps_DiscardsWallsAndCornersAndReturnsToSweeping()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));
            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);

            GhostCoordinateFrame frame = workflow.Frame;

            Assert.IsTrue(workflow.RedoWallSweeps());

            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreEqual(0, workflow.WallSweep.WallCount);
            Assert.AreEqual(0, workflow.Snapshot.room.corners.Length);

            // The room is re-measured, not re-anchored.
            Assert.AreSame(frame, workflow.Frame);
        }

        [Test]
        public void RedoWallSweeps_WorksWithinTheSweepPhaseToo()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, workflow, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));

            Assert.IsTrue(workflow.RedoWallSweeps());
            Assert.AreEqual(ScanPhase.SweepWalls, workflow.Phase);
            Assert.AreEqual(0, workflow.WallSweep.WallCount);
        }

        [Test]
        public void FallBackToWalkedCorners_SwitchesPathAndDiscardsSweptWalls()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepWall(provider, workflow, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, workflow, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));

            Assert.IsTrue(workflow.FallBackToWalkedCorners());

            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);
            Assert.AreEqual(0, workflow.WallSweep.WallCount);
            Assert.AreEqual(0, workflow.Snapshot.room.corners.Length);

            // And the walked path works normally from here.
            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryCaptureCorner(out CornerCaptureRejection rejection),
                $"{rejection}: {workflow.Corners.LastError}");
            Assert.AreEqual(1, workflow.Corners.CornerCount);
        }

        /// <summary>
        /// The two paths must never contribute corners to the same room. A
        /// fallback after a complete swept room discards that room outright.
        /// </summary>
        [Test]
        public void FallBackToWalkedCorners_RefusedOnceTheRoomIsDerived()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));

            // Past the sweep phase, so this is no longer the escape hatch.
            Assert.IsFalse(workflow.FallBackToWalkedCorners());
            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);
        }

        // -------------------------------------------------------------------
        // The rest of the scan is unaffected
        // -------------------------------------------------------------------

        /// <summary>
        /// Height capture derives its wall planes from the corner store, so it
        /// must work identically whichever path filled it. This is the main
        /// reason the swept path hands corners to
        /// <see cref="CornerCaptureController"/> instead of keeping its own.
        /// </summary>
        [Test]
        public void HeightCapture_WorksOnASweptRoom()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);

            // Four walls derived from the swept footprint are available to aim at.
            Assert.AreEqual(4, workflow.Height.Walls.Count);

            Assert.IsTrue(workflow.TrySetManualHeight(
                2.5f, out HeightCaptureRejection rejection), rejection.ToString());

            Assert.AreEqual(2.5f, workflow.Snapshot.room.heightM, Tolerance);
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);
        }

        [Test]
        public void SweptRoomSnapshot_CarriesSchemaV1Shape()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));

            SceneSnapshot snapshot = workflow.Snapshot;

            Assert.AreEqual(1, snapshot.schemaVersion);
            Assert.IsFalse(snapshot.finalized);
            Assert.IsNotNull(snapshot.room);
            Assert.IsNotNull(snapshot.room.openings);
            Assert.IsNotNull(snapshot.room.objects);
            Assert.AreEqual(0, snapshot.room.openings.Length);
            Assert.AreEqual(0, snapshot.room.objects.Length);
        }

        /// <summary>
        /// A swept room's footprint satisfies the same shared validator a walked
        /// one does, asserted on the published snapshot rather than on an
        /// intermediate.
        /// </summary>
        [Test]
        public void PublishedSweptRoom_PassesTheSharedRoomValidator()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = SweepingWorkflow(provider);

            SweepLegalRoom(provider, workflow);
            Assert.IsTrue(workflow.TryDeriveRoomFromSweeps(out _, out _));

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));

            ValidationResult result = RoomValidator.ValidateRoom(workflow.Snapshot.room);
            Assert.IsTrue(result.IsValid, result.Error);
        }
    }
}
