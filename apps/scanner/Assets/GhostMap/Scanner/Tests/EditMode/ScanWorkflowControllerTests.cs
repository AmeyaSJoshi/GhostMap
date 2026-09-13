using System;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
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
            return new ScanWorkflowController(new FloorLockController(provider));
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
    }
}
