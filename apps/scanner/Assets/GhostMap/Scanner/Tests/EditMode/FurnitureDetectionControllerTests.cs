using GhostMap.Scanner.AR;
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
    /// ADR-0006 furniture detection: every gate that can refuse a detected
    /// surface, the measurement derived from one that passes, and the
    /// accept/dismiss bookkeeping.
    ///
    /// <para>The fixture frame is the hostile one the S3 tests use — floor
    /// 1.4 m below Unity's origin, 40 degrees of yaw — so a detection path that
    /// forgot the frame produces visibly wrong Ghost coordinates rather than
    /// coincidentally right ones.</para>
    /// </summary>
    public sealed class FurnitureDetectionControllerTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);

        private const float YawDeg = 40f;

        /// <summary>A legal 4.0 m x 3.0 m room.</summary>
        private static readonly Vector2[] RoomCorners =
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(4f, 3f),
            new Vector2(0f, 3f)
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
                HasPlaneDetection = true,
                FloorHitAlignment = PlaneAlignment.HorizontalUp,
                FloorHitWorldPosition = FloorOrigin,
                CameraPose = new Pose(
                    FloorOrigin + new Vector3(0f, 1.5f, 0f),
                    Quaternion.Euler(30f, YawDeg, 0f))
            };
        }

        /// <summary>
        /// A detection controller over a locked floor and a complete room
        /// footprint, which is the only state in which detection runs.
        /// </summary>
        private static FurnitureDetectionController Detector(
            FakeSpatialProvider provider,
            out CornerCaptureController corners)
        {
            var floorLock = new FloorLockController(provider);

            Assert.IsTrue(
                floorLock.TryLockFloor(out FloorLockRejection rejection),
                $"The fixture's own floor lock should succeed, got {rejection}.");

            corners = new CornerCaptureController(provider, floorLock);

            for (int i = 0; i < RoomCorners.Length; i++)
            {
                AimAtGhost(provider, floorLock.Frame, RoomCorners[i].x, RoomCorners[i].y);

                Assert.IsTrue(
                    corners.TryCaptureCorner(out CornerCaptureRejection cornerRejection),
                    $"Fixture corner {i} should be accepted, got {cornerRejection}: {corners.LastError}");
            }

            Assert.IsTrue(corners.IsComplete);

            return new FurnitureDetectionController(provider, floorLock, corners);
        }

        private static FurnitureDetectionController Detector(FakeSpatialProvider provider)
            => Detector(provider, out _);

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

        private static ulong nextSurfaceId = 1;

        private static TrackableId NewSurfaceId()
            => new TrackableId(nextSurfaceId++, 0);

        /// <summary>
        /// A detected surface placed by Ghost-space description, so a test
        /// states "a 1.4 x 0.7 m top at 0.73 m, at (2, 1.5), square to the
        /// room" and the frame conversion is exercised for real.
        /// </summary>
        private static DetectedSurface Surface(
            GhostCoordinateFrame frame,
            float ghostX,
            float ghostZ,
            float ghostY,
            float widthM,
            float depthM,
            float yawDeg = 0f,
            PlaneAlignment alignment = PlaneAlignment.HorizontalUp)
        {
            Vector3 worldCenter = frame.GhostToWorld(new Vector3(ghostX, ghostY, ghostZ));

            // The spanning axis is specified as a Ghost-space yaw, so build it
            // there and take it to world. Ghost +Z is yaw 0, matching how
            // SceneObjectModel.yawDeg is applied as a rotation about +Y.
            Vector3 ghostAxis = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            Vector3 worldAxis = frame.GhostDirectionToWorld(ghostAxis);

            // DetectedSurface documents the plane's local +X as the spanning
            // axis and local +Y as the normal. LookRotation puts a direction on
            // local +Z, and Euler(0, -90, 0) maps +X onto +Z, so composing them
            // gives a rotation whose local +X is worldAxis.
            Quaternion worldRotation =
                Quaternion.LookRotation(worldAxis, Vector3.up)
                * Quaternion.Euler(0f, -90f, 0f);

            return new DetectedSurface(
                NewSurfaceId(),
                worldCenter,
                worldRotation,
                new Vector2(widthM, depthM),
                alignment);
        }

        /// <summary>A plausible desk top: 1.4 x 0.7 m at 0.73 m, mid-room.</summary>
        private static DetectedSurface DeskSurface(GhostCoordinateFrame frame)
            => Surface(frame, 2f, 1.5f, 0.73f, 1.4f, 0.7f);

        // -------------------------------------------------------------------
        // Preconditions
        // -------------------------------------------------------------------

        [Test]
        public void Refresh_RefusedBeforeFloorLock()
        {
            FakeSpatialProvider provider = Provider();
            provider.HasFloorHit = false;

            var floorLock = new FloorLockController(provider);
            Assert.IsFalse(floorLock.TryLockFloor(out _));

            var corners = new CornerCaptureController(provider, floorLock);
            var detector = new FurnitureDetectionController(provider, floorLock, corners);

            Assert.IsFalse(detector.Refresh(out FurnitureDetectionRejection rejection));
            Assert.AreEqual(FurnitureDetectionRejection.FloorNotLocked, rejection);
        }

        [Test]
        public void Refresh_RefusedWhenTrackingIsBad()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.IsTrackingGood = false;

            Assert.IsFalse(detector.Refresh(out FurnitureDetectionRejection rejection));
            Assert.AreEqual(FurnitureDetectionRejection.TrackingNotGood, rejection);
        }

        [Test]
        public void Refresh_RefusedWithoutACompleteRoomFootprint()
        {
            FakeSpatialProvider provider = Provider();

            var floorLock = new FloorLockController(provider);
            Assert.IsTrue(floorLock.TryLockFloor(out _));

            var corners = new CornerCaptureController(provider, floorLock);
            var detector = new FurnitureDetectionController(provider, floorLock, corners);

            Assert.IsFalse(detector.Refresh(out FurnitureDetectionRejection rejection));
            Assert.AreEqual(FurnitureDetectionRejection.NoRoomFootprint, rejection);
        }

        [Test]
        public void Refresh_RefusedWhenPlaneDetectionIsUnavailable()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.HasPlaneDetection = false;

            Assert.IsFalse(detector.Refresh(out FurnitureDetectionRejection rejection));
            Assert.AreEqual(FurnitureDetectionRejection.DetectionUnavailable, rejection);
        }

        [Test]
        public void CanDetect_RequiresFrameTrackingAndFootprint()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            Assert.IsTrue(detector.CanDetect);

            provider.IsTrackingGood = false;
            Assert.IsFalse(detector.CanDetect);
        }

        // -------------------------------------------------------------------
        // Measurement
        // -------------------------------------------------------------------

        /// <summary>
        /// The point of ADR-0006: the dimensions come from the surface, and the
        /// height is the surface's own height above the floor.
        /// </summary>
        [Test]
        public void Refresh_MeasuresADetectedDeskTop()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));

            Assert.IsTrue(detector.Refresh(out FurnitureDetectionRejection rejection), rejection.ToString());
            Assert.AreEqual(1, detector.CandidateCount);

            FurnitureCandidate candidate = detector.Candidates[0];

            Assert.AreEqual(2f, candidate.CenterGhost.x, Tolerance);
            Assert.AreEqual(1.5f, candidate.CenterGhost.z, Tolerance);
            Assert.AreEqual(0f, candidate.CenterGhost.y, Tolerance, "centre must sit on the floor plane");

            Assert.AreEqual(1.4f, candidate.WidthM, Tolerance);
            Assert.AreEqual(0.7f, candidate.DepthM, Tolerance);
            Assert.AreEqual(0.73f, candidate.HeightM, Tolerance, "height is the top surface's height");
        }

        [Test]
        public void Refresh_RecoversSurfaceYawThroughTheGhostFrame()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(
                Surface(detector.Frame, 2f, 1.5f, 0.75f, 1.2f, 0.6f, yawDeg: 30f));

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(1, detector.CandidateCount);

            // Yaw is only defined modulo 180 for a box, because the plane's
            // axis sign is arbitrary.
            float yaw = Mathf.Repeat(detector.Candidates[0].YawDeg, 180f);
            Assert.AreEqual(30f, yaw, 0.5f);
        }

        [Test]
        public void Refresh_ReportsHowManySurfacesItConsidered()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));
            provider.DetectedSurfaces.Add(
                Surface(detector.Frame, 1f, 1f, 0.0f, 4f, 3f));

            Assert.IsTrue(detector.Refresh(out _));

            Assert.AreEqual(2, detector.LastSurfaceCount);
            Assert.AreEqual(1, detector.CandidateCount, "the floor-level plane is not furniture");
        }

        // -------------------------------------------------------------------
        // Gates, each in isolation
        // -------------------------------------------------------------------

        [Test]
        public void Evaluate_RejectsAVerticalPlane()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface wall = Surface(
                detector.Frame, 2f, 1.5f, 0.75f, 1.2f, 0.6f,
                alignment: PlaneAlignment.Vertical);

            Assert.AreEqual(
                SurfaceVerdict.NotHorizontalUp,
                detector.TryEvaluate(wall, detector.Frame, out _));
        }

        [Test]
        public void Evaluate_RejectsADownwardFacingPlane()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface ceiling = Surface(
                detector.Frame, 2f, 1.5f, 0.75f, 1.2f, 0.6f,
                alignment: PlaneAlignment.HorizontalDown);

            Assert.AreEqual(
                SurfaceVerdict.NotHorizontalUp,
                detector.TryEvaluate(ceiling, detector.Frame, out _));
        }

        [Test]
        public void Evaluate_RejectsAPlaneAtFloorLevel()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface rug = Surface(detector.Frame, 2f, 1.5f, 0.01f, 1.5f, 1f);

            Assert.AreEqual(
                SurfaceVerdict.TooLow,
                detector.TryEvaluate(rug, detector.Frame, out _));
        }

        [Test]
        public void Evaluate_RejectsAPlaneAboveFurnitureHeight()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface shelf = Surface(detector.Frame, 2f, 1.5f, 2.2f, 1f, 0.4f);

            Assert.AreEqual(
                SurfaceVerdict.TooHigh,
                detector.TryEvaluate(shelf, detector.Frame, out _));
        }

        [Test]
        public void Evaluate_RejectsATinyPlane()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface clutter = Surface(detector.Frame, 2f, 1.5f, 0.75f, 0.12f, 0.12f);

            Assert.AreEqual(
                SurfaceVerdict.TooSmall,
                detector.TryEvaluate(clutter, detector.Frame, out _));
        }

        [Test]
        public void Evaluate_RejectsAPlaneLargerThanTheFurnitureRules()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface huge = Surface(
                detector.Frame, 2f, 1.5f, 0.75f,
                FurnitureValidator.MaxDimensionM + 1f, 1f);

            Assert.AreEqual(
                SurfaceVerdict.TooLarge,
                detector.TryEvaluate(huge, detector.Frame, out _));
        }

        /// <summary>
        /// A plane seen through a doorway belongs to the next room, and must not
        /// become furniture in this one.
        /// </summary>
        [Test]
        public void Evaluate_RejectsASurfaceOutsideTheRoomFootprint()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            // Refresh once so the footprint is loaded.
            Assert.IsTrue(detector.Refresh(out _));

            DetectedSurface nextRoom = Surface(detector.Frame, 7f, 1.5f, 0.75f, 1.2f, 0.6f);

            Assert.AreEqual(
                SurfaceVerdict.OutsideRoom,
                detector.TryEvaluate(nextRoom, detector.Frame, out _));
        }

        [Test]
        public void Evaluate_AcceptsASurfaceInsideTheRoomFootprint()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            Assert.IsTrue(detector.Refresh(out _));

            Assert.AreEqual(
                SurfaceVerdict.Accepted,
                detector.TryEvaluate(DeskSurface(detector.Frame), detector.Frame, out _));
        }

        // -------------------------------------------------------------------
        // Type selection
        // -------------------------------------------------------------------

        [Test]
        public void SetType_AcceptsEverySupportedType()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            for (int i = 0; i < FurnitureValidator.SupportedTypes.Count; i++)
            {
                string type = FurnitureValidator.SupportedTypes[i];
                Assert.IsTrue(detector.SetType(type), type);
                Assert.AreEqual(type, detector.SelectedType);
            }
        }

        [Test]
        public void SetType_RefusesAnUnsupportedType()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            Assert.IsFalse(detector.SetType("piano"));
            Assert.AreEqual(FurnitureDetectionRejection.UnsupportedType, detector.LastRejection);
        }

        // -------------------------------------------------------------------
        // Building an object
        // -------------------------------------------------------------------

        [Test]
        public void BuildSelected_UsesMeasuredDimensionsNotTypeDefaults()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));
            Assert.IsTrue(detector.Refresh(out _));
            Assert.IsTrue(detector.SetType("desk"));

            Assert.IsTrue(
                detector.TryBuildSelected(out SceneObjectModel model, out FurnitureDetectionRejection rejection),
                $"{rejection}: {detector.LastError}");

            Assert.AreEqual("desk", model.type);
            Assert.AreEqual(1.4f, model.widthM, Tolerance);
            Assert.AreEqual(0.7f, model.depthM, Tolerance);
            Assert.AreEqual(0.73f, model.heightM, Tolerance);
            Assert.IsNotEmpty(model.id);

            // And the shared defaults for a desk are something else entirely,
            // so this really did come from the measurement.
            Assert.IsTrue(FurnitureValidator.TryGetDefaultDimensions(
                "desk", out float defaultW, out float defaultD, out float defaultH));
            Assert.IsTrue(
                Mathf.Abs(defaultW - model.widthM) > Tolerance
                || Mathf.Abs(defaultD - model.depthM) > Tolerance
                || Mathf.Abs(defaultH - model.heightM) > Tolerance,
                "the fixture must not coincidentally equal the type defaults");
        }

        [Test]
        public void BuildSelected_ProducesAnObjectTheSharedValidatorAccepts()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));
            Assert.IsTrue(detector.Refresh(out _));
            Assert.IsTrue(detector.SetType("table"));
            Assert.IsTrue(detector.TryBuildSelected(out SceneObjectModel model, out _));

            ValidationResult result = FurnitureValidator.Validate(model);
            Assert.IsTrue(result.IsValid, result.Error);
        }

        [Test]
        public void BuildSelected_RefusedWithNoSelection()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(0, detector.CandidateCount);

            Assert.IsFalse(detector.TryBuildSelected(out _, out FurnitureDetectionRejection rejection));
            Assert.AreEqual(FurnitureDetectionRejection.NoCandidateSelected, rejection);
        }

        // -------------------------------------------------------------------
        // Accept / dismiss bookkeeping
        // -------------------------------------------------------------------

        /// <summary>
        /// ARKit grows a plane as it observes more of the surface, so the same
        /// trackable reappears every frame with a larger extent. It must not
        /// become a second object.
        /// </summary>
        [Test]
        public void MarkResolved_StopsASurfaceBeingOfferedAgain()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface desk = DeskSurface(detector.Frame);
            provider.DetectedSurfaces.Add(desk);

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(1, detector.CandidateCount);

            detector.MarkResolved(desk.Id);
            Assert.IsTrue(detector.IsResolved(desk.Id));
            Assert.AreEqual(0, detector.CandidateCount);

            // The plane is still being detected, and has grown.
            provider.DetectedSurfaces.Clear();
            provider.DetectedSurfaces.Add(new DetectedSurface(
                desk.Id, desk.WorldCenter, desk.WorldRotation,
                new Vector2(1.6f, 0.8f), desk.Alignment));

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(0, detector.CandidateCount, "a grown plane must not be offered twice");
        }

        [Test]
        public void DismissSelected_RemovesTheCandidateWithoutBuildingAnything()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));
            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(1, detector.CandidateCount);

            Assert.IsTrue(detector.DismissSelected());
            Assert.AreEqual(0, detector.CandidateCount);
            Assert.IsFalse(detector.HasSelection);
        }

        [Test]
        public void DismissSelected_RefusedWithNoSelection()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            Assert.IsFalse(detector.DismissSelected());
            Assert.AreEqual(FurnitureDetectionRejection.NoCandidateSelected, detector.LastRejection);
        }

        [Test]
        public void ClearResolved_OffersDismissedSurfacesAgain()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));
            Assert.IsTrue(detector.Refresh(out _));
            Assert.IsTrue(detector.DismissSelected());

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(0, detector.CandidateCount);

            detector.ClearResolved();

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(1, detector.CandidateCount);
        }

        // -------------------------------------------------------------------
        // Selection across refreshes
        // -------------------------------------------------------------------

        [Test]
        public void Refresh_KeepsTheSelectionByIdentityNotIndex()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            DetectedSurface first = Surface(detector.Frame, 1f, 1f, 0.45f, 0.8f, 0.8f);
            DetectedSurface second = Surface(detector.Frame, 3f, 2f, 0.73f, 1.4f, 0.7f);

            provider.DetectedSurfaces.Add(first);
            provider.DetectedSurfaces.Add(second);

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(2, detector.CandidateCount);
            Assert.IsTrue(detector.SelectCandidate(1));

            TrackableId selected = detector.Candidates[1].SurfaceId;

            // The planes come back in the opposite order next frame.
            provider.DetectedSurfaces.Clear();
            provider.DetectedSurfaces.Add(second);
            provider.DetectedSurfaces.Add(first);

            Assert.IsTrue(detector.Refresh(out _));
            Assert.IsTrue(detector.HasSelection);
            Assert.AreEqual(
                selected,
                detector.Candidates[detector.SelectedIndex].SurfaceId,
                "the same physical surface must stay selected");
        }

        [Test]
        public void SelectNextCandidate_CyclesAndWraps()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            provider.DetectedSurfaces.Add(Surface(detector.Frame, 1f, 1f, 0.45f, 0.8f, 0.8f));
            provider.DetectedSurfaces.Add(Surface(detector.Frame, 3f, 2f, 0.73f, 1.4f, 0.7f));

            Assert.IsTrue(detector.Refresh(out _));
            Assert.AreEqual(0, detector.SelectedIndex);

            Assert.IsTrue(detector.SelectNextCandidate());
            Assert.AreEqual(1, detector.SelectedIndex);

            Assert.IsTrue(detector.SelectNextCandidate());
            Assert.AreEqual(0, detector.SelectedIndex);
        }

        [Test]
        public void SelectNextCandidate_RefusedWithNoCandidates()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            Assert.IsTrue(detector.Refresh(out _));
            Assert.IsFalse(detector.SelectNextCandidate());
        }

        [Test]
        public void Refresh_SelectsTheFirstCandidateWhenNothingWasSelected()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider);

            Assert.IsFalse(detector.HasSelection);

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));
            Assert.IsTrue(detector.Refresh(out _));

            Assert.IsTrue(detector.HasSelection);
            Assert.AreEqual(0, detector.SelectedIndex);
        }

        // -------------------------------------------------------------------
        // The frame is read, never written
        // -------------------------------------------------------------------

        [Test]
        public void Detection_DoesNotDisturbTheLockedFrameOrTheFootprint()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureDetectionController detector = Detector(provider, out CornerCaptureController corners);

            GhostCoordinateFrame frame = detector.Frame;
            int cornerCount = corners.CornerCount;

            provider.DetectedSurfaces.Add(DeskSurface(detector.Frame));
            Assert.IsTrue(detector.Refresh(out _));
            Assert.IsTrue(detector.SetType("desk"));
            Assert.IsTrue(detector.TryBuildSelected(out _, out _));

            Assert.AreSame(frame, detector.Frame);
            Assert.AreEqual(cornerCount, corners.CornerCount);
        }
    }
}
