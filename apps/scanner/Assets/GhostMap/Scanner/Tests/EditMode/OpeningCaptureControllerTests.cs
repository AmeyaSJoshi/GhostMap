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
    /// Task S5 Part 1: capturing a door or window on one of the four derived
    /// walls by intersecting the center-screen ray with the wall's plane at a
    /// lower-left then an upper-right point.
    ///
    /// <para>The fixture frame and room are the same deliberately hostile ones
    /// Task S3/S4 used: floor 1.4 m below Unity's origin, 40 degree yaw, a
    /// 3.0 m x 2.5 m footprint. A capture path that quietly assumed an
    /// axis-aligned room or forgot the frame would produce visibly wrong
    /// numbers rather than coincidentally right ones.</para>
    /// </summary>
    public sealed class OpeningCaptureControllerTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);
        private const float YawDeg = 40f;

        /// <summary>A legal 3.0 m x 2.5 m room, same fixture as S3/S4.</summary>
        private static readonly Vector2[] LegalRoom =
        {
            new Vector2(0f, 0f),
            new Vector2(3f, 0f),
            new Vector2(3f, 2.5f),
            new Vector2(0f, 2.5f)
        };

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
        /// Aims from roughly the room's center at a point on wall 0 (corners
        /// 0-&gt;1, at Ghost z = 0) with wall-local coordinates (u, v).
        /// </summary>
        private static void AimAtWall0(FakeSpatialProvider provider, GhostCoordinateFrame frame, float u, float v)
        {
            AimAtGhost(provider, frame, new Vector3(1.5f, 1.5f, 1.25f), new Vector3(u, v, 0f));
        }

        /// <summary>Locks the floor, captures the legal room, and captures a 2.5 m height on wall 0.</summary>
        private static OpeningCaptureController CapturedRoomWithHeight(
            FakeSpatialProvider provider,
            out CornerCaptureController corners,
            out FloorLockController floorLock,
            out HeightCaptureController height)
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

            height = new HeightCaptureController(provider, floorLock, corners);
            Assert.IsTrue(height.SelectWall(0));
            AimAtWall0(provider, floorLock.Frame, 1.5f, 2.5f);
            Assert.IsTrue(height.TryCaptureHeight(out _));

            return new OpeningCaptureController(provider, floorLock, corners, height);
        }

        private static OpeningCaptureController CapturedRoomWithHeight(FakeSpatialProvider provider)
            => CapturedRoomWithHeight(provider, out _, out _, out _);

        // -------------------------------------------------------------------
        // Wall derivation
        // -------------------------------------------------------------------

        [Test]
        public void WallPlaneIsDerivedFromTheCapturedRoomCorners()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider, out CornerCaptureController corners, out _, out _);

            Assert.AreEqual(4, capture.WallCount);

            WallDefinition wall0 = capture.Walls[0];
            Assert.AreEqual(corners.Corners[0].id, wall0.StartCornerId);
            Assert.AreEqual(corners.Corners[1].id, wall0.EndCornerId);
            Assert.That(wall0.LengthM, Is.EqualTo(3f).Within(Tolerance));
        }

        [Test]
        public void NoWallsExistBeforeAllFourCornersAreCaptured()
        {
            FakeSpatialProvider provider = Provider();
            var floorLock = new FloorLockController(provider);
            Assert.IsTrue(floorLock.TryLockFloor(out _));

            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var capture = new OpeningCaptureController(provider, floorLock, corners, height);

            Assert.AreEqual(0, capture.WallCount);
            Assert.IsFalse(capture.SelectWall(0));
        }

        // -------------------------------------------------------------------
        // Ray / wall-plane intersection
        // -------------------------------------------------------------------

        [Test]
        public void AValidRayHitsTheSelectedWallAtTheAimedPoint()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));

            AimAtWall0(provider, capture.Frame, 1.2f, 0.9f);

            Assert.IsTrue(capture.TryProjectCrosshairToWall(out Vector3 ghost));
            Assert.That(ghost.x, Is.EqualTo(1.2f).Within(Tolerance));
            Assert.That(ghost.y, Is.EqualTo(0.9f).Within(Tolerance));
        }

        [Test]
        public void RayParallelToTheWallIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider, out _, out FloorLockController floorLock, out _);
            Assert.IsTrue(capture.SelectWall(0));

            Vector3 eye = floorLock.Frame.GhostToWorld(new Vector3(0f, 1.5f, 1.25f));
            Vector3 direction = floorLock.Frame.GhostDirectionToWorld(new Vector3(1f, 0f, 0f));
            provider.ScreenRay = new Ray(eye, direction);

            Assert.IsFalse(capture.TryCaptureStartPoint(out OpeningCaptureRejection rejection));
            Assert.AreEqual(OpeningCaptureRejection.RayParallelToWall, rejection);
        }

        [Test]
        public void WallIntersectionBehindTheCameraIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider, out _, out FloorLockController floorLock, out _);
            Assert.IsTrue(capture.SelectWall(0));

            Vector3 eye = floorLock.Frame.GhostToWorld(new Vector3(1.5f, 1.5f, 1.25f));
            Vector3 direction = floorLock.Frame.GhostDirectionToWorld(new Vector3(0f, 0f, 1f));
            provider.ScreenRay = new Ray(eye, direction);

            Assert.IsFalse(capture.TryCaptureStartPoint(out OpeningCaptureRejection rejection));
            Assert.AreEqual(OpeningCaptureRejection.RayBehindCamera, rejection);
        }

        // -------------------------------------------------------------------
        // Wall-local coordinates and reversal
        // -------------------------------------------------------------------

        [Test]
        public void WallLocalOffsetIsMeasuredFromTheWallsStartCorner()
        {
            var wall = new WallDefinition("a", "b", new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f));

            WallGeometry.ToWallLocal(wall, new Vector3(1.2f, 0.9f, 0f), out float u, out float v);

            Assert.That(u, Is.EqualTo(1.2f).Within(Tolerance));
            Assert.That(v, Is.EqualTo(0.9f).Within(Tolerance));
        }

        /// <summary>
        /// The same physical opening, captured against the same physical wall
        /// described in reverse (start and end swapped), must resolve to a
        /// different wall-local offset — the mirror of the forward one, not a
        /// coincidentally identical number. A silent-mirroring bug would
        /// ignore which corner is declared "start" and always report the same
        /// offset regardless of direction.
        /// </summary>
        [Test]
        public void WallReversalDoesNotSilentlyMirrorAnOpeningOffset()
        {
            var forwardWall = new WallDefinition("a", "b", new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f));
            var reversedWall = new WallDefinition("b", "a", new Vector3(3f, 0f, 0f), new Vector3(0f, 0f, 0f));

            // The same physical point, 1.2 m from corner "a" along the wall.
            var physicalPoint = new Vector3(1.2f, 0.9f, 0f);

            WallGeometry.ToWallLocal(forwardWall, physicalPoint, out float forwardU, out _);
            WallGeometry.ToWallLocal(reversedWall, physicalPoint, out float reversedU, out _);

            Assert.That(forwardU, Is.EqualTo(1.2f).Within(Tolerance));

            // Measured from "b" instead, the same physical point is
            // (wall length - forward offset) along the wall: the mirror
            // image, not the same number.
            Assert.That(reversedU, Is.EqualTo(3f - 1.2f).Within(Tolerance));
            Assert.That(reversedU, Is.Not.EqualTo(forwardU).Within(Tolerance));
        }

        // -------------------------------------------------------------------
        // Two-point capture and validation
        // -------------------------------------------------------------------

        [Test]
        public void AValidDoorIsAccepted()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeDoor));

            AimAtWall0(provider, capture.Frame, 0.5f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));

            AimAtWall0(provider, capture.Frame, 1.5f, 2.05f);
            Assert.IsTrue(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.None, rejection);
            Assert.AreEqual(1, capture.OpeningCount);

            OpeningModel door = capture.Openings[0];
            Assert.AreEqual(OpeningValidator.TypeDoor, door.type);
            Assert.That(door.offsetM, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(door.widthM, Is.EqualTo(1.0f).Within(Tolerance));
            Assert.That(door.sillHeightM, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(door.heightM, Is.EqualTo(2.05f).Within(Tolerance));
        }

        [Test]
        public void AValidWindowIsAccepted()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeWindow));

            AimAtWall0(provider, capture.Frame, 0.5f, 0.9f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));

            AimAtWall0(provider, capture.Frame, 1.5f, 1.9f);
            Assert.IsTrue(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.None, rejection);
            Assert.AreEqual(1, capture.OpeningCount);

            OpeningModel window = capture.Openings[0];
            Assert.AreEqual(OpeningValidator.TypeWindow, window.type);
            Assert.That(window.offsetM, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(window.widthM, Is.EqualTo(1.0f).Within(Tolerance));
            Assert.That(window.sillHeightM, Is.EqualTo(0.9f).Within(Tolerance));
            Assert.That(window.heightM, Is.EqualTo(1.0f).Within(Tolerance));
        }

        [Test]
        public void ADoorExtendingOutsideTheWallIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeDoor));

            // Wall 0 is 3.0 m long; this spans 2.0-4.0 m.
            AimAtWall0(provider, capture.Frame, 2.0f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));

            AimAtWall0(provider, capture.Frame, 4.0f, 2.0f);
            Assert.IsFalse(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(0, capture.OpeningCount);
            Assert.IsNotEmpty(capture.LastError);
        }

        [Test]
        public void AWindowExtendingOutsideTheWallIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeWindow));

            AimAtWall0(provider, capture.Frame, -0.5f, 0.9f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));

            AimAtWall0(provider, capture.Frame, 0.5f, 1.9f);
            Assert.IsFalse(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(0, capture.OpeningCount);
        }

        [Test]
        public void AWindowWithASillBelowTheFloorIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeWindow));

            AimAtWall0(provider, capture.Frame, 0.5f, -0.1f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));

            AimAtWall0(provider, capture.Frame, 1.5f, 1.0f);
            Assert.IsFalse(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(0, capture.OpeningCount);
        }

        [Test]
        public void AnOpeningAboveTheCapturedCeilingIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeWindow));

            // Room height is 2.5 m; sill 2.0 + height 1.0 = top 3.0 m.
            AimAtWall0(provider, capture.Frame, 0.5f, 2.0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));

            AimAtWall0(provider, capture.Frame, 1.5f, 3.0f);
            Assert.IsFalse(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(0, capture.OpeningCount);
        }

        [Test]
        public void OverlappingOpeningsOnTheSameWallAreRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeDoor));

            AimAtWall0(provider, capture.Frame, 0.5f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));
            AimAtWall0(provider, capture.Frame, 1.5f, 2.05f);
            Assert.IsTrue(capture.TryCaptureEndPointAndCommit(out _));
            Assert.AreEqual(1, capture.OpeningCount);

            // 1.0-2.0 m overlaps the first door's 0.5-1.5 m span.
            AimAtWall0(provider, capture.Frame, 1.0f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));
            AimAtWall0(provider, capture.Frame, 2.0f, 2.05f);
            Assert.IsFalse(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(1, capture.OpeningCount, "The overlapping candidate must not have been appended.");
        }

        [Test]
        public void AnInvalidCaptureDoesNotMutatePreviouslyAcceptedOpenings()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeDoor));

            AimAtWall0(provider, capture.Frame, 0.5f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));
            AimAtWall0(provider, capture.Frame, 1.5f, 2.05f);
            Assert.IsTrue(capture.TryCaptureEndPointAndCommit(out _));

            OpeningModel acceptedBefore = capture.Openings[0];

            // Off the end of the wall: rejected.
            AimAtWall0(provider, capture.Frame, 2.9f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));
            AimAtWall0(provider, capture.Frame, 3.9f, 2.0f);
            Assert.IsFalse(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.ValidationFailed, rejection);
            Assert.AreEqual(1, capture.OpeningCount);
            Assert.AreEqual(acceptedBefore.id, capture.Openings[0].id);
            Assert.AreEqual(acceptedBefore.offsetM, capture.Openings[0].offsetM);
            Assert.AreEqual(acceptedBefore.widthM, capture.Openings[0].widthM);
        }

        [Test]
        public void EndPointCapturedWithNoPendingStartPointIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));

            AimAtWall0(provider, capture.Frame, 1.5f, 2.0f);
            Assert.IsFalse(capture.TryCaptureEndPointAndCommit(out OpeningCaptureRejection rejection));

            Assert.AreEqual(OpeningCaptureRejection.NoStartPointCaptured, rejection);
        }

        // -------------------------------------------------------------------
        // Undo
        // -------------------------------------------------------------------

        [Test]
        public void UndoRemovesTheMostRecentlyCapturedOpening()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeDoor));

            AimAtWall0(provider, capture.Frame, 0.5f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));
            AimAtWall0(provider, capture.Frame, 1.5f, 2.05f);
            Assert.IsTrue(capture.TryCaptureEndPointAndCommit(out _));

            Assert.IsTrue(capture.TryUndoLastOpening(out OpeningCaptureRejection rejection));
            Assert.AreEqual(OpeningCaptureRejection.None, rejection);
            Assert.AreEqual(0, capture.OpeningCount);
        }

        [Test]
        public void UndoWithNoOpeningsIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);

            Assert.IsFalse(capture.TryUndoLastOpening(out OpeningCaptureRejection rejection));
            Assert.AreEqual(OpeningCaptureRejection.NoOpeningsToUndo, rejection);
        }

        // -------------------------------------------------------------------
        // Frame / corner / height immutability
        // -------------------------------------------------------------------

        [Test]
        public void TheFrameCornersAndHeightAreUnchangedByOpeningCapture()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(
                provider, out CornerCaptureController corners, out FloorLockController floorLock, out HeightCaptureController height);

            GhostCoordinateFrame frame = floorLock.Frame;
            float heightBefore = height.HeightM;
            int cornerCountBefore = corners.CornerCount;

            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeDoor));
            AimAtWall0(provider, capture.Frame, 0.5f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));
            AimAtWall0(provider, capture.Frame, 1.5f, 2.05f);
            Assert.IsTrue(capture.TryCaptureEndPointAndCommit(out _));

            Assert.AreSame(frame, floorLock.Frame);
            Assert.That(height.HeightM, Is.EqualTo(heightBefore).Within(Tolerance));
            Assert.AreEqual(cornerCountBefore, corners.CornerCount);
        }

        // -------------------------------------------------------------------
        // Snapshot support
        // -------------------------------------------------------------------

        [Test]
        public void CopyOpeningsProducesIndependentValuesNotLiveReferences()
        {
            FakeSpatialProvider provider = Provider();
            OpeningCaptureController capture = CapturedRoomWithHeight(provider);
            Assert.IsTrue(capture.SelectWall(0));
            Assert.IsTrue(capture.SetType(OpeningValidator.TypeDoor));

            AimAtWall0(provider, capture.Frame, 0.5f, 0f);
            Assert.IsTrue(capture.TryCaptureStartPoint(out _));
            AimAtWall0(provider, capture.Frame, 1.5f, 2.05f);
            Assert.IsTrue(capture.TryCaptureEndPointAndCommit(out _));

            OpeningModel[] copy = capture.CopyOpenings();

            Assert.AreEqual(1, copy.Length);
            Assert.AreNotSame(capture.Openings[0], copy[0]);
            Assert.AreEqual(capture.Openings[0].id, copy[0].id);
            Assert.AreEqual(capture.Openings[0].offsetM, copy[0].offsetM);

            Assert.IsTrue(capture.TryUndoLastOpening(out _));
            Assert.AreEqual(1, copy.Length, "A previously copied array must survive an undo of the live list.");
        }
    }
}
