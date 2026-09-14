using System;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Protocol;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// The scan phase machine and the snapshot it publishes, as far as Task S2
    /// takes them: Boot through FloorLocked.
    /// </summary>
    public sealed class ScanWorkflowControllerTests
    {
        private static FakeSpatialProvider GoodProvider()
        {
            return new FakeSpatialProvider
            {
                IsTrackingGood = true,
                HasCameraPose = true,
                HasFloorHit = true,
                FloorHitAlignment = PlaneAlignment.HorizontalUp,
                FloorHitWorldPosition = new Vector3(0f, -1.4f, 1.8f),
                CameraPose = new Pose(new Vector3(0f, 0f, 0f), Quaternion.Euler(30f, 20f, 0f))
            };
        }

        private static ScanWorkflowController Workflow(FakeSpatialProvider provider)
        {
            var floorLock = new FloorLockController(provider);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var openingCapture = new OpeningCaptureController(provider, floorLock, corners, height);
            var objectPlacement = new ObjectPlacementController(provider, floorLock);

            return new ScanWorkflowController(floorLock, corners, height, openingCapture, objectPlacement);
        }

        /// <summary>Ticks to FindFloor and locks, leaving the phase at FloorLocked.</summary>
        private static ScanWorkflowController LockedWorkflow(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = Workflow(provider);
            workflow.Tick(true);
            workflow.Tick(true);

            Assert.IsTrue(
                workflow.TryLockFloor(out FloorLockRejection rejection),
                $"The fixture's own floor lock should succeed, got {rejection}.");

            return workflow;
        }

        /// <summary>
        /// Points the center-screen ray at a Ghost-space floor point, from an
        /// eye 1.5 m above the floor and 1 m short of the target.
        /// </summary>
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

        /// <summary>A legal 3.0 m x 2.5 m room: 7.5 m2, four right angles.</summary>
        private static readonly Vector2[] LegalRoom =
        {
            new Vector2(0f, 0f),
            new Vector2(3f, 0f),
            new Vector2(3f, 2.5f),
            new Vector2(0f, 2.5f)
        };

        /// <summary>Locks the floor, enters CaptureCorners and captures the legal room.</summary>
        private static ScanWorkflowController RoomCaptured(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Assert.IsTrue(workflow.BeginCornerCapture());

            foreach (Vector2 corner in LegalRoom)
            {
                AimAtGhost(provider, workflow.Frame, corner.x, corner.y);

                Assert.IsTrue(
                    workflow.TryCaptureCorner(out CornerCaptureRejection rejection),
                    $"Corner ({corner.x}, {corner.y}) should have been accepted, got " +
                    $"{rejection}: {workflow.Corners.LastError}");
            }

            return workflow;
        }

        [Test]
        public void StartsInBoot()
        {
            Assert.AreEqual(ScanPhase.Boot, Workflow(GoodProvider()).Phase);
        }

        [Test]
        public void BootAdvancesToWaitingForTracking()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());

            workflow.Tick(isTrackingGood: false);

            Assert.AreEqual(ScanPhase.WaitingForTracking, workflow.Phase);
        }

        [Test]
        public void WaitingForTrackingAdvancesToFindFloorOnlyWhenTrackingIsGood()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());

            workflow.Tick(isTrackingGood: false);
            Assert.AreEqual(ScanPhase.WaitingForTracking, workflow.Phase);

            workflow.Tick(isTrackingGood: true);
            Assert.AreEqual(ScanPhase.FindFloor, workflow.Phase);
        }

        [Test]
        public void FloorLockAdvancesToFloorLocked()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = Workflow(provider);
            workflow.Tick(true);
            workflow.Tick(true);

            Assert.IsTrue(workflow.TryLockFloor(out FloorLockRejection rejection));

            Assert.AreEqual(FloorLockRejection.None, rejection);
            Assert.AreEqual(ScanPhase.FloorLocked, workflow.Phase);
            Assert.IsNotNull(workflow.Frame);
        }

        [Test]
        public void FloorLockIncrementsTheRevision()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());
            workflow.Tick(true);
            workflow.Tick(true);

            int before = workflow.Revision;
            Assert.IsTrue(workflow.TryLockFloor(out _));

            Assert.AreEqual(before + 1, workflow.Revision);
            Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);
        }

        [Test]
        public void FloorLockPublishesAnEmptyRoomAtHeightZero()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());
            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            SceneSnapshot snapshot = workflow.Snapshot;

            Assert.AreEqual(ProtocolConstants.SchemaVersion, snapshot.schemaVersion);
            Assert.AreEqual(workflow.SessionId, snapshot.sessionId);
            Assert.AreEqual(ScanPhase.FloorLocked.ToString(), snapshot.scanPhase);
            Assert.IsFalse(snapshot.finalized);
            Assert.AreEqual(0f, snapshot.closureErrorM);

            Assert.IsNotNull(snapshot.room);
            Assert.AreEqual(0f, snapshot.room.heightM);
            Assert.IsNotNull(snapshot.room.corners);
            Assert.IsEmpty(snapshot.room.corners);
            Assert.IsEmpty(snapshot.room.openings);
            Assert.IsEmpty(snapshot.room.objects);
        }

        /// <summary>
        /// Scene schema v1 requires collections to be zero-length arrays rather
        /// than null on the wire, and JsonUtility will happily emit null.
        /// </summary>
        [Test]
        public void FloorLockSnapshotSerializesWithEmptyArrays()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());
            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            string json = JsonUtility.ToJson(workflow.Snapshot);

            StringAssert.Contains("\"corners\":[]", json.Replace(" ", string.Empty));
            StringAssert.DoesNotContain("\"walls\"", json);
        }

        [Test]
        public void FailedFloorLockDoesNotChangePhaseOrRevision()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.FloorHitAlignment = PlaneAlignment.Vertical;

            ScanWorkflowController workflow = Workflow(provider);
            workflow.Tick(true);
            workflow.Tick(true);

            int revisionBefore = workflow.Revision;

            Assert.IsFalse(workflow.TryLockFloor(out FloorLockRejection rejection));

            Assert.AreEqual(FloorLockRejection.PlaneNotHorizontalUp, rejection);
            Assert.AreEqual(ScanPhase.FindFloor, workflow.Phase);
            Assert.AreEqual(revisionBefore, workflow.Revision);
            Assert.IsNull(workflow.Frame);
        }

        [Test]
        public void FloorCannotBeLockedBeforeTheFindFloorPhase()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());

            Assert.IsFalse(workflow.TryLockFloor(out _));
            Assert.AreEqual(ScanPhase.Boot, workflow.Phase);
        }

        [Test]
        public void FloorCannotBeLockedTwice()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());
            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            int revisionAfterLock = workflow.Revision;

            Assert.IsFalse(workflow.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.AlreadyLocked, rejection);
            Assert.AreEqual(revisionAfterLock, workflow.Revision);
        }

        [Test]
        public void TickAfterFloorLockDoesNotWalkThePhaseBack()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());
            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            workflow.Tick(isTrackingGood: false);
            workflow.Tick(isTrackingGood: true);

            Assert.AreEqual(ScanPhase.FloorLocked, workflow.Phase);
        }

        [Test]
        public void IllegalTransitionThrows()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());

            Assert.Throws<InvalidOperationException>(
                () => workflow.TransitionTo(ScanPhase.AddObjects));
        }

        [Test]
        public void DocumentedBackTransitionsAreAllowed()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());
            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            workflow.TransitionTo(ScanPhase.CaptureCorners);
            workflow.TransitionTo(ScanPhase.VerifyClosure);

            Assert.IsTrue(workflow.CanTransitionTo(ScanPhase.CaptureCorners));
            Assert.IsTrue(workflow.CanTransitionTo(ScanPhase.CaptureHeight));
        }

        [Test]
        public void SessionIdIsStableForTheLifeOfTheSession()
        {
            ScanWorkflowController workflow = Workflow(GoodProvider());
            string sessionId = workflow.SessionId;

            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            Assert.AreEqual(sessionId, workflow.SessionId);
            Assert.AreEqual(sessionId, workflow.Snapshot.sessionId);
        }

        // -------------------------------------------------------------------
        // Task S3 — corner capture
        // -------------------------------------------------------------------

        [Test]
        public void CornerCaptureIsEnteredFromFloorLocked()
        {
            ScanWorkflowController workflow = LockedWorkflow(GoodProvider());

            Assert.IsTrue(workflow.BeginCornerCapture());
            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);
            Assert.AreEqual(ScanPhase.CaptureCorners.ToString(), workflow.Snapshot.scanPhase);
        }

        [Test]
        public void CornersCannotBeCapturedBeforeTheCaptureCornersPhase()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            AimAtGhost(provider, workflow.Frame, 0f, 0f);

            Assert.IsFalse(workflow.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.WrongPhase, rejection);
            Assert.AreEqual(0, workflow.Corners.CornerCount);
        }

        [Test]
        public void EachAcceptedCornerAdvancesTheRevision()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Assert.IsTrue(workflow.BeginCornerCapture());

            int revision = workflow.Revision;

            foreach (Vector2 corner in LegalRoom)
            {
                AimAtGhost(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _));

                Assert.Greater(workflow.Revision, revision, "Revision must be monotonic.");
                Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);

                revision = workflow.Revision;
            }
        }

        [Test]
        public void ARejectedCornerChangesNothing()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Assert.IsTrue(workflow.BeginCornerCapture());

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryCaptureCorner(out _));

            int revision = workflow.Revision;

            // 0.30 m from the previous corner, below the 0.50 m minimum.
            AimAtGhost(provider, workflow.Frame, 0.30f, 0f);

            Assert.IsFalse(workflow.TryCaptureCorner(out CornerCaptureRejection rejection));

            Assert.AreEqual(CornerCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(revision, workflow.Revision);
            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);
            Assert.AreEqual(1, workflow.Snapshot.room.corners.Length);
        }

        [Test]
        public void TheFourthCornerEntersClosureVerification()
        {
            ScanWorkflowController workflow = RoomCaptured(GoodProvider());

            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);
            Assert.AreEqual(ScanPhase.VerifyClosure.ToString(), workflow.Snapshot.scanPhase);
            Assert.AreEqual(4, workflow.Snapshot.room.corners.Length);
        }

        [Test]
        public void TheSnapshotCarriesTheCornersInCaptureOrder()
        {
            ScanWorkflowController workflow = RoomCaptured(GoodProvider());

            CornerModel[] corners = workflow.Snapshot.room.corners;

            for (int i = 0; i < LegalRoom.Length; i++)
            {
                Assert.That(corners[i].position.x, Is.EqualTo(LegalRoom[i].x).Within(1e-3f), $"corner {i} x");
                Assert.That(corners[i].position.z, Is.EqualTo(LegalRoom[i].y).Within(1e-3f), $"corner {i} z");
                Assert.AreEqual(0f, corners[i].position.y, $"corner {i} y");
            }
        }

        /// <summary>
        /// A published snapshot is a value, not a window onto live state. If it
        /// shared corner objects with the capture controller, an undo would
        /// retroactively rewrite a revision the viewer had already accepted.
        /// </summary>
        [Test]
        public void SnapshotCornersAreCopiesRatherThanLiveReferences()
        {
            ScanWorkflowController workflow = RoomCaptured(GoodProvider());

            SceneSnapshot published = workflow.Snapshot;
            CornerModel live = workflow.Corners.Corners[0];

            Assert.AreNotSame(live, published.room.corners[0]);

            Assert.IsTrue(workflow.TryUndoCorner(out _));

            Assert.AreEqual(4, published.room.corners.Length,
                "The already-published snapshot changed when a corner was undone.");
        }

        /// <summary>
        /// Walls are derived from consecutive corners and must never appear on
        /// the wire; a stored wall could disagree with the corners it came from.
        /// </summary>
        [Test]
        public void TheFourCornerSnapshotDoesNotSerializeWalls()
        {
            ScanWorkflowController workflow = RoomCaptured(GoodProvider());

            string json = JsonUtility.ToJson(workflow.Snapshot);

            StringAssert.DoesNotContain("\"walls\"", json);
            StringAssert.Contains("\"corners\"", json);
        }

        // -------------------------------------------------------------------
        // Task S3 — undo
        // -------------------------------------------------------------------

        [Test]
        public void UndoRemovesACornerAndStillAdvancesTheRevision()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Assert.IsTrue(workflow.BeginCornerCapture());

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryCaptureCorner(out _));
            AimAtGhost(provider, workflow.Frame, 3f, 0f);
            Assert.IsTrue(workflow.TryCaptureCorner(out _));

            int revision = workflow.Revision;

            Assert.IsTrue(workflow.TryUndoCorner(out CornerCaptureRejection rejection));

            Assert.AreEqual(CornerCaptureRejection.None, rejection);
            Assert.AreEqual(1, workflow.Corners.CornerCount);
            Assert.AreEqual(1, workflow.Snapshot.room.corners.Length);

            // Revision is monotonic per scene schema v1: the viewer ignores any
            // snapshot whose revision is not greater than the one it holds, so
            // an undo that wound the counter back would be dropped.
            Assert.Greater(workflow.Revision, revision);
            Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);
        }

        [Test]
        public void UndoFromClosureVerificationReturnsToCornerCapture()
        {
            ScanWorkflowController workflow = RoomCaptured(GoodProvider());

            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);

            Assert.IsTrue(workflow.TryUndoCorner(out _));

            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);
            Assert.AreEqual(3, workflow.Corners.CornerCount);
            Assert.AreEqual(0f, workflow.Snapshot.closureErrorM);
        }

        // -------------------------------------------------------------------
        // Task S3 — closure verification
        // -------------------------------------------------------------------

        [Test]
        public void AnExcellentClosureIsRecordedAndTheScanProceeds()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomCaptured(provider);

            AimAtGhost(provider, workflow.Frame, 0.05f, 0f);

            Assert.IsTrue(workflow.TryVerifyClosure(out ClosureQuality quality, out _));

            Assert.AreEqual(ClosureQuality.Excellent, quality);
            Assert.That(workflow.Snapshot.closureErrorM, Is.EqualTo(0.05f).Within(1e-3f));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);
        }

        [Test]
        public void AnAcceptableClosureAlsoProceeds()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomCaptured(provider);

            AimAtGhost(provider, workflow.Frame, 0.12f, 0f);

            Assert.IsTrue(workflow.TryVerifyClosure(out ClosureQuality quality, out _));

            Assert.AreEqual(ClosureQuality.Acceptable, quality);
            Assert.That(workflow.Snapshot.closureErrorM, Is.EqualTo(0.12f).Within(1e-3f));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);
        }

        /// <summary>
        /// A rejected closure must not continue to height capture. The phase
        /// stays put so the user can only redo the corners.
        /// </summary>
        [Test]
        public void ARejectedClosureDoesNotProceedToHeightCapture()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomCaptured(provider);

            AimAtGhost(provider, workflow.Frame, 0.20f, 0f);

            Assert.IsTrue(workflow.TryVerifyClosure(out ClosureQuality quality, out _));

            Assert.AreEqual(ClosureQuality.Rejected, quality);
            Assert.AreEqual(ScanPhase.VerifyClosure, workflow.Phase);
            Assert.That(workflow.Snapshot.closureErrorM, Is.EqualTo(0.20f).Within(1e-3f));
            Assert.IsFalse(workflow.Corners.IsClosureAccepted);
        }

        [Test]
        public void RedoCornersClearsTheRoomAndReturnsToCornerCapture()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomCaptured(provider);

            AimAtGhost(provider, workflow.Frame, 0.20f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));

            int revision = workflow.Revision;

            Assert.IsTrue(workflow.RedoCorners());

            Assert.AreEqual(ScanPhase.CaptureCorners, workflow.Phase);
            Assert.AreEqual(0, workflow.Corners.CornerCount);
            Assert.IsEmpty(workflow.Snapshot.room.corners);
            Assert.AreEqual(0f, workflow.Snapshot.closureErrorM);
            Assert.Greater(workflow.Revision, revision);
        }

        /// <summary>
        /// Redoing the corners must not disturb the frame. The room is being
        /// re-measured, not re-anchored.
        /// </summary>
        [Test]
        public void RedoCornersKeepsTheLockedFrame()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomCaptured(provider);

            GhostCoordinateFrame frame = workflow.Frame;

            AimAtGhost(provider, workflow.Frame, 0.20f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.IsTrue(workflow.RedoCorners());

            Assert.AreSame(frame, workflow.Frame);
        }

        [Test]
        public void ClosureCannotBeVerifiedOutsideTheVerifyClosurePhase()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Assert.IsTrue(workflow.BeginCornerCapture());

            AimAtGhost(provider, workflow.Frame, 0f, 0f);

            Assert.IsFalse(workflow.TryVerifyClosure(out _, out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.WrongPhase, rejection);
        }

        /// <summary>
        /// The whole of S3 runs against the frame S2 established. Nothing here
        /// may re-lock or move it.
        /// </summary>
        [Test]
        public void TheFrameSurvivesTheWholeCornerCaptureWorkflow()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            GhostCoordinateFrame frame = workflow.Frame;
            Vector3 origin = frame.Origin;
            Vector3 right = frame.Right;
            Vector3 forward = frame.Forward;

            Assert.IsTrue(workflow.BeginCornerCapture());

            foreach (Vector2 corner in LegalRoom)
            {
                AimAtGhost(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _));
                Assert.AreSame(frame, workflow.Frame);
            }

            AimAtGhost(provider, workflow.Frame, 0.05f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));

            Assert.AreSame(frame, workflow.Frame);
            Assert.AreEqual(origin, frame.Origin);
            Assert.AreEqual(right, frame.Right);
            Assert.AreEqual(forward, frame.Forward);
        }

        // -------------------------------------------------------------------
        // Task S4 — height capture
        // -------------------------------------------------------------------

        /// <summary>
        /// Points the center-screen ray from roughly the room's center at a
        /// point on wall 0 (corners 0-&gt;1, at Ghost z = 0) at the given height.
        /// </summary>
        private static void AimAtWallHeight(
            FakeSpatialProvider provider, GhostCoordinateFrame frame, float wallX, float heightM)
        {
            Vector3 eye = frame.GhostToWorld(new Vector3(1.5f, 1.5f, 1.25f));
            Vector3 target = frame.GhostToWorld(new Vector3(wallX, heightM, 0f));

            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        /// <summary>Captures the legal room and a good closure, landing at CaptureHeight.</summary>
        private static ScanWorkflowController RoomAtCaptureHeight(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = RoomCaptured(provider);

            AimAtGhost(provider, workflow.Frame, 0.05f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);

            return workflow;
        }

        [Test]
        public void ConfirmingAValidHeightAdvancesToAddOpenings()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.5f);

            Assert.IsTrue(workflow.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.None, rejection);
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);
        }

        [Test]
        public void AnInvalidHeightDoesNotAdvanceThePhase()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 1.2f);

            Assert.IsFalse(workflow.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);
        }

        [Test]
        public void AConfirmedHeightIsWrittenToTheRoomSnapshot()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);

            Assert.AreEqual(0f, workflow.Snapshot.room.heightM);

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.5f);
            Assert.IsTrue(workflow.TryCaptureHeight(out _));

            Assert.That(workflow.Snapshot.room.heightM, Is.EqualTo(2.5f).Within(1e-3f));
        }

        [Test]
        public void RevisionIncreasesMonotonicallyAfterHeightIsCaptured()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);
            int revision = workflow.Revision;

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.5f);
            Assert.IsTrue(workflow.TryCaptureHeight(out _));

            Assert.Greater(workflow.Revision, revision);
            Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);
        }

        [Test]
        public void ManualHeightAlsoAdvancesToAddOpenings()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);

            Assert.IsTrue(workflow.TrySetManualHeight(2.6f, out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.None, rejection);
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);
            Assert.That(workflow.Snapshot.room.heightM, Is.EqualTo(2.6f).Within(1e-3f));
        }

        [Test]
        public void HeightCannotBeCapturedOutsideTheCaptureHeightPhase()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Assert.IsTrue(workflow.BeginCornerCapture());

            Assert.IsFalse(workflow.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.WrongPhase, rejection);
        }

        [Test]
        public void ManualHeightCannotBeSetOutsideTheCaptureHeightPhase()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = LockedWorkflow(provider);

            Assert.IsFalse(workflow.TrySetManualHeight(2.5f, out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.WrongPhase, rejection);
        }

        /// <summary>
        /// The S2 frame and the S3 footprint are authoritative and must not be
        /// disturbed by S4. Height capture only ever reads them.
        /// </summary>
        [Test]
        public void TheFrameAndCornersSurviveHeightCapture()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);

            GhostCoordinateFrame frame = workflow.Frame;
            var beforeCorners = new CornerModel[workflow.Snapshot.room.corners.Length];
            workflow.Snapshot.room.corners.CopyTo(beforeCorners, 0);

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.5f);
            Assert.IsTrue(workflow.TryCaptureHeight(out _));

            Assert.AreSame(frame, workflow.Frame);
            Assert.AreEqual(4, workflow.Snapshot.room.corners.Length);

            for (int i = 0; i < beforeCorners.Length; i++)
            {
                Assert.AreEqual(beforeCorners[i].position.x, workflow.Snapshot.room.corners[i].position.x);
                Assert.AreEqual(beforeCorners[i].position.y, workflow.Snapshot.room.corners[i].position.y);
                Assert.AreEqual(beforeCorners[i].position.z, workflow.Snapshot.room.corners[i].position.z);
            }
        }

        // -------------------------------------------------------------------
        // Task S5 Part 1 — openings
        // -------------------------------------------------------------------

        /// <summary>Captures a legal room, a good closure and a 2.5 m height, landing at AddOpenings.</summary>
        private static ScanWorkflowController RoomAtAddOpenings(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.5f);
            Assert.IsTrue(workflow.TryCaptureHeight(out _));
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);

            return workflow;
        }

        [Test]
        public void ACompleteDoorCaptureAppendsAnOpeningAndAdvancesTheRevision()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddOpenings(provider);

            Assert.IsTrue(workflow.SelectOpeningWall(0));
            Assert.IsTrue(workflow.SetOpeningType(OpeningValidator.TypeDoor));

            int revision = workflow.Revision;

            AimAtWallHeight(provider, workflow.Frame, 0.5f, 0f);
            Assert.IsTrue(workflow.TryCaptureOpeningStartPoint(out _));

            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.05f);
            Assert.IsTrue(workflow.TryCaptureOpeningEndPoint(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.None, rejection);
            Assert.AreEqual(1, workflow.Snapshot.room.openings.Length);
            Assert.Greater(workflow.Revision, revision);
            Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);
        }

        [Test]
        public void OpeningCaptureCannotHappenOutsideAddOpeningsPhase()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtCaptureHeight(provider);

            Assert.IsFalse(workflow.TryCaptureOpeningStartPoint(out OpeningCaptureRejection rejection));
            Assert.AreEqual(OpeningCaptureRejection.WrongPhase, rejection);
        }

        [Test]
        public void FinishingOpeningsWithNoneCapturedIsAllowedAndAdvancesToAddObjects()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddOpenings(provider);

            Assert.IsTrue(workflow.FinishAddingOpenings());

            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);
            Assert.IsEmpty(workflow.Snapshot.room.openings);
        }

        [Test]
        public void FinishAddingOpeningsIncreasesTheRevision()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddOpenings(provider);
            int revision = workflow.Revision;

            Assert.IsTrue(workflow.FinishAddingOpenings());

            Assert.Greater(workflow.Revision, revision);
            Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);
        }

        // -------------------------------------------------------------------
        // Task S5 Part 2 — furniture / objects
        // -------------------------------------------------------------------

        /// <summary>Skips straight through AddOpenings with none captured, landing at AddObjects.</summary>
        private static ScanWorkflowController RoomAtAddObjects(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = RoomAtAddOpenings(provider);
            Assert.IsTrue(workflow.FinishAddingOpenings());
            return workflow;
        }

        [Test]
        public void PlacingAnObjectAppendsItAndAdvancesTheRevision()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddObjects(provider);

            Assert.IsTrue(workflow.SetObjectType("bed"));
            AimAtGhost(provider, workflow.Frame, 1.5f, 1.25f);

            int revision = workflow.Revision;

            Assert.IsTrue(workflow.TryPlaceObject(out ObjectPlacementRejection rejection));

            Assert.AreEqual(ObjectPlacementRejection.None, rejection);
            Assert.AreEqual(1, workflow.Snapshot.room.objects.Length);
            Assert.AreEqual("bed", workflow.Snapshot.room.objects[0].type);
            Assert.Greater(workflow.Revision, revision);
            Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);
        }

        [Test]
        public void ObjectPlacementCannotHappenOutsideAddObjectsPhase()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddOpenings(provider);

            Assert.IsFalse(workflow.TryPlaceObject(out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.WrongPhase, rejection);
        }

        [Test]
        public void FinishingObjectsWithNoneCapturedIsAllowedAndAdvancesToReadyToFinalize()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddObjects(provider);

            Assert.IsTrue(workflow.FinishAddingObjects());

            Assert.AreEqual(ScanPhase.ReadyToFinalize, workflow.Phase);
            Assert.IsEmpty(workflow.Snapshot.room.objects);
        }

        /// <summary>
        /// S5 stops at ReadyToFinalize. Actually finalizing — sending
        /// <c>scan.finalized</c> and setting <c>SceneSnapshot.finalized</c> —
        /// is Task S6's networking work, not S5's.
        /// </summary>
        [Test]
        public void ReachingReadyToFinalizeDoesNotFinalizeTheScan()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddObjects(provider);

            Assert.IsTrue(workflow.FinishAddingObjects());

            Assert.AreEqual(ScanPhase.ReadyToFinalize, workflow.Phase);
            Assert.IsFalse(workflow.Snapshot.finalized);
            Assert.AreNotEqual(ScanPhase.Finalized, workflow.Phase);
        }

        [Test]
        public void RevisionIncreasesMonotonicallyThroughOpeningsAndObjects()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddOpenings(provider);
            int revision = workflow.Revision;

            Assert.IsTrue(workflow.SelectOpeningWall(0));
            AimAtWallHeight(provider, workflow.Frame, 0.5f, 0f);
            Assert.IsTrue(workflow.TryCaptureOpeningStartPoint(out _));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.05f);
            Assert.IsTrue(workflow.TryCaptureOpeningEndPoint(out _));
            Assert.Greater(workflow.Revision, revision);
            revision = workflow.Revision;

            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.Greater(workflow.Revision, revision);
            revision = workflow.Revision;

            Assert.IsTrue(workflow.SetObjectType("chair"));
            AimAtGhost(provider, workflow.Frame, 1.5f, 1.25f);
            Assert.IsTrue(workflow.TryPlaceObject(out _));
            Assert.Greater(workflow.Revision, revision);
            revision = workflow.Revision;

            Assert.IsTrue(workflow.FinishAddingObjects());
            Assert.Greater(workflow.Revision, revision);

            Assert.AreEqual(workflow.Revision, workflow.Snapshot.revision);
        }

        [Test]
        public void TheFrameCornersAndHeightSurviveOpeningAndObjectCapture()
        {
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = RoomAtAddOpenings(provider);

            GhostCoordinateFrame frame = workflow.Frame;
            float capturedHeight = workflow.Snapshot.room.heightM;
            int cornerCount = workflow.Snapshot.room.corners.Length;

            Assert.IsTrue(workflow.SelectOpeningWall(0));
            AimAtWallHeight(provider, workflow.Frame, 0.5f, 0f);
            Assert.IsTrue(workflow.TryCaptureOpeningStartPoint(out _));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.05f);
            Assert.IsTrue(workflow.TryCaptureOpeningEndPoint(out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());

            AimAtGhost(provider, workflow.Frame, 1.5f, 1.25f);
            Assert.IsTrue(workflow.TryPlaceObject(out _));

            Assert.AreSame(frame, workflow.Frame);
            Assert.AreEqual(capturedHeight, workflow.Snapshot.room.heightM);
            Assert.AreEqual(cornerCount, workflow.Snapshot.room.corners.Length);
        }
    }
}
