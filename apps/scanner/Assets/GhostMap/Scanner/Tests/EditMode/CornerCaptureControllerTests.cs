using System.Collections.Generic;
using GhostMap.Scanner.Capture;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task S3 corner capture: the floor-ray arithmetic, every validation rule
    /// that can refuse a corner, and closure verification.
    ///
    /// <para>The fixture frame is deliberately hostile. Its floor sits 1.4 m
    /// <em>below</em> Unity's origin and its yaw is 40 degrees, so a capture
    /// path that forgot the frame, forgot the floor's world Y, or quietly
    /// assumed an axis-aligned room would produce visibly wrong Ghost
    /// coordinates rather than coincidentally right ones.</para>
    ///
    /// <para>Corners are aimed at by constructing a real world-space ray from a
    /// plausible eye position through the intended floor point, so these
    /// exercise <see cref="RayPlaneMath"/> rather than stubbing past it.</para>
    /// </summary>
    public sealed class CornerCaptureControllerTests
    {
        private const float Tolerance = 1e-3f;

        /// <summary>Floor well away from world zero, so a dropped origin shows up.</summary>
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

        private static CornerCaptureController Capture(
            FakeSpatialProvider provider,
            out FloorLockController floorLock)
        {
            floorLock = new FloorLockController(provider);

            Assert.IsTrue(
                floorLock.TryLockFloor(out FloorLockRejection rejection),
                $"The fixture's own floor lock should succeed, got {rejection}.");

            return new CornerCaptureController(provider, floorLock);
        }

        private static CornerCaptureController Capture(FakeSpatialProvider provider)
            => Capture(provider, out _);

        /// <summary>
        /// Points the center-screen ray at a Ghost-space floor point, from an
        /// eye 1.5 m above the floor and 1 m short of the target — roughly how
        /// a phone is held when framing a corner.
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

        private static void CaptureAt(
            FakeSpatialProvider provider,
            CornerCaptureController capture,
            float ghostX,
            float ghostZ)
        {
            AimAtGhost(provider, capture.Frame, ghostX, ghostZ);

            Assert.IsTrue(
                capture.TryCaptureCorner(out CornerCaptureRejection rejection),
                $"Corner ({ghostX}, {ghostZ}) should have been accepted, got " +
                $"{rejection}: {capture.LastError}");
        }

        /// <summary>A legal 3.0 m x 2.5 m room: 7.5 m2, four right angles.</summary>
        private static readonly Vector2[] LegalRoom =
        {
            new Vector2(0f, 0f),
            new Vector2(3f, 0f),
            new Vector2(3f, 2.5f),
            new Vector2(0f, 2.5f)
        };

        private static CornerCaptureController CapturedLegalRoom(FakeSpatialProvider provider)
        {
            CornerCaptureController capture = Capture(provider);

            foreach (Vector2 corner in LegalRoom)
            {
                CaptureAt(provider, capture, corner.x, corner.y);
            }

            Assert.AreEqual(4, capture.CornerCount, "The legal room should have four corners.");
            return capture;
        }

        private static Vector3 GhostOf(CornerCaptureController capture, int index)
            => capture.Corners[index].position.ToVector3();

        // -------------------------------------------------------------------
        // Floor-ray intersection
        // -------------------------------------------------------------------

        /// <summary>
        /// The captured point is the ray/floor-plane intersection, computed
        /// against the locked frame's floor Y rather than world zero.
        /// </summary>
        [Test]
        public void CapturedCornerIsTheRayFloorPlaneIntersection()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            AimAtGhost(provider, capture.Frame, 1.25f, 2.75f);

            Assert.IsTrue(capture.TryCaptureCorner(out _));

            Vector3 ghost = GhostOf(capture, 0);

            Assert.That(ghost.x, Is.EqualTo(1.25f).Within(Tolerance));
            Assert.That(ghost.z, Is.EqualTo(2.75f).Within(Tolerance));
        }

        /// <summary>
        /// Corner capture must not need an AR plane raycast. Plane extents lag
        /// behind the room and stop at furniture, so requiring a plane hit at
        /// every corner would make real corners uncapturable.
        /// </summary>
        [Test]
        public void CaptureDoesNotUseAnArPlaneRaycast()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            int floorHitsAfterLock = provider.FloorHitRequestCount;

            CaptureAt(provider, capture, 0f, 0f);
            CaptureAt(provider, capture, 3f, 0f);

            Assert.AreEqual(
                floorHitsAfterLock,
                provider.FloorHitRequestCount,
                "Corner capture asked the AR plane raycaster for a hit.");

            Assert.AreEqual(2, provider.ScreenRayRequestCount);
        }

        [Test]
        public void CaptureUsesTheCenterScreenRay()
        {
            FakeSpatialProvider provider = Provider();
            provider.CenterScreenPoint = new Vector2(540f, 1200f);

            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);

            Assert.AreEqual(provider.CenterScreenPoint, provider.LastScreenRayPoint);
        }

        [Test]
        public void RayParallelToTheFloorIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            provider.ScreenRay = new Ray(
                FloorOrigin + new Vector3(0f, 1.5f, 0f),
                new Vector3(1f, 0f, 0f));

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.RayMissedFloor, rejection);
            Assert.AreEqual(0, capture.CornerCount);
        }

        [Test]
        public void RayAimedAwayFromTheFloorIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            // Above the floor, pointing up: the intersection is behind the ray.
            provider.ScreenRay = new Ray(
                FloorOrigin + new Vector3(0f, 1.5f, 0f),
                new Vector3(0f, 1f, 0.2f).normalized);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.RayMissedFloor, rejection);
        }

        // -------------------------------------------------------------------
        // Ghost Y
        // -------------------------------------------------------------------

        /// <summary>
        /// Every stored corner is exactly on the floor plane. The intersection
        /// already lands within float noise of Ghost Y = 0; this asserts the
        /// value is forced, because scene schema v1 requires Y = 0 and a
        /// residue of 1e-7 would accumulate through later wall math.
        /// </summary>
        [Test]
        public void CapturedCornerHasGhostYExactlyZero()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 12.5f, -9.25f);

            Assert.AreEqual(0f, GhostOf(capture, 0).y, "Corner Y must be forced to exactly zero.");
        }

        /// <summary>
        /// The forcing is not cosmetic. <c>origin + direction * t</c> is three
        /// roundings away from the plane it was solved for, and the residue
        /// scales with how far the ray travelled. A phone held a metre from the
        /// floor happens to round to exactly zero, which is why the cases below
        /// aim from progressively further out: at those magnitudes the raw
        /// intersection lands a fraction of a millimetre off the floor, and
        /// scene schema v1 asks for zero rather than nearly zero.
        /// </summary>
        [TestCase(1.5f, 1f)]
        [TestCase(12.5f, 7.25f)]
        [TestCase(137.77f, 211.31f)]
        [TestCase(1013.4f, 1777.9f)]
        public void GhostYIsForcedToZeroEvenWhenTheIntersectionCarriesResidue(
            float eyeHeightM,
            float eyeSetbackM)
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);
            GhostCoordinateFrame frame = capture.Frame;

            Vector3 target = frame.GhostToWorld(new Vector3(0.3f, 0f, 0.7f));
            Vector3 eye = frame.GhostToWorld(new Vector3(0.3f, eyeHeightM, 0.7f - eyeSetbackM));

            provider.ScreenRay = new Ray(eye, (target - eye).normalized);

            Assert.IsTrue(capture.TryProjectCrosshairToFloor(out Vector3 ghost));

            Assert.AreEqual(0f, ghost.y, "Ghost Y must be forced to exactly zero.");
        }

        [Test]
        public void EveryCornerOfACapturedRoomHasGhostYExactlyZero()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            for (int i = 0; i < capture.CornerCount; i++)
            {
                Assert.AreEqual(0f, GhostOf(capture, i).y, $"Corner {i} is off the floor plane.");
            }
        }

        // -------------------------------------------------------------------
        // Acceptance and ordering
        // -------------------------------------------------------------------

        [Test]
        public void FirstCornerIsAccepted()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            AimAtGhost(provider, capture.Frame, 0f, 0f);

            Assert.IsTrue(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.None, rejection);
            Assert.AreEqual(1, capture.CornerCount);
            Assert.IsEmpty(capture.LastError);
        }

        [Test]
        public void LegalFourCornerRoomIsAccepted()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            Assert.IsTrue(capture.IsComplete);
            Assert.AreEqual(CornerCaptureRejection.None, capture.LastRejection);
        }

        /// <summary>
        /// Corner order is load bearing: walls are derived from consecutive
        /// corners, so a reordered list silently reshapes the room.
        /// </summary>
        [Test]
        public void CornerOrderIsPreserved()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            for (int i = 0; i < LegalRoom.Length; i++)
            {
                Vector3 ghost = GhostOf(capture, i);

                Assert.That(ghost.x, Is.EqualTo(LegalRoom[i].x).Within(Tolerance), $"corner {i} x");
                Assert.That(ghost.z, Is.EqualTo(LegalRoom[i].y).Within(Tolerance), $"corner {i} z");
            }
        }

        [Test]
        public void EveryCornerGetsAUniqueId()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            var ids = new HashSet<string>();

            foreach (CornerModel corner in capture.Corners)
            {
                Assert.IsNotEmpty(corner.id, "A corner was stored without an id.");
                Assert.IsTrue(ids.Add(corner.id), $"Duplicate corner id '{corner.id}'.");
            }
        }

        [Test]
        public void AFifthCornerIsRefused()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            AimAtGhost(provider, capture.Frame, 1.5f, 1.2f);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.AllCornersCaptured, rejection);
            Assert.AreEqual(4, capture.CornerCount);
        }

        // -------------------------------------------------------------------
        // Validation rules
        // -------------------------------------------------------------------

        [Test]
        public void CornerTooCloseToThePreviousCornerIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);

            // 0.30 m apart, below the 0.50 m minimum spacing.
            AimAtGhost(provider, capture.Frame, 0.30f, 0f);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(1, capture.CornerCount, "A rejected corner must not be stored.");
            StringAssert.Contains("spacing", capture.LastError);
        }

        /// <summary>
        /// A bow tie: the closing corner crosses an earlier wall. This is the
        /// classic bad four-corner scan, and it is only detectable once the
        /// polygon closes.
        /// </summary>
        [Test]
        public void SelfIntersectingRoomIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);
            CaptureAt(provider, capture, 3f, 0f);
            CaptureAt(provider, capture, 0f, 2.5f);

            AimAtGhost(provider, capture.Frame, 3f, 2.5f);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(3, capture.CornerCount);
            StringAssert.Contains("cross", capture.LastError);
        }

        /// <summary>
        /// A 1 m square: every spacing rule passes, but 1.0 m2 is below the
        /// 2.0 m2 minimum, which only the whole-room check can see.
        /// </summary>
        [Test]
        public void RoomBelowTheMinimumAreaIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);
            CaptureAt(provider, capture, 1f, 0f);
            CaptureAt(provider, capture, 1f, 1f);

            AimAtGhost(provider, capture.Frame, 0f, 1f);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(3, capture.CornerCount);
            StringAssert.Contains("area", capture.LastError);
        }

        /// <summary>
        /// A 25 m wall exceeds the 20 m maximum. Nothing in the per-corner
        /// spacing rules catches it, so this also proves the whole-room check
        /// runs on the partial chain rather than only at the fourth corner.
        /// </summary>
        [Test]
        public void WallOverTheMaximumLengthIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);

            AimAtGhost(provider, capture.Frame, 25f, 0f);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(1, capture.CornerCount);
            StringAssert.Contains("maximum", capture.LastError);
        }

        /// <summary>
        /// A sliver quad whose corner at the origin closes to about 14 degrees,
        /// outside the 35-145 degree MVP range. Area, spacing, wall lengths and
        /// simplicity all pass, so the interior-angle rule is the only thing
        /// that can refuse it.
        /// </summary>
        [Test]
        public void ExtremeInteriorAngleIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);
            CaptureAt(provider, capture, 10f, 0f);
            CaptureAt(provider, capture, 10f, 1f);

            AimAtGhost(provider, capture.Frame, 0.8f, 0.2f);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(3, capture.CornerCount);
            StringAssert.Contains("angle", capture.LastError);
        }

        [Test]
        public void CaptureIsBlockedWhileTrackingIsPoor()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            provider.IsTrackingGood = false;
            AimAtGhost(provider, capture.Frame, 0f, 0f);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.TrackingNotGood, rejection);
            Assert.IsFalse(capture.CanCapture);
        }

        [Test]
        public void CaptureIsBlockedBeforeTheFloorIsLocked()
        {
            FakeSpatialProvider provider = Provider();
            var floorLock = new FloorLockController(provider);
            var capture = new CornerCaptureController(provider, floorLock);

            Assert.IsFalse(capture.TryCaptureCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.FloorNotLocked, rejection);
            Assert.IsNull(capture.Frame);
        }

        // -------------------------------------------------------------------
        // Undo
        // -------------------------------------------------------------------

        [Test]
        public void UndoRemovesTheLastCornerOnly()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);
            CaptureAt(provider, capture, 3f, 0f);

            string firstId = capture.Corners[0].id;

            Assert.IsTrue(capture.TryUndoLastCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.None, rejection);
            Assert.AreEqual(1, capture.CornerCount);
            Assert.AreEqual(firstId, capture.Corners[0].id);
        }

        [Test]
        public void UndoWithNoCornersIsRefused()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            Assert.IsFalse(capture.TryUndoLastCorner(out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.NoCornersToUndo, rejection);
        }

        [Test]
        public void UndoDiscardsAnyClosureMeasurement()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            AimAtGhost(provider, capture.Frame, 0.05f, 0f);
            Assert.IsTrue(capture.TryMeasureClosure(out _, out _));
            Assert.IsTrue(capture.HasClosureMeasurement);

            Assert.IsTrue(capture.TryUndoLastCorner(out _));

            Assert.IsFalse(capture.HasClosureMeasurement);
            Assert.AreEqual(0f, capture.ClosureErrorM);
        }

        // -------------------------------------------------------------------
        // Closure verification
        // -------------------------------------------------------------------

        [Test]
        public void ClosureCannotBeMeasuredBeforeFourCorners()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            CaptureAt(provider, capture, 0f, 0f);
            AimAtGhost(provider, capture.Frame, 0f, 0f);

            Assert.IsFalse(capture.TryMeasureClosure(out _, out CornerCaptureRejection rejection));
            Assert.AreEqual(CornerCaptureRejection.NotFourCorners, rejection);
        }

        /// <summary>
        /// Closure error is the horizontal distance between the stored first
        /// corner and the re-aimed verification point, in Ghost coordinates.
        /// </summary>
        [Test]
        public void ClosureErrorIsTheDistanceFromTheFirstCorner()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            // 0.06 m along X and 0.08 m along Z: a 3-4-5 triangle, 0.10 m.
            AimAtGhost(provider, capture.Frame, 0.06f, 0.08f);

            Assert.IsTrue(capture.TryMeasureClosure(out _, out _));

            Assert.That(capture.ClosureErrorM, Is.EqualTo(0.10f).Within(Tolerance));
        }

        /// <summary>
        /// Re-aiming at the same physical corner must land back on the stored
        /// first corner. This is the whole point of the check: it proves the
        /// frame has not drifted during capture.
        /// </summary>
        [Test]
        public void ReAimingAtTheFirstCornerLandsOnTheStoredFirstCorner()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            Vector3 first = GhostOf(capture, 0);

            AimAtGhost(provider, capture.Frame, first.x, first.z);

            Assert.IsTrue(capture.TryMeasureClosure(out ClosureQuality quality, out _));

            Assert.That(capture.ClosurePointGhost.x, Is.EqualTo(first.x).Within(Tolerance));
            Assert.That(capture.ClosurePointGhost.z, Is.EqualTo(first.z).Within(Tolerance));
            Assert.AreEqual(0f, capture.ClosurePointGhost.y);
            Assert.That(capture.ClosureErrorM, Is.EqualTo(0f).Within(Tolerance));
            Assert.AreEqual(ClosureQuality.Excellent, quality);
        }

        [Test]
        public void ClosureWithinEightCentimetresIsExcellent()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            AimAtGhost(provider, capture.Frame, 0.05f, 0f);

            Assert.IsTrue(capture.TryMeasureClosure(out ClosureQuality quality, out _));

            Assert.That(capture.ClosureErrorM, Is.EqualTo(0.05f).Within(Tolerance));
            Assert.AreEqual(ClosureQuality.Excellent, quality);
            Assert.IsTrue(capture.IsClosureAccepted);
        }

        [Test]
        public void ClosureBetweenEightAndFifteenCentimetresIsAcceptable()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            AimAtGhost(provider, capture.Frame, 0.12f, 0f);

            Assert.IsTrue(capture.TryMeasureClosure(out ClosureQuality quality, out _));

            Assert.That(capture.ClosureErrorM, Is.EqualTo(0.12f).Within(Tolerance));
            Assert.AreEqual(ClosureQuality.Acceptable, quality);
            Assert.IsTrue(capture.IsClosureAccepted);
        }

        [Test]
        public void ClosureBeyondFifteenCentimetresIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            AimAtGhost(provider, capture.Frame, 0.20f, 0f);

            Assert.IsTrue(
                capture.TryMeasureClosure(out ClosureQuality quality, out _),
                "A rejected closure is still a successful measurement.");

            Assert.That(capture.ClosureErrorM, Is.EqualTo(0.20f).Within(Tolerance));
            Assert.AreEqual(ClosureQuality.Rejected, quality);
            Assert.IsFalse(capture.IsClosureAccepted);
        }

        /// <summary>
        /// The band boundaries are inclusive. Asserted against the shared
        /// classifier directly, because aiming a ray at exactly 0.08 m would
        /// land a float ulp either side of the boundary.
        /// </summary>
        [Test]
        public void ClosureBandBoundariesAreInclusive()
        {
            Assert.AreEqual(ClosureQuality.Excellent, RoomValidator.ClassifyClosure(0.08f));
            Assert.AreEqual(ClosureQuality.Acceptable, RoomValidator.ClassifyClosure(0.15f));
            Assert.AreEqual(
                ClosureQuality.Rejected,
                RoomValidator.ClassifyClosure(0.15f + 0.001f));
        }

        [Test]
        public void RedoCornersClearsTheRoomAndTheClosure()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = CapturedLegalRoom(provider);

            AimAtGhost(provider, capture.Frame, 0.20f, 0f);
            Assert.IsTrue(capture.TryMeasureClosure(out _, out _));

            capture.ClearCorners();

            Assert.AreEqual(0, capture.CornerCount);
            Assert.IsFalse(capture.HasClosureMeasurement);
            Assert.AreEqual(0f, capture.ClosureErrorM);
        }

        // -------------------------------------------------------------------
        // Frame immutability
        // -------------------------------------------------------------------

        /// <summary>
        /// The S2 frame must survive every S3 capture untouched. If it moved,
        /// corners captured before the move would silently refer to a different
        /// room than corners captured after it, and closure would be measuring
        /// the drift rather than the user's aim.
        /// </summary>
        [Test]
        public void TheFrameIsUnchangedAcrossEveryCaptureAndTheClosureCheck()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider, out FloorLockController floorLock);

            GhostCoordinateFrame before = capture.Frame;
            Vector3 origin = before.Origin;
            Vector3 right = before.Right;
            Vector3 up = before.Up;
            Vector3 forward = before.Forward;
            float floorY = before.FloorWorldY;

            foreach (Vector2 corner in LegalRoom)
            {
                CaptureAt(provider, capture, corner.x, corner.y);

                Assert.AreSame(before, capture.Frame, "A capture replaced the locked frame.");
            }

            AimAtGhost(provider, capture.Frame, 0.05f, 0f);
            Assert.IsTrue(capture.TryMeasureClosure(out _, out _));

            Assert.AreSame(before, capture.Frame);
            Assert.AreSame(before, floorLock.Frame);

            Assert.AreEqual(origin, before.Origin);
            Assert.AreEqual(right, before.Right);
            Assert.AreEqual(up, before.Up);
            Assert.AreEqual(forward, before.Forward);
            Assert.AreEqual(floorY, before.FloorWorldY);
        }

        /// <summary>
        /// Moving the camera between corners — which is exactly what the user
        /// does — must not move the frame or retroactively shift a stored
        /// corner.
        /// </summary>
        [Test]
        public void WalkingBetweenCornersDoesNotMoveTheFrameOrStoredCorners()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider);

            GhostCoordinateFrame frame = capture.Frame;

            CaptureAt(provider, capture, 0f, 0f);
            Vector3 firstAfterCapture = GhostOf(capture, 0);

            provider.CameraPose = new Pose(
                FloorOrigin + new Vector3(3.4f, 1.6f, -2.1f),
                Quaternion.Euler(12f, 205f, 4f));

            CaptureAt(provider, capture, 3f, 0f);

            Assert.AreSame(frame, capture.Frame);
            Assert.AreEqual(firstAfterCapture, GhostOf(capture, 0));
        }

        /// <summary>
        /// The floor lock refuses a second lock, so nothing in the S3 UI can
        /// re-establish the frame mid-capture even if it tried.
        /// </summary>
        [Test]
        public void TheFloorCannotBeRelockedDuringCornerCapture()
        {
            FakeSpatialProvider provider = Provider();
            CornerCaptureController capture = Capture(provider, out FloorLockController floorLock);

            CaptureAt(provider, capture, 0f, 0f);

            GhostCoordinateFrame frame = floorLock.Frame;

            Assert.IsFalse(floorLock.TryLockFloor(out FloorLockRejection rejection));
            Assert.AreEqual(FloorLockRejection.AlreadyLocked, rejection);
            Assert.AreSame(frame, floorLock.Frame);
            Assert.IsFalse(floorLock.CanLock);
        }
    }
}
