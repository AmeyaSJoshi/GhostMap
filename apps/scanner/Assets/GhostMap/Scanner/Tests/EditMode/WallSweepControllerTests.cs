using GhostMap.Scanner.Capture;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// ADR-0005 wall sweeping: the per-frame floor-ray accumulation, every rule
    /// that can refuse a sweep, and corner derivation from four walls.
    ///
    /// <para>The fixture frame is the same deliberately hostile one
    /// <see cref="CornerCaptureControllerTests"/> uses — floor 1.4 m below
    /// Unity's origin, 40 degrees of yaw — so a sweep path that dropped the
    /// frame or the floor's world Y produces visibly wrong Ghost coordinates
    /// rather than coincidentally right ones.</para>
    ///
    /// <para>Sweeps are driven by constructing real world-space rays through
    /// intended floor points, so these exercise
    /// <see cref="RayPlaneMath"/> and <see cref="WallFitting"/> rather than
    /// stubbing past them.</para>
    /// </summary>
    public sealed class WallSweepControllerTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);

        private const float YawDeg = 40f;

        /// <summary>Default eye standoff from the aim point, in meters.</summary>
        private const float DefaultStandoffM = 1f;

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

        private static WallSweepController Sweeper(FakeSpatialProvider provider)
        {
            var floorLock = new FloorLockController(provider);

            Assert.IsTrue(
                floorLock.TryLockFloor(out FloorLockRejection rejection),
                $"The fixture's own floor lock should succeed, got {rejection}.");

            return new WallSweepController(provider, floorLock);
        }

        /// <summary>A sweeper whose floor has deliberately not been locked.</summary>
        private static WallSweepController UnlockedSweeper(FakeSpatialProvider provider)
        {
            provider.HasFloorHit = false;
            var floorLock = new FloorLockController(provider);
            Assert.IsFalse(floorLock.TryLockFloor(out _));
            provider.HasFloorHit = true;

            return new WallSweepController(provider, floorLock);
        }

        /// <summary>
        /// Points the center-screen ray at a Ghost-space floor point from an eye
        /// 1.5 m above the floor and <paramref name="standoffM"/> short of it.
        /// </summary>
        private static void AimAtGhost(
            FakeSpatialProvider provider,
            GhostCoordinateFrame frame,
            float ghostX,
            float ghostZ,
            float standoffM = DefaultStandoffM)
        {
            Vector3 target = frame.GhostToWorld(new Vector3(ghostX, 0f, ghostZ));
            Vector3 eye = frame.GhostToWorld(
                new Vector3(ghostX, 1.5f, ghostZ - standoffM));

            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        /// <summary>
        /// Sweeps a straight Ghost-space segment with evenly spaced aims, and
        /// completes it. Asserts acceptance.
        /// </summary>
        private static void SweepWall(
            FakeSpatialProvider provider,
            WallSweepController sweeper,
            Vector2 from,
            Vector2 to,
            int samples = 20,
            float standoffM = DefaultStandoffM)
        {
            Assert.IsTrue(
                sweeper.TryBeginSweep(out WallSweepRejection begin),
                $"Sweep should have started, got {begin}.");

            AddSamplesAlong(provider, sweeper, from, to, samples, standoffM);

            Assert.IsTrue(
                sweeper.TryCompleteSweep(out WallSweepRejection complete),
                $"Sweep {from} -> {to} should have been accepted, got " +
                $"{complete}: {sweeper.LastError}");
        }

        private static void AddSamplesAlong(
            FakeSpatialProvider provider,
            WallSweepController sweeper,
            Vector2 from,
            Vector2 to,
            int samples,
            float standoffM = DefaultStandoffM)
        {
            for (int i = 0; i < samples; i++)
            {
                float t = samples == 1 ? 0f : (float)i / (samples - 1);

                AimAtGhost(
                    provider,
                    sweeper.Frame,
                    Mathf.Lerp(from.x, to.x, t),
                    Mathf.Lerp(from.y, to.y, t),
                    standoffM);

                sweeper.TryAddSample(out _);
            }
        }

        /// <summary>
        /// The four walls of a legal 4.0 m x 3.0 m room, in sweep order, each
        /// swept over most of its length but short of its corners.
        /// </summary>
        private static void SweepLegalRoom(
            FakeSpatialProvider provider,
            WallSweepController sweeper)
        {
            SweepWall(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, sweeper, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));
            SweepWall(provider, sweeper, new Vector2(0.2f, 3f), new Vector2(3.8f, 3f));
            SweepWall(provider, sweeper, new Vector2(0f, 0.2f), new Vector2(0f, 2.8f));
        }

        // -------------------------------------------------------------------
        // Gates on starting a sweep
        // -------------------------------------------------------------------

        [Test]
        public void BeginSweep_RefusedBeforeFloorLock()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = UnlockedSweeper(provider);

            Assert.IsFalse(sweeper.TryBeginSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.FloorNotLocked, rejection);
            Assert.IsFalse(sweeper.IsSweeping);
        }

        [Test]
        public void BeginSweep_RefusedWhenTrackingIsBad()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            provider.IsTrackingGood = false;

            Assert.IsFalse(sweeper.TryBeginSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.TrackingNotGood, rejection);
        }

        [Test]
        public void BeginSweep_RefusedWhileAnotherSweepIsActive()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            Assert.IsFalse(sweeper.TryBeginSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.SweepAlreadyActive, rejection);
        }

        [Test]
        public void BeginSweep_RefusedOnceAllWallsAreSwept()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepLegalRoom(provider, sweeper);
            Assert.IsTrue(sweeper.IsComplete);

            Assert.IsFalse(sweeper.TryBeginSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.AllWallsCaptured, rejection);
        }

        [Test]
        public void CanSweep_TracksFrameTrackingAndCompletion()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.CanSweep);

            provider.IsTrackingGood = false;
            Assert.IsFalse(sweeper.CanSweep);

            provider.IsTrackingGood = true;
            SweepLegalRoom(provider, sweeper);
            Assert.IsFalse(sweeper.CanSweep);
        }

        // -------------------------------------------------------------------
        // Sample accumulation
        // -------------------------------------------------------------------

        [Test]
        public void AddSample_RefusedWithNoActiveSweep()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            AimAtGhost(provider, sweeper.Frame, 1f, 0f);

            Assert.IsFalse(sweeper.TryAddSample(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.NoSweepActive, rejection);
            Assert.AreEqual(0, sweeper.ActiveSampleCount);
        }

        [Test]
        public void AddSample_AccumulatesDistinctSamples()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AddSamplesAlong(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f), 20);

            Assert.AreEqual(20, sweeper.ActiveSampleCount);
            Assert.AreEqual(3.6f, sweeper.ActiveSpanM, Tolerance);
        }

        /// <summary>
        /// Holding the phone still must not bank samples. Without the spacing
        /// gate a stationary sweep would satisfy the minimum sample count
        /// without adding any information.
        /// </summary>
        [Test]
        public void AddSample_DropsSamplesThatHaveBarelyMoved()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AimAtGhost(provider, sweeper.Frame, 1f, 0f);

            for (int i = 0; i < 30; i++)
            {
                sweeper.TryAddSample(out _);
            }

            Assert.AreEqual(1, sweeper.ActiveSampleCount);
            Assert.AreEqual(0f, sweeper.ActiveSpanM, Tolerance);
        }

        [Test]
        public void AddSample_DroppedSampleIsNotReportedAsAFailure()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AimAtGhost(provider, sweeper.Frame, 1f, 0f);

            Assert.IsTrue(sweeper.TryAddSample(out _));
            Assert.IsFalse(sweeper.TryAddSample(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.None, rejection);
        }

        [Test]
        public void AddSample_RejectsRayThatMissesTheFloor()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));

            // Aimed at the ceiling: away from the floor plane entirely.
            provider.ScreenRay = new Ray(
                FloorOrigin + new Vector3(0f, 1.5f, 0f), Vector3.up);

            Assert.IsFalse(sweeper.TryAddSample(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.RayMissedFloor, rejection);
            Assert.AreEqual(0, sweeper.ActiveSampleCount);
        }

        [Test]
        public void AddSample_RejectsRayParallelToTheFloor()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));

            provider.ScreenRay = new Ray(
                FloorOrigin + new Vector3(0f, 1.5f, 0f), Vector3.forward);

            Assert.IsFalse(sweeper.TryAddSample(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.RayMissedFloor, rejection);
        }

        /// <summary>
        /// Tracking is checked per sample, not only at sweep start: a sweep runs
        /// for seconds and a relocalization partway through would otherwise
        /// contribute samples from a frame that has silently moved.
        /// </summary>
        [Test]
        public void AddSample_RejectedWhenTrackingDegradesMidSweep()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AddSamplesAlong(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(1f, 0f), 5);

            int before = sweeper.ActiveSampleCount;
            provider.IsTrackingGood = false;

            AimAtGhost(provider, sweeper.Frame, 2f, 0f);

            Assert.IsFalse(sweeper.TryAddSample(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.TrackingNotGood, rejection);
            Assert.AreEqual(before, sweeper.ActiveSampleCount);
        }

        /// <summary>
        /// Per-frame sampling must not leave a sticky error on screen. A sweep
        /// that crosses a doorway misses the floor for a run of frames, which is
        /// normal.
        /// </summary>
        [Test]
        public void AddSample_DoesNotTouchLastRejection()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            Assert.AreEqual(WallSweepRejection.None, sweeper.LastRejection);

            provider.ScreenRay = new Ray(
                FloorOrigin + new Vector3(0f, 1.5f, 0f), Vector3.up);

            Assert.IsFalse(sweeper.TryAddSample(out _));
            Assert.AreEqual(WallSweepRejection.None, sweeper.LastRejection);
            Assert.IsEmpty(sweeper.LastError);
        }

        [Test]
        public void ActiveSweepLooksUsable_OnlyOnceSamplesAndSpanSuffice()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsFalse(sweeper.ActiveSweepLooksUsable);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            Assert.IsFalse(sweeper.ActiveSweepLooksUsable);

            AddSamplesAlong(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f), 20);
            Assert.IsTrue(sweeper.ActiveSweepLooksUsable);
        }

        // -------------------------------------------------------------------
        // Completing a sweep
        // -------------------------------------------------------------------

        [Test]
        public void CompleteSweep_RefusedWithNoActiveSweep()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsFalse(sweeper.TryCompleteSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.NoSweepActive, rejection);
        }

        [Test]
        public void CompleteSweep_AcceptsAStraightSweepAsAWall()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepWall(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));

            Assert.AreEqual(1, sweeper.WallCount);
            Assert.IsFalse(sweeper.IsSweeping);
            Assert.AreEqual(0, sweeper.ActiveSampleCount);

            WallLine line = sweeper.Walls[0].Line;
            Assert.AreEqual(0f, line.Point.z, Tolerance);
            Assert.AreEqual(1f, Mathf.Abs(line.Direction.x), Tolerance);
            Assert.AreEqual(0f, line.RmsResidualM, Tolerance);
            Assert.AreEqual(3.6f, line.SpanM, Tolerance);
        }

        [Test]
        public void CompleteSweep_RejectsTooFewDistinctSamples()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AddSamplesAlong(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f), 5);

            Assert.IsFalse(sweeper.TryCompleteSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.FitRejected, rejection);
            Assert.AreEqual(WallFitRejection.TooFewSamples, sweeper.LastFitRejection);
            Assert.IsNotEmpty(sweeper.LastError);
            Assert.AreEqual(0, sweeper.WallCount);
        }

        /// <summary>
        /// Perfectly collinear but barely moved: the residual looks excellent
        /// and only the span gate catches it.
        /// </summary>
        [Test]
        public void CompleteSweep_RejectsSweepThatCoveredTooLittleWall()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AddSamplesAlong(provider, sweeper, new Vector2(1f, 0f), new Vector2(1.2f, 0f), 10);

            Assert.IsFalse(sweeper.TryCompleteSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.FitRejected, rejection);
            Assert.AreEqual(WallFitRejection.SpanTooShort, sweeper.LastFitRejection);
            Assert.AreEqual(0, sweeper.WallCount);
        }

        /// <summary>
        /// An aim that wandered off the junction line. 0.15 m of alternating
        /// perpendicular offset is three times the accept limit.
        /// </summary>
        [Test]
        public void CompleteSweep_RejectsSweepWhoseResidualIsTooHigh()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));

            for (int i = 0; i < 20; i++)
            {
                float x = 0.2f + (0.19f * i);
                float z = (i % 2 == 0) ? 0.15f : -0.15f;

                AimAtGhost(provider, sweeper.Frame, x, z);
                sweeper.TryAddSample(out _);
            }

            Assert.IsFalse(sweeper.TryCompleteSweep(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.ResidualTooHigh, rejection);
            Assert.AreEqual(WallFitRejection.None, sweeper.LastFitRejection);
            Assert.IsNotEmpty(sweeper.LastError);
            Assert.AreEqual(0, sweeper.WallCount);
        }

        [Test]
        public void CompleteSweep_DiscardsARefusedSweepEntirely()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AddSamplesAlong(provider, sweeper, new Vector2(1f, 0f), new Vector2(1.15f, 0f), 10);
            Assert.IsFalse(sweeper.TryCompleteSweep(out _));

            Assert.IsFalse(sweeper.IsSweeping);
            Assert.AreEqual(0, sweeper.ActiveSampleCount);
            Assert.AreEqual(0f, sweeper.ActiveSpanM, Tolerance);

            // And the same wall can simply be swept again.
            SweepWall(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            Assert.AreEqual(1, sweeper.WallCount);
        }

        [Test]
        public void CompleteSweep_RecordsTheFurthestSampleDistance()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepWall(
                provider, sweeper,
                new Vector2(0.2f, 0f), new Vector2(3.8f, 0f),
                samples: 20, standoffM: 5f);

            // Eye 1.5 m up and 5 m back: sqrt(1.5^2 + 5^2).
            float expected = Mathf.Sqrt((1.5f * 1.5f) + (5f * 5f));

            Assert.AreEqual(expected, sweeper.Walls[0].MaxSampleDistanceM, 1e-2f);
            Assert.IsTrue(sweeper.HasFarSweptWall);
        }

        [Test]
        public void HasFarSweptWall_FalseForCloseSweeps()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepLegalRoom(provider, sweeper);

            Assert.IsFalse(sweeper.HasFarSweptWall);
        }

        // -------------------------------------------------------------------
        // Cancel, undo, clear
        // -------------------------------------------------------------------

        [Test]
        public void CancelSweep_DiscardsSamplesAndAcceptsNoWall()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsTrue(sweeper.TryBeginSweep(out _));
            AddSamplesAlong(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f), 20);

            Assert.IsTrue(sweeper.CancelSweep());
            Assert.IsFalse(sweeper.IsSweeping);
            Assert.AreEqual(0, sweeper.ActiveSampleCount);
            Assert.AreEqual(0, sweeper.WallCount);
        }

        [Test]
        public void CancelSweep_RefusedWithNoActiveSweep()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsFalse(sweeper.CancelSweep());
            Assert.AreEqual(WallSweepRejection.NoSweepActive, sweeper.LastRejection);
        }

        [Test]
        public void UndoLastWall_RemovesOnlyTheMostRecentWall()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepWall(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, sweeper, new Vector2(4f, 0.2f), new Vector2(4f, 2.8f));

            Assert.AreEqual(2, sweeper.WallCount);
            Assert.IsTrue(sweeper.TryUndoLastWall(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.None, rejection);
            Assert.AreEqual(1, sweeper.WallCount);

            // The surviving wall is still the first one.
            Assert.AreEqual(0f, sweeper.Walls[0].Line.Point.z, Tolerance);
        }

        [Test]
        public void UndoLastWall_RefusedWithNoWalls()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            Assert.IsFalse(sweeper.TryUndoLastWall(out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.NoWallsToUndo, rejection);
        }

        [Test]
        public void ClearWalls_DiscardsEverythingButKeepsTheFrame()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepLegalRoom(provider, sweeper);
            GhostCoordinateFrame frame = sweeper.Frame;

            sweeper.ClearWalls();

            Assert.AreEqual(0, sweeper.WallCount);
            Assert.IsFalse(sweeper.IsSweeping);
            Assert.IsFalse(sweeper.IsComplete);
            Assert.AreSame(frame, sweeper.Frame);
        }

        // -------------------------------------------------------------------
        // Corner derivation
        // -------------------------------------------------------------------

        [Test]
        public void DeriveCorners_RefusedBeforeFourWalls()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepWall(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));

            Assert.IsFalse(sweeper.TryDeriveCorners(out _, out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.NotFourWalls, rejection);
            Assert.IsNotEmpty(sweeper.LastError);
        }

        /// <summary>
        /// The whole point of ADR-0005: four sweeps that never reached a corner
        /// still produce all four corners, because each is an intersection
        /// rather than an observation.
        /// </summary>
        [Test]
        public void DeriveCorners_ProducesTheRoomCornersFromFourSweeps()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepLegalRoom(provider, sweeper);

            Assert.IsTrue(
                sweeper.TryDeriveCorners(out Vector3[] corners, out WallSweepRejection rejection),
                $"{rejection}: {sweeper.LastError}");

            Assert.AreEqual(4, corners.Length);

            Assert.AreEqual(0f, corners[0].x, Tolerance);
            Assert.AreEqual(0f, corners[0].z, Tolerance);

            Assert.AreEqual(4f, corners[1].x, Tolerance);
            Assert.AreEqual(0f, corners[1].z, Tolerance);

            Assert.AreEqual(4f, corners[2].x, Tolerance);
            Assert.AreEqual(3f, corners[2].z, Tolerance);

            Assert.AreEqual(0f, corners[3].x, Tolerance);
            Assert.AreEqual(3f, corners[3].z, Tolerance);

            for (int i = 0; i < corners.Length; i++)
            {
                Assert.AreEqual(0f, corners[i].y, Tolerance, $"corner {i} y");
            }
        }

        [Test]
        public void DeriveCorners_RejectsFourParallelWalls()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepWall(provider, sweeper, new Vector2(0.2f, 0f), new Vector2(3.8f, 0f));
            SweepWall(provider, sweeper, new Vector2(0.2f, 1f), new Vector2(3.8f, 1f));
            SweepWall(provider, sweeper, new Vector2(0.2f, 2f), new Vector2(3.8f, 2f));
            SweepWall(provider, sweeper, new Vector2(0.2f, 3f), new Vector2(3.8f, 3f));

            Assert.AreEqual(4, sweeper.WallCount);
            Assert.IsFalse(sweeper.TryDeriveCorners(out _, out WallSweepRejection rejection));
            Assert.AreEqual(WallSweepRejection.DerivationFailed, rejection);
            Assert.IsNotEmpty(sweeper.LastError);
        }

        /// <summary>
        /// Derivation is geometric only. It does not know or care that the
        /// walls were swept in a hostile frame, so the Ghost coordinates it
        /// produces are a real test of the frame round-trip.
        /// </summary>
        [Test]
        public void DeriveCorners_IsUnaffectedByTheFrameOffsetAndYaw()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            SweepLegalRoom(provider, sweeper);
            Assert.IsTrue(sweeper.TryDeriveCorners(out Vector3[] corners, out _));

            // A 4 x 3 m footprint, whatever the world-space frame was.
            float width = Vector3.Distance(corners[0], corners[1]);
            float depth = Vector3.Distance(corners[1], corners[2]);

            Assert.AreEqual(4f, width, Tolerance);
            Assert.AreEqual(3f, depth, Tolerance);
        }

        // -------------------------------------------------------------------
        // No AR plane raycasts after the floor lock
        // -------------------------------------------------------------------

        /// <summary>
        /// ADR-0004 and plan section 1.2: capture after the floor lock must not
        /// depend on AR plane detection. Counting raycasts is how that stays
        /// true, exactly as the S3 tests do for walked corners.
        /// </summary>
        [Test]
        public void Sweeping_NeverAsksForAnArPlaneRaycast()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            int afterLock = provider.FloorHitRequestCount;

            SweepLegalRoom(provider, sweeper);
            Assert.IsTrue(sweeper.TryDeriveCorners(out _, out _));

            Assert.AreEqual(afterLock, provider.FloorHitRequestCount);
        }

        [Test]
        public void ProjectCrosshairToFloor_ReportsGhostPointAndRange()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = Sweeper(provider);

            AimAtGhost(provider, sweeper.Frame, 2f, 1.5f, standoffM: 2f);

            Assert.IsTrue(sweeper.TryProjectCrosshairToFloor(
                out Vector3 ghost, out float distanceM));

            Assert.AreEqual(2f, ghost.x, Tolerance);
            Assert.AreEqual(1.5f, ghost.z, Tolerance);
            Assert.AreEqual(0f, ghost.y, Tolerance);

            Assert.AreEqual(
                Mathf.Sqrt((1.5f * 1.5f) + (2f * 2f)), distanceM, 1e-2f);
        }

        [Test]
        public void ProjectCrosshairToFloor_RefusedBeforeFloorLock()
        {
            FakeSpatialProvider provider = Provider();
            WallSweepController sweeper = UnlockedSweeper(provider);

            Assert.IsFalse(sweeper.TryProjectCrosshairToFloor(out _, out _));
        }
    }
}
