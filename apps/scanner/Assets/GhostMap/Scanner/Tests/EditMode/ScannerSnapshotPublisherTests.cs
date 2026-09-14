using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Networking;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task S6: <see cref="ScannerSnapshotPublisher"/> turns a workflow's
    /// revision changes and finalization into the wire messages protocol v1
    /// requires, against a <see cref="FakeSnapshotSink"/> rather than a real
    /// socket.
    /// </summary>
    public sealed class ScannerSnapshotPublisherTests
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

        private static ScanWorkflowController NewWorkflow(FakeSpatialProvider provider)
        {
            var floorLock = new FloorLockController(provider);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var openingCapture = new OpeningCaptureController(provider, floorLock, corners, height);
            var objectPlacement = new ObjectPlacementController(provider, floorLock);

            return new ScanWorkflowController(floorLock, corners, height, openingCapture, objectPlacement);
        }

        private static ScanWorkflowController LockedWorkflow(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = NewWorkflow(provider);
            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            return workflow;
        }

        private static void AimAtGhost(FakeSpatialProvider provider, GhostCoordinateFrame frame, float ghostX, float ghostZ)
        {
            Vector3 target = frame.GhostToWorld(new Vector3(ghostX, 0f, ghostZ));
            Vector3 eye = frame.GhostToWorld(new Vector3(ghostX, 1.5f, ghostZ - 1f));
            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        private static void AimAtWallHeight(FakeSpatialProvider provider, GhostCoordinateFrame frame, float wallX, float heightM)
        {
            Vector3 eye = frame.GhostToWorld(new Vector3(1.5f, 1.5f, 1.25f));
            Vector3 target = frame.GhostToWorld(new Vector3(wallX, heightM, 0f));
            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        private static readonly Vector2[] LegalRoom =
        {
            new Vector2(0f, 0f),
            new Vector2(3f, 0f),
            new Vector2(3f, 2.5f),
            new Vector2(0f, 2.5f)
        };

        /// <summary>Drives a full legal scan (room, closure, height, one door, one bed) to ReadyToFinalize.</summary>
        private static ScanWorkflowController RoomAtReadyToFinalize(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = LockedWorkflow(provider);
            Assert.IsTrue(workflow.BeginCornerCapture());

            foreach (Vector2 corner in LegalRoom)
            {
                AimAtGhost(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _));
            }

            AimAtGhost(provider, workflow.Frame, 0.05f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));

            Assert.IsTrue(workflow.SelectHeightWall(0));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.5f);
            Assert.IsTrue(workflow.TryCaptureHeight(out _));

            Assert.IsTrue(workflow.SelectOpeningWall(0));
            AimAtWallHeight(provider, workflow.Frame, 0.5f, 0f);
            Assert.IsTrue(workflow.TryCaptureOpeningStartPoint(out _));
            AimAtWallHeight(provider, workflow.Frame, 1.5f, 2.05f);
            Assert.IsTrue(workflow.TryCaptureOpeningEndPoint(out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());

            Assert.IsTrue(workflow.SetObjectType("bed"));
            AimAtGhost(provider, workflow.Frame, 1.5f, 1.25f);
            Assert.IsTrue(workflow.TryPlaceObject(out _));
            Assert.IsTrue(workflow.FinishAddingObjects());

            Assert.AreEqual(ScanPhase.ReadyToFinalize, workflow.Phase);
            return workflow;
        }

        [Test]
        public void RebindForcesAnInitialSnapshotEvenAtRevisionZero()
        {
            var sink = new FakeSnapshotSink();
            var publisher = new ScannerSnapshotPublisher(sink);
            ScanWorkflowController workflow = NewWorkflow(GoodProvider());

            publisher.Rebind(workflow);
            publisher.Tick();

            Assert.AreEqual(1, sink.Batches.Count);
            var message = (SceneSnapshotMessage)sink.Messages[0];
            Assert.AreEqual(0, message.snapshot.revision);
            Assert.AreEqual(workflow.SessionId, message.sessionId);
        }

        [Test]
        public void TickWithNoChangeSincePreviousTickEnqueuesNothing()
        {
            var sink = new FakeSnapshotSink();
            var publisher = new ScannerSnapshotPublisher(sink);
            ScanWorkflowController workflow = NewWorkflow(GoodProvider());

            publisher.Rebind(workflow);
            publisher.Tick();
            Assert.AreEqual(1, sink.Batches.Count);

            publisher.Tick();
            publisher.Tick();

            Assert.AreEqual(1, sink.Batches.Count, "No mutation happened between ticks; nothing new should be enqueued.");
        }

        [Test]
        public void RevisionChangeEnqueuesANewSnapshotMessage()
        {
            var sink = new FakeSnapshotSink();
            var publisher = new ScannerSnapshotPublisher(sink);
            FakeSpatialProvider provider = GoodProvider();
            ScanWorkflowController workflow = NewWorkflow(provider);

            publisher.Rebind(workflow);
            publisher.Tick();

            workflow.Tick(true);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));

            publisher.Tick();

            Assert.AreEqual(2, sink.Batches.Count);
            var message = (SceneSnapshotMessage)sink.Messages[1];
            Assert.AreEqual(workflow.Revision, message.snapshot.revision);
            Assert.AreEqual(ScanPhase.FloorLocked.ToString(), message.snapshot.scanPhase);
        }

        [Test]
        public void FinalizeEnqueuesTheFinalSnapshotAndTheFinalizedMessageInOneBatch()
        {
            var sink = new FakeSnapshotSink();
            var publisher = new ScannerSnapshotPublisher(sink);
            ScanWorkflowController workflow = RoomAtReadyToFinalize(GoodProvider());

            publisher.Rebind(workflow);
            publisher.Tick();
            sink.Batches.Clear();
            sink.Messages.Clear();

            Assert.IsTrue(workflow.TryFinalize(out _));
            publisher.Tick();

            Assert.AreEqual(1, sink.Batches.Count, "The final snapshot and scan.finalized must arrive as one batch.");

            object[] batch = sink.Batches[0];
            Assert.AreEqual(2, batch.Length);

            var snapshotMessage = (SceneSnapshotMessage)batch[0];
            Assert.IsTrue(snapshotMessage.snapshot.finalized);
            Assert.AreEqual(ScanPhase.Finalized.ToString(), snapshotMessage.snapshot.scanPhase);

            var finalizedMessage = (ScanFinalizedMessage)batch[1];
            Assert.AreEqual(workflow.Revision, finalizedMessage.finalRevision);
            Assert.AreEqual(workflow.SessionId, finalizedMessage.sessionId);
        }

        [Test]
        public void FinalizedMessageIsSentOnlyOnce()
        {
            var sink = new FakeSnapshotSink();
            var publisher = new ScannerSnapshotPublisher(sink);
            ScanWorkflowController workflow = RoomAtReadyToFinalize(GoodProvider());

            publisher.Rebind(workflow);
            Assert.IsTrue(workflow.TryFinalize(out _));
            publisher.Tick();

            int batchesAfterFinalize = sink.Batches.Count;

            publisher.Tick();
            publisher.Tick();

            Assert.AreEqual(batchesAfterFinalize, sink.Batches.Count);
        }

        [Test]
        public void ReconnectAfterFinalizationStillResendsTheFinalizedSnapshotViaTheLatestSnapshotProvider()
        {
            // The publisher does not itself resend on reconnect — that is
            // ScannerNetworkClient's job, reading the workflow's current
            // Snapshot directly. This test only pins that a finalized
            // workflow's Snapshot really does carry finalized = true and the
            // Finalized phase after the publisher has observed it, which is
            // the value ScannerNetworkClient's latestSnapshotProvider reads.
            ScanWorkflowController workflow = RoomAtReadyToFinalize(GoodProvider());
            Assert.IsTrue(workflow.TryFinalize(out _));

            Assert.IsTrue(workflow.Snapshot.finalized);
            Assert.AreEqual(ScanPhase.Finalized.ToString(), workflow.Snapshot.scanPhase);
        }

        [Test]
        public void RebindResetsStateForAFreshSession()
        {
            var sink = new FakeSnapshotSink();
            var publisher = new ScannerSnapshotPublisher(sink);
            ScanWorkflowController firstWorkflow = NewWorkflow(GoodProvider());

            publisher.Rebind(firstWorkflow);
            publisher.Tick();
            Assert.AreEqual(1, sink.Batches.Count);

            ScanWorkflowController secondWorkflow = NewWorkflow(GoodProvider());
            publisher.Rebind(secondWorkflow);
            publisher.Tick();

            Assert.AreEqual(2, sink.Batches.Count);
            var message = (SceneSnapshotMessage)sink.Messages[1];
            Assert.AreEqual(secondWorkflow.SessionId, message.sessionId);
            Assert.AreNotEqual(firstWorkflow.SessionId, message.sessionId);
        }

        [Test]
        public void RebindAfterFinalizeAllowsAFreshSessionToBeFinalizedAgain()
        {
            var sink = new FakeSnapshotSink();
            var publisher = new ScannerSnapshotPublisher(sink);
            ScanWorkflowController firstWorkflow = RoomAtReadyToFinalize(GoodProvider());

            publisher.Rebind(firstWorkflow);
            Assert.IsTrue(firstWorkflow.TryFinalize(out _));
            publisher.Tick();

            ScanWorkflowController secondWorkflow = RoomAtReadyToFinalize(GoodProvider());
            publisher.Rebind(secondWorkflow);
            sink.Batches.Clear();
            sink.Messages.Clear();

            Assert.IsTrue(secondWorkflow.TryFinalize(out _));
            publisher.Tick();

            Assert.AreEqual(1, sink.Batches.Count);
            Assert.AreEqual(2, sink.Batches[0].Length);
        }
    }
}
