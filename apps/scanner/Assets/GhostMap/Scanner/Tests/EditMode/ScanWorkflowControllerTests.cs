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

            return new ScanWorkflowController(
                floorLock,
                new CornerCaptureController(provider, floorLock));
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
    }
}
