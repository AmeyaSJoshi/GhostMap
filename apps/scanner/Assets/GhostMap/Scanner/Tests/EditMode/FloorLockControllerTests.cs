using GhostMap.Scanner.Capture;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// The floor-lock rules from implementation plan Task S2, driven through a
    /// scripted spatial provider so every rejection path is verified without a
    /// phone.
    /// </summary>
    public sealed class FloorLockControllerTests
    {
        private const float Tolerance = 1e-4f;

        private static FakeSpatialProvider GoodProvider()
        {
            return new FakeSpatialProvider
            {
                IsTrackingGood = true,
                HasCameraPose = true,
                HasFloorHit = true,
                FloorHitAlignment = PlaneAlignment.HorizontalUp,
                FloorHitWorldPosition = new Vector3(0.4f, -1.45f, 2.2f),
                CameraPose = new Pose(
                    new Vector3(0.1f, 0.02f, 0.3f),
                    Quaternion.Euler(32f, 55f, 0f))
            };
        }

        [Test]
        public void GoodHorizontalHitLocksTheFloor()
        {
            var controller = new FloorLockController(GoodProvider());

            Assert.IsTrue(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.None, rejection);
            Assert.IsTrue(controller.IsLocked);
            Assert.IsNotNull(controller.Frame);
        }

        [Test]
        public void LockedFrameOriginIsTheFloorHit()
        {
            FakeSpatialProvider provider = GoodProvider();
            var controller = new FloorLockController(provider);

            Assert.IsTrue(controller.TryLockFloor(out _));

            Assert.That(
                Vector3.Distance(controller.Frame.Origin, provider.FloorHitWorldPosition),
                Is.LessThan(Tolerance));
            Assert.That(
                controller.Frame.FloorWorldY,
                Is.EqualTo(provider.FloorHitWorldPosition.y).Within(Tolerance));
        }

        /// <summary>
        /// The lock reads the crosshair, which is the center of the screen, not
        /// wherever the user last touched.
        /// </summary>
        [Test]
        public void LockRaycastsThroughTheCrosshair()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.CenterScreenPoint = new Vector2(640f, 1420f);
            var controller = new FloorLockController(provider);

            Assert.IsTrue(controller.TryLockFloor(out _));

            Assert.AreEqual(provider.CenterScreenPoint, provider.LastRaycastScreenPoint);
        }

        [Test]
        public void VerticalPlaneHitIsRejected()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.FloorHitAlignment = PlaneAlignment.Vertical;
            var controller = new FloorLockController(provider);

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.PlaneNotHorizontalUp, rejection);
            Assert.IsFalse(controller.IsLocked);
            Assert.IsNull(controller.Frame);
        }

        /// <summary>A ceiling is horizontal too, and locking it would invert the room.</summary>
        [Test]
        public void CeilingHitIsRejected()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.FloorHitAlignment = PlaneAlignment.HorizontalDown;
            var controller = new FloorLockController(provider);

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.PlaneNotHorizontalUp, rejection);
        }

        [Test]
        public void PoorTrackingIsRejected()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.IsTrackingGood = false;
            var controller = new FloorLockController(provider);

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.TrackingNotGood, rejection);
            Assert.IsFalse(controller.IsLocked);
        }

        [Test]
        public void PoorTrackingDisablesTheLockControl()
        {
            FakeSpatialProvider provider = GoodProvider();
            var controller = new FloorLockController(provider);

            Assert.IsTrue(controller.CanLock);

            provider.IsTrackingGood = false;
            Assert.IsFalse(controller.CanLock);
        }

        [Test]
        public void NoPlaneUnderTheCrosshairIsRejected()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.HasFloorHit = false;
            var controller = new FloorLockController(provider);

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.NoFloorHit, rejection);
        }

        [Test]
        public void MissingCameraPoseIsRejected()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.HasCameraPose = false;
            var controller = new FloorLockController(provider);

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.NoCameraPose, rejection);
        }

        /// <summary>
        /// Pointing straight down leaves nothing to project onto the floor, so
        /// the forward axis would be rounding noise. Refuse rather than lock a
        /// room to an arbitrary yaw.
        /// </summary>
        [Test]
        public void CameraAimedStraightDownIsRejected()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.CameraPose = new Pose(new Vector3(0f, 1.5f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var controller = new FloorLockController(provider);

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.DegenerateForward, rejection);
        }

        [Test]
        public void CameraAimedStraightUpIsRejected()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.CameraPose = new Pose(new Vector3(0f, 1.5f, 0f), Quaternion.Euler(-90f, 0f, 0f));
            var controller = new FloorLockController(provider);

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.DegenerateForward, rejection);
        }

        /// <summary>
        /// A realistic downward aim — 70 degrees below horizontal — must still
        /// succeed. The degenerate guard has to reject noise without rejecting
        /// the way people actually hold a phone over a floor.
        /// </summary>
        [Test]
        public void SteepButUsableDownwardAimStillLocks()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.CameraPose = new Pose(new Vector3(0f, 1.5f, 0f), Quaternion.Euler(70f, 130f, 0f));
            var controller = new FloorLockController(provider);

            Assert.IsTrue(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.None, rejection);

            // Yaw survives the steep pitch: forward still points along 130 deg.
            Vector3 expected = Quaternion.Euler(0f, 130f, 0f) * Vector3.forward;
            Assert.That(Vector3.Angle(controller.Frame.Forward, expected), Is.LessThan(0.1f));
        }

        [Test]
        public void ForwardProjectionProducesANormalizedFrame()
        {
            FakeSpatialProvider provider = GoodProvider();
            var controller = new FloorLockController(provider);

            Assert.IsTrue(controller.TryLockFloor(out _));

            GhostCoordinateFrame frame = controller.Frame;
            Assert.That(frame.Right.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(frame.Up.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(frame.Forward.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                Vector3.Dot(Vector3.Cross(frame.Right, frame.Up), frame.Forward),
                Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void SecondLockAttemptIsRefused()
        {
            var controller = new FloorLockController(GoodProvider());

            Assert.IsTrue(controller.TryLockFloor(out _));
            GhostCoordinateFrame first = controller.Frame;

            Assert.IsFalse(controller.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.AlreadyLocked, rejection);
            Assert.AreSame(first, controller.Frame, "The locked frame must never be replaced.");
        }

        [Test]
        public void LockControlIsDisabledOnceLocked()
        {
            var controller = new FloorLockController(GoodProvider());

            Assert.IsTrue(controller.TryLockFloor(out _));
            Assert.IsFalse(controller.CanLock);
        }

        /// <summary>
        /// The frame is taken from the floor hit and the camera aim at the lock
        /// instant. Afterwards the user walks the room, and neither the frame
        /// nor any coordinate already expressed in it may move.
        /// </summary>
        [Test]
        public void FrameSurvivesTheUserWalkingTheRoom()
        {
            FakeSpatialProvider provider = GoodProvider();
            var controller = new FloorLockController(provider);

            Assert.IsTrue(controller.TryLockFloor(out _));

            GhostCoordinateFrame frame = controller.Frame;
            Vector3 origin = frame.Origin;
            Vector3 right = frame.Right;
            Vector3 forward = frame.Forward;

            var probeWorld = new Vector3(2.4f, -1.45f, 3.9f);
            Vector3 ghostBefore = frame.WorldToGhost(probeWorld);

            // Walk, turn around, tilt up, and let the plane subsystem re-report
            // the floor a few centimetres away as its estimate refines.
            provider.CameraPose = new Pose(
                new Vector3(3.2f, 0.15f, -2.5f),
                Quaternion.Euler(-25f, 212f, 8f));
            provider.FloorHitWorldPosition = new Vector3(2.9f, -1.43f, -2.1f);

            Assert.AreSame(frame, controller.Frame);
            Assert.That(Vector3.Distance(controller.Frame.Origin, origin), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(controller.Frame.Right, right), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(controller.Frame.Forward, forward), Is.LessThan(Tolerance));

            Assert.That(
                Vector3.Distance(controller.Frame.WorldToGhost(probeWorld), ghostBefore),
                Is.LessThan(Tolerance));
        }

        /// <summary>
        /// The frame must come from the locked floor hit, not from the XR
        /// Origin, whose Camera Offset carries CameraYOffset. Locking a floor
        /// 1.45 m below the session origin must still put that floor at Ghost
        /// y = 0 and the camera at its true eye height above it.
        /// </summary>
        [Test]
        public void LockedFloorNormalizesToGhostZeroDespiteTheCameraOffset()
        {
            FakeSpatialProvider provider = GoodProvider();
            provider.FloorHitWorldPosition = new Vector3(0.4f, -1.45f, 2.2f);
            provider.CameraPose = new Pose(
                new Vector3(0.4f, 0.02f, 2.2f),
                Quaternion.Euler(32f, 0f, 0f));

            var controller = new FloorLockController(provider);
            Assert.IsTrue(controller.TryLockFloor(out _));

            Assert.That(
                controller.Frame.WorldToGhost(provider.FloorHitWorldPosition).y,
                Is.EqualTo(0f).Within(Tolerance));

            Assert.That(
                controller.Frame.WorldToGhost(provider.CameraPose.position).y,
                Is.EqualTo(1.47f).Within(1e-3f),
                "Eye height above the locked floor, not height above the session origin.");
        }
    }
}
