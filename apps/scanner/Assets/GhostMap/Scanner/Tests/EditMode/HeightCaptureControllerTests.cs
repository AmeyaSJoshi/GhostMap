using System.Collections.Generic;
using GhostMap.Scanner.Capture;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task S4 height capture: deriving vertical wall planes from the captured
    /// footprint, intersecting the center-screen ray with the selected wall,
    /// and validating the resulting height.
    ///
    /// <para>The fixture frame is the same deliberately hostile one Task S3
    /// used: floor 1.4 m below Unity's origin, 40 degree yaw. A capture path
    /// that quietly assumed an axis-aligned room, forgot the frame, or leaked
    /// a camera offset by hand would produce visibly wrong numbers rather than
    /// coincidentally right ones.</para>
    /// </summary>
    public sealed class HeightCaptureControllerTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);
        private const float YawDeg = 40f;

        /// <summary>A legal 3.0 m x 2.5 m room, same fixture as the S3 workflow tests.</summary>
        private static readonly Vector2[] LegalRoom =
        {
            new Vector2(0f, 0f),
            new Vector2(3f, 0f),
            new Vector2(3f, 2.5f),
            new Vector2(0f, 2.5f)
        };

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

        private static void AimAtGhost(
            FakeSpatialProvider provider,
            GhostCoordinateFrame frame,
            Vector3 ghostEye,
            Vector3 ghostTarget)
        {
            Vector3 target = frame.GhostToWorld(ghostTarget);
            Vector3 eye = frame.GhostToWorld(ghostEye);

            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        /// <summary>
        /// Aims from roughly the room's center at a point on wall 0 (the wall
        /// spanning corners 0-&gt;1, at Ghost z = 0) at the given height.
        /// </summary>
        private static void AimAtWall0(
            FakeSpatialProvider provider, GhostCoordinateFrame frame, float wallX, float heightM)
        {
            AimAtGhost(provider, frame, new Vector3(1.5f, 1.5f, 1.25f), new Vector3(wallX, heightM, 0f));
        }

        private static HeightCaptureController CapturedRoom(
            FakeSpatialProvider provider,
            out CornerCaptureController corners,
            out FloorLockController floorLock)
        {
            floorLock = new FloorLockController(provider);
            Assert.IsTrue(
                floorLock.TryLockFloor(out FloorLockRejection lockRejection),
                $"The fixture's own floor lock should succeed, got {lockRejection}.");

            corners = new CornerCaptureController(provider, floorLock);

            foreach (Vector2 corner in LegalRoom)
            {
                AimAtGhost(
                    provider, floorLock.Frame,
                    new Vector3(corner.x, 1.5f, corner.y - 1f),
                    new Vector3(corner.x, 0f, corner.y));

                Assert.IsTrue(
                    corners.TryCaptureCorner(out CornerCaptureRejection rejection),
                    $"Corner ({corner.x}, {corner.y}) should have been accepted, got " +
                    $"{rejection}: {corners.LastError}");
            }

            return new HeightCaptureController(provider, floorLock, corners);
        }

        private static HeightCaptureController CapturedRoom(FakeSpatialProvider provider)
            => CapturedRoom(provider, out _, out _);

        // -------------------------------------------------------------------
        // Wall derivation
        // -------------------------------------------------------------------

        [Test]
        public void WallsAreDerivedFromTheFourCapturedCornersInOrder()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider, out CornerCaptureController corners, out _);

            Assert.AreEqual(4, height.WallCount);

            IReadOnlyList<WallDefinition> walls = height.Walls;
            IReadOnlyList<CornerModel> stored = corners.Corners;

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(stored[i].id, walls[i].StartCornerId);
                Assert.AreEqual(stored[(i + 1) % 4].id, walls[i].EndCornerId);
            }
        }

        [Test]
        public void NoWallsExistBeforeAllFourCornersAreCaptured()
        {
            FakeSpatialProvider provider = Provider();
            var floorLock = new FloorLockController(provider);
            Assert.IsTrue(floorLock.TryLockFloor(out _));

            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);

            Assert.AreEqual(0, height.WallCount);
            Assert.IsFalse(height.SelectWall(0));
        }

        // -------------------------------------------------------------------
        // Ray / wall-plane intersection
        // -------------------------------------------------------------------

        [Test]
        public void AValidRayHitsTheSelectedWallAtTheAimedHeight()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);

            Assert.IsTrue(height.SelectWall(0));
            AimAtWall0(provider, height.Frame, 1.5f, 2.5f);

            Assert.IsTrue(height.TryProjectCrosshairToWall(out Vector3 ghostPoint));
            Assert.That(ghostPoint.y, Is.EqualTo(2.5f).Within(Tolerance));
        }

        [Test]
        public void ProjectionReportsWhetherTheAimIsWithinTheWallSpan()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));

            AimAtWall0(provider, height.Frame, 1.5f, 2.5f);
            Assert.IsTrue(height.TryProjectCrosshairToWall(out _, out bool withinSpan));
            Assert.IsTrue(withinSpan, "Aiming at the middle of the wall should read as within its span.");

            AimAtWall0(provider, height.Frame, 10f, 2.5f);
            Assert.IsTrue(height.TryProjectCrosshairToWall(out _, out bool outsideSpan));
            Assert.IsFalse(outsideSpan, "Aiming 7 m past the wall's end should not read as within its span.");
        }

        [Test]
        public void RayParallelToTheWallIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider, out _, out FloorLockController floorLock);

            // Wall 0 spans corners (0,0)-(3,0): its plane's normal lies along
            // Ghost Z. A ray traveling along the wall's own tangent (+X) never
            // approaches that plane.
            Assert.IsTrue(height.SelectWall(0));

            Vector3 eye = floorLock.Frame.GhostToWorld(new Vector3(0f, 1.5f, 1.25f));
            Vector3 direction = floorLock.Frame.GhostDirectionToWorld(new Vector3(1f, 0f, 0f));
            provider.ScreenRay = new Ray(eye, direction);

            Assert.IsFalse(height.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.RayParallelToWall, rejection);
        }

        [Test]
        public void WallIntersectionBehindTheCameraIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider, out _, out FloorLockController floorLock);

            Assert.IsTrue(height.SelectWall(0));

            // Standing mid-room and aiming further into the room (+Z, away
            // from wall 0 at z = 0) never reaches wall 0 going forward.
            Vector3 eye = floorLock.Frame.GhostToWorld(new Vector3(1.5f, 1.5f, 1.25f));
            Vector3 direction = floorLock.Frame.GhostDirectionToWorld(new Vector3(0f, 0f, 1f));
            provider.ScreenRay = new Ray(eye, direction);

            Assert.IsFalse(height.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.RayBehindCamera, rejection);
        }

        // -------------------------------------------------------------------
        // Height validation
        // -------------------------------------------------------------------

        [Test]
        public void AHeightOfExactlyTwoMetersIsAccepted()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));

            AimAtWall0(provider, height.Frame, 1.5f, 2.0f);

            Assert.IsTrue(height.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.None, rejection);
            Assert.That(height.HeightM, Is.EqualTo(2.0f).Within(Tolerance));
            Assert.IsTrue(height.HasCapturedHeight);
        }

        [Test]
        public void AHeightOfExactlyFourMetersIsAccepted()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));

            AimAtWall0(provider, height.Frame, 1.5f, 4.0f);

            Assert.IsTrue(height.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.None, rejection);
            Assert.That(height.HeightM, Is.EqualTo(4.0f).Within(Tolerance));
        }

        [Test]
        public void AHeightBelowTwoMetersIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));

            AimAtWall0(provider, height.Frame, 1.5f, 1.5f);

            Assert.IsFalse(height.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.ValidationFailed, rejection);
            Assert.IsFalse(height.HasCapturedHeight);
            Assert.IsNotEmpty(height.LastError);
        }

        [Test]
        public void AHeightAboveFourMetersIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));

            AimAtWall0(provider, height.Frame, 1.5f, 5.0f);

            Assert.IsFalse(height.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.ValidationFailed, rejection);
            Assert.IsFalse(height.HasCapturedHeight);
        }

        [Test]
        public void AnInvalidCaptureDoesNotMutateAPreviouslyConfirmedHeight()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));

            AimAtWall0(provider, height.Frame, 1.5f, 2.5f);
            Assert.IsTrue(height.TryCaptureHeight(out _));
            Assert.That(height.HeightM, Is.EqualTo(2.5f).Within(Tolerance));

            AimAtWall0(provider, height.Frame, 1.5f, 1.0f);
            Assert.IsFalse(height.TryCaptureHeight(out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.ValidationFailed, rejection);

            // The earlier valid capture must survive the later failed one.
            Assert.That(height.HeightM, Is.EqualTo(2.5f).Within(Tolerance));
            Assert.IsTrue(height.HasCapturedHeight);
        }

        // -------------------------------------------------------------------
        // Frame / corner immutability
        // -------------------------------------------------------------------

        [Test]
        public void TheFrameIsUnchangedByHeightCapture()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider, out _, out FloorLockController floorLock);

            GhostCoordinateFrame frame = floorLock.Frame;
            Assert.IsTrue(height.SelectWall(0));
            AimAtWall0(provider, height.Frame, 1.5f, 2.5f);
            Assert.IsTrue(height.TryCaptureHeight(out _));

            Assert.AreSame(frame, floorLock.Frame);
        }

        [Test]
        public void CapturedCornersAreUnchangedByHeightCapture()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height =
                CapturedRoom(provider, out CornerCaptureController corners, out _);

            var before = new List<Vector3>();
            foreach (CornerModel corner in corners.Corners)
            {
                before.Add(corner.position.ToVector3());
            }

            Assert.IsTrue(height.SelectWall(0));
            AimAtWall0(provider, height.Frame, 1.5f, 2.5f);
            Assert.IsTrue(height.TryCaptureHeight(out _));

            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i], corners.Corners[i].position.ToVector3());
            }
        }

        // -------------------------------------------------------------------
        // Winding / CameraYOffset independence
        // -------------------------------------------------------------------

        [Test]
        public void WallWindingDirectionDoesNotAffectTheIntersectedHeight()
        {
            // Same physical wall, corners in reversed order: the plane's
            // normal flips sign, but the intersection point it produces must
            // not, per RayPlaneMath's own contract that normal sign is
            // irrelevant.
            var forwardWall = new WallDefinition("a", "b", new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f));
            var reversedWall = new WallDefinition("b", "a", new Vector3(3f, 0f, 0f), new Vector3(0f, 0f, 0f));

            var ray = new Ray(new Vector3(1.5f, 1.5f, 1.25f), new Vector3(0f, 0f, -1f));

            Assert.IsTrue(
                RayPlaneMath.TryIntersectPlane(ray, WallGeometry.PlaneFor(forwardWall), out Vector3 forwardPoint));
            Assert.IsTrue(
                RayPlaneMath.TryIntersectPlane(ray, WallGeometry.PlaneFor(reversedWall), out Vector3 reversedPoint));

            Assert.That(reversedPoint.y, Is.EqualTo(forwardPoint.y).Within(Tolerance));
            Assert.That(reversedPoint.x, Is.EqualTo(forwardPoint.x).Within(Tolerance));
            Assert.That(reversedPoint.z, Is.EqualTo(forwardPoint.z).Within(Tolerance));
        }

        [Test]
        public void DifferentCameraOriginHeightsYieldTheSameCapturedHeight()
        {
            // Two rays aimed at the exact same physical wall point from eyes
            // at different heights — standing in for different CameraYOffset
            // values — must produce the same candidate height: nothing here
            // may add or subtract a camera offset by hand.
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));

            AimAtGhost(provider, height.Frame, new Vector3(1.5f, 1.2f, 1.25f), new Vector3(1.5f, 2.5f, 0f));
            Assert.IsTrue(height.TryProjectCrosshairToWall(out Vector3 firstPoint));

            AimAtGhost(provider, height.Frame, new Vector3(1.5f, 1.9f, 1.25f), new Vector3(1.5f, 2.5f, 0f));
            Assert.IsTrue(height.TryProjectCrosshairToWall(out Vector3 secondPoint));

            Assert.That(secondPoint.y, Is.EqualTo(firstPoint.y).Within(Tolerance));
            Assert.That(firstPoint.y, Is.EqualTo(2.5f).Within(Tolerance));
        }

        // -------------------------------------------------------------------
        // Manual fallback
        // -------------------------------------------------------------------

        [Test]
        public void ManualHeightWithinRangeIsAccepted()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);

            Assert.IsTrue(height.TrySetManualHeight(2.6f, out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.None, rejection);
            Assert.That(height.HeightM, Is.EqualTo(2.6f).Within(Tolerance));
            Assert.IsTrue(height.HasCapturedHeight);
            Assert.IsTrue(height.IsManualEntry);
        }

        [Test]
        public void ManualHeightOutsideRangeIsRejectedAndDoesNotMutateState()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);

            Assert.IsFalse(height.TrySetManualHeight(1.5f, out HeightCaptureRejection rejection));
            Assert.AreEqual(HeightCaptureRejection.ValidationFailed, rejection);
            Assert.IsFalse(height.HasCapturedHeight);
        }

        [Test]
        public void ManualHeightOverridesAnAutomaticCaptureAndIsFlaggedManual()
        {
            FakeSpatialProvider provider = Provider();
            HeightCaptureController height = CapturedRoom(provider);
            Assert.IsTrue(height.SelectWall(0));
            AimAtWall0(provider, height.Frame, 1.5f, 2.5f);
            Assert.IsTrue(height.TryCaptureHeight(out _));
            Assert.IsFalse(height.IsManualEntry);

            Assert.IsTrue(height.TrySetManualHeight(3.0f, out _));

            Assert.That(height.HeightM, Is.EqualTo(3.0f).Within(Tolerance));
            Assert.IsTrue(height.IsManualEntry);
        }
    }
}
