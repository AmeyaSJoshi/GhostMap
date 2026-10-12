using System.IO;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Export;
using GhostMap.Scanner.UI;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task R9: the predictable failures (poor tracking on every capture path,
    /// returning from the background, a full disk) and the guided-flow polish
    /// (six steps, the nothing-found hint, haptics, the developer toggle).
    /// </summary>
    public sealed class HardeningTests
    {
        private static readonly Vector2[] Corners =
        {
            new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 2.5f), new Vector2(0f, 2.5f)
        };

        private static readonly ScanGuideContext Tracking =
            new ScanGuideContext(true, FloorLockRejection.None, connectedToComputer: false);

        // -------------------------------------------------------------------
        // Fixture: a workflow stopped at a chosen phase
        // -------------------------------------------------------------------

        private static FakeSpatialProvider Provider() => new FakeSpatialProvider
        {
            IsTrackingGood = true,
            HasCameraPose = true,
            HasFloorHit = true,
            HasPlaneDetection = true,
            FloorHitAlignment = PlaneAlignment.HorizontalUp,
            FloorHitWorldPosition = new Vector3(0f, -1.4f, 1.8f),
            CameraPose = new Pose(new Vector3(0f, 0f, 0f), Quaternion.Euler(30f, 20f, 0f))
        };

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

        /// <summary>Floor locked and the four corners walked and checked: the height step.</summary>
        private static ScanWorkflowController AtHeight(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = Workflow(provider);
            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Assert.IsTrue(workflow.BeginCornerCapture());

            foreach (Vector2 corner in Corners)
            {
                ScanWorkflowTestRoom.AimAtFloor(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _), workflow.Corners.LastError);
            }

            ScanWorkflowTestRoom.AimAtFloor(provider, workflow.Frame, 0.05f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.AreEqual(ScanPhase.CaptureHeight, workflow.Phase);
            return workflow;
        }

        private static ScanWorkflowController AtOpenings(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = AtHeight(provider);
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Assert.AreEqual(ScanPhase.AddOpenings, workflow.Phase);
            return workflow;
        }

        private static ScanWorkflowController AtObjects(FakeSpatialProvider provider)
        {
            ScanWorkflowController workflow = AtOpenings(provider);
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);
            return workflow;
        }

        // -------------------------------------------------------------------
        // Poor tracking refuses every capture, with no new revision
        // -------------------------------------------------------------------

        [Test]
        public void PoorTracking_RefusesTheHeightCapture()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtHeight(provider);
            Assert.IsTrue(workflow.SelectHeightWall(0));
            ScanWorkflowTestRoom.AimAtWall(provider, workflow.Frame, 1.5f, 2.5f);
            int revision = workflow.Revision;

            provider.IsTrackingGood = false;
            Assert.IsFalse(workflow.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.TrackingNotGood, rejection);
            Assert.AreEqual(revision, workflow.Revision);

            provider.IsTrackingGood = true;
            Assert.IsTrue(workflow.TryCaptureHeight(out _), "works again once tracking is back");
        }

        [Test]
        public void PoorTracking_RefusesAnOpeningCorner()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtOpenings(provider);
            Assert.IsTrue(workflow.SelectOpeningWall(0));
            ScanWorkflowTestRoom.AimAtWall(provider, workflow.Frame, 0.5f, 0f);
            int revision = workflow.Revision;

            provider.IsTrackingGood = false;
            Assert.IsFalse(workflow.TryCaptureOpeningStartPoint(out OpeningCaptureRejection rejection));
            Assert.AreEqual(OpeningCaptureRejection.TrackingNotGood, rejection);
            Assert.AreEqual(revision, workflow.Revision);
        }

        [Test]
        public void PoorTracking_RefusesPlacingFurniture()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtObjects(provider);
            ScanWorkflowTestRoom.AimAtFloor(provider, workflow.Frame, 1.5f, 1.25f);
            int revision = workflow.Revision;

            provider.IsTrackingGood = false;
            Assert.IsFalse(workflow.TryPlaceObject(out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.TrackingNotGood, rejection);
            Assert.AreEqual(revision, workflow.Revision);
            Assert.AreEqual(0, workflow.Objects.ObjectCount);
        }

        [Test]
        public void PoorTracking_RefusesAddingADetectedSurface()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtObjects(provider);
            Vector3 axis = workflow.Frame.GhostDirectionToWorld(Vector3.forward);
            provider.DetectedSurfaces.Add(new DetectedSurface(
                new TrackableId(424242, 0),
                workflow.Frame.GhostToWorld(new Vector3(1.5f, 0.74f, 1.25f)),
                Quaternion.LookRotation(axis, Vector3.up) * Quaternion.Euler(0f, -90f, 0f),
                new Vector2(1.4f, 0.7f),
                PlaneAlignment.HorizontalUp));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            int revision = workflow.Revision;

            provider.IsTrackingGood = false;
            Assert.IsFalse(workflow.TryAcceptDetectedFurniture(out FurnitureDetectionRejection rejection, out _));
            Assert.AreEqual(FurnitureDetectionRejection.TrackingNotGood, rejection);
            Assert.AreEqual(revision, workflow.Revision);
            Assert.AreEqual(0, workflow.TryAcceptAllDetectedFurniture());
        }

        [Test]
        public void PoorTracking_StillAllowsTypingTheHeight()
        {
            // Typing needs no AR, so it is the way out when tracking will not settle.
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtHeight(provider);
            provider.IsTrackingGood = false;

            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
        }

        // -------------------------------------------------------------------
        // Returning from the background
        // -------------------------------------------------------------------

        [Test]
        public void ResumeGate_PassesTrackingThroughUntilTheAppIsPaused()
        {
            var gate = new TrackingResumeGate();

            Assert.IsTrue(gate.Allows(true, 0f));
            Assert.IsFalse(gate.Allows(false, 0.1f));
            Assert.IsFalse(gate.IsRecovering);
        }

        [Test]
        public void ResumeGate_BlocksWhilePaused_AndUntilTrackingHasSettled()
        {
            var gate = new TrackingResumeGate();
            gate.Pause();
            Assert.IsFalse(gate.Allows(true, 10f), "paused");
            Assert.IsTrue(gate.IsRecovering);

            gate.Resume();
            Assert.IsFalse(gate.Allows(true, 20f), "ARKit's stale 'tracking' right after resuming is not trusted");
            Assert.IsFalse(gate.Allows(true, 20f + TrackingResumeGate.SettleSeconds * 0.9f));
            Assert.IsTrue(gate.Allows(true, 20f + TrackingResumeGate.SettleSeconds));
            Assert.IsFalse(gate.IsRecovering);

            Assert.IsFalse(gate.Allows(false, 30f), "settled: follows ARKit again");
            Assert.IsTrue(gate.Allows(true, 30.1f));
        }

        [Test]
        public void ResumeGate_RestartsTheSettlingClockWhenTrackingDrops()
        {
            var gate = new TrackingResumeGate();
            gate.Pause();
            gate.Resume();

            Assert.IsFalse(gate.Allows(true, 0f));
            Assert.IsFalse(gate.Allows(false, 0.8f));
            Assert.IsFalse(gate.Allows(true, 1.2f), "the drop reset the clock");
            Assert.IsTrue(gate.Allows(true, 1.2f + TrackingResumeGate.SettleSeconds));
        }

        [Test]
        public void Guide_SaysWelcomeBack_WhileTrackingRecovers()
        {
            StringAssert.StartsWith("Welcome back.", ScanGuide.TrackingLostMessage(recoveringFromBackground: true));
            StringAssert.StartsWith("Tracking lost.", ScanGuide.TrackingLostMessage(recoveringFromBackground: false));
        }

        // -------------------------------------------------------------------
        // Storage full
        // -------------------------------------------------------------------

        [Test]
        public void DiskFull_IsRecognisedByCodeOrMessage()
        {
            Assert.IsTrue(SavedScanStore.IsDiskFull(new IOException("write failed", 28)), "ENOSPC");
            Assert.IsTrue(SavedScanStore.IsDiskFull(new IOException("write failed", unchecked((int)0x80070070))), "Windows disk full");
            Assert.IsTrue(SavedScanStore.IsDiskFull(new IOException("No space left on device")));
            Assert.IsFalse(SavedScanStore.IsDiskFull(new IOException("Sharing violation", 32)));
        }

        [Test]
        public void ASaveThatFails_SaysSo_AndLeavesNothingBehind()
        {
            string blocker = Path.Combine(Path.GetTempPath(), "ghostmap-blocker-" + System.Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "a file where the folder should be");
            try
            {
                var store = new SavedScanStore(Path.Combine(blocker, "scans"));

                Assert.IsFalse(store.TrySave(new byte[] { 1, 2, 3 }, "GhostMap-Room-20261011-1200.zip", out _, out string error));

                StringAssert.StartsWith("The room could not be saved", error);
                Assert.AreEqual(0, store.List().Count);
            }
            finally
            {
                File.Delete(blocker);
            }
        }

        // -------------------------------------------------------------------
        // Guided flow
        // -------------------------------------------------------------------

        [Test]
        public void Guide_HasSixNamedSteps()
        {
            Assert.AreEqual(6, ScanGuide.StepCount);
            CollectionAssert.AreEqual(new[] { "Floor", "Room", "Height", "Openings", "Furniture", "Send" }, ScanGuide.StepNames);
        }

        [Test]
        public void Guide_NumbersEachPhaseIntoTheSixSteps()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = Workflow(provider);
            workflow.Tick(true);
            Assert.AreEqual(1, ScanGuide.Describe(workflow, Tracking).Number);

            workflow = AtHeight(provider);
            Assert.AreEqual(3, ScanGuide.Describe(workflow, Tracking).Number);
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Assert.AreEqual(4, ScanGuide.Describe(workflow, Tracking).Number);
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(5, ScanGuide.Describe(workflow, Tracking).Number);
            Assert.IsTrue(workflow.FinishAddingObjects());
            Assert.AreEqual(6, ScanGuide.Describe(workflow, Tracking).Number);
            Assert.IsTrue(workflow.TryFinalize(out _));
            Assert.AreEqual(6, ScanGuide.Describe(workflow, Tracking).Number);
        }

        [Test]
        public void Furniture_WithADetector_FirstAsksToLookAround_ThenPointsToPlacingByHand()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtObjects(provider);

            ScanGuideStep early = ScanGuide.Describe(workflow, new ScanGuideContext(
                true, FloorLockRejection.None, false, secondsInPhase: 2f, detectorAvailable: true));
            StringAssert.Contains("Look slowly around", early.Instruction);
            Assert.IsNull(early.AimHint);
            StringAssert.Contains("Place", early.Message);

            ScanGuideStep late = ScanGuide.Describe(workflow, new ScanGuideContext(
                true, FloorLockRejection.None, false,
                secondsInPhase: ScanGuide.NothingFoundAfterSeconds + 1f, detectorAvailable: true));
            StringAssert.Contains("tap Place", late.Instruction);
            StringAssert.StartsWith("Nothing recognised yet.", late.Message);
        }

        [Test]
        public void Furniture_WithoutADetector_GoesStraightToPlacingByHand()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtObjects(provider);

            ScanGuideStep step = ScanGuide.Describe(workflow, new ScanGuideContext(
                true, FloorLockRejection.None, false, secondsInPhase: 0f, detectorAvailable: false));

            StringAssert.Contains("tap Place", step.Instruction);
        }

        // -------------------------------------------------------------------
        // Haptics
        // -------------------------------------------------------------------

        private sealed class CountingHaptics : IHaptics
        {
            public int Pulses { get; private set; }
            public void LightImpact() => Pulses++;
        }

        [Test]
        public void Haptics_OnePulsePerCapture_NoneForANewScanOrNothing()
        {
            var counter = new CountingHaptics();
            var haptics = new CaptureHaptics(counter);
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AtObjects(provider);

            haptics.Observe(workflow);
            haptics.Observe(workflow);
            Assert.AreEqual(0, counter.Pulses, "a new workflow, then no change");

            ScanWorkflowTestRoom.AimAtFloor(provider, workflow.Frame, 1.5f, 1.25f);
            Assert.IsTrue(workflow.TryPlaceObject(out _));
            haptics.Observe(workflow);
            Assert.AreEqual(1, counter.Pulses);

            Assert.IsTrue(workflow.TrySetObjectWidth(0, 0.8f, out _));
            Assert.IsTrue(workflow.TrySetObjectDepth(0, 0.8f, out _));
            haptics.Observe(workflow);
            Assert.AreEqual(2, counter.Pulses, "two changes in one frame are one pulse");

            haptics.Observe(AtObjects(Provider()));
            Assert.AreEqual(2, counter.Pulses, "Restart is not a capture");
        }

        [Test]
        public void Detector_RunsSlowerUntilTheFurnitureStep()
        {
            // R2: during the room scan ARKit needs the phone's capacity for walls.
            Assert.AreEqual(ObjectDetectors.BackgroundIntervalSeconds, ObjectDetectors.IntervalFor(ScanPhase.AutoScanRoom));
            Assert.AreEqual(ObjectDetectors.BackgroundIntervalSeconds, ObjectDetectors.IntervalFor(ScanPhase.CaptureHeight));
            Assert.AreEqual(ObjectDetectors.ActiveIntervalSeconds, ObjectDetectors.IntervalFor(ScanPhase.AddObjects));
            Assert.Greater(ObjectDetectors.BackgroundIntervalSeconds, ObjectDetectors.ActiveIntervalSeconds);
        }

        // -------------------------------------------------------------------
        // Developer mode toggle
        // -------------------------------------------------------------------

        [Test]
        public void SecretTaps_FiveQuickTapsToggle_SlowTapsDoNot()
        {
            var taps = new SecretTapCounter();
            for (int i = 0; i < SecretTapCounter.Taps - 1; i++)
            {
                Assert.IsFalse(taps.Register(i * 0.3f));
            }

            Assert.IsTrue(taps.Register(1.5f));

            var slow = new SecretTapCounter();
            for (int i = 0; i < 10; i++)
            {
                Assert.IsFalse(slow.Register(i * (SecretTapCounter.WindowSeconds / 2f)), $"tap {i}");
            }
        }
    }
}
