using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task R4 (ADR-0011): every rule between the detector's boxes and the
    /// furniture Add All creates. The detector itself is faked: a box's
    /// bottom-centre at screen point (100 x, 100 z) looks at Ghost floor point
    /// (x, z), so each test states where things are in the room.
    /// </summary>
    public sealed class FurnitureIdentificationTests
    {
        private const float Tolerance = 1e-3f;
        private const float PixelsPerMetre = 100f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);

        /// <summary>A legal 4.0 m x 3.0 m room.</summary>
        private static readonly Vector2[] RoomCorners =
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(4f, 3f),
            new Vector2(0f, 3f)
        };

        private static readonly IReadOnlyList<FurnitureCandidate> NoSurfaces = new FurnitureCandidate[0];
        private static readonly IReadOnlyList<SceneObjectModel> NoObjects = new SceneObjectModel[0];

        private static ulong nextSurfaceId = 9000;

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
                CameraPose = new Pose(FloorOrigin + new Vector3(0f, 1.5f, 0f), Quaternion.Euler(30f, 40f, 0f))
            };
        }

        private static void AimAtGhost(FakeSpatialProvider provider, GhostCoordinateFrame frame, float x, float z)
        {
            provider.ScreenRay = RayToFloor(frame, x, z);
        }

        private static Ray RayToFloor(GhostCoordinateFrame frame, float x, float z)
        {
            Vector3 target = frame.GhostToWorld(new Vector3(x, 0f, z));
            Vector3 eye = frame.GhostToWorld(new Vector3(x, 1.5f, z - 1f));
            return new Ray(eye, (target - eye).normalized);
        }

        /// <summary>From here on, screen point (100 x, 100 z) looks at floor point (x, z).</summary>
        private static void MapScreenToFloor(FakeSpatialProvider provider, GhostCoordinateFrame frame)
        {
            provider.ScreenRayAt = point => RayToFloor(frame, point.x / PixelsPerMetre, point.y / PixelsPerMetre);
        }

        private static FurnitureIdentificationController Identifier(
            FakeSpatialProvider provider, out GhostCoordinateFrame frame)
        {
            var floorLock = new FloorLockController(provider);
            Assert.IsTrue(floorLock.TryLockFloor(out FloorLockRejection rejection), rejection.ToString());

            var corners = new CornerCaptureController(provider, floorLock);
            foreach (Vector2 corner in RoomCorners)
            {
                AimAtGhost(provider, floorLock.Frame, corner.x, corner.y);
                Assert.IsTrue(corners.TryCaptureCorner(out _), corners.LastError);
            }

            Assert.IsTrue(corners.IsComplete);

            frame = floorLock.Frame;
            MapScreenToFloor(provider, frame);
            return new FurnitureIdentificationController(provider, floorLock, corners);
        }

        private static FurnitureIdentificationController Identifier(FakeSpatialProvider provider)
            => Identifier(provider, out _);

        /// <summary>Floor locked, no room yet: the state while the user turns to scan.</summary>
        private static FurnitureIdentificationController IdentifierBeforeRoom(
            FakeSpatialProvider provider, out GhostCoordinateFrame frame, out CornerCaptureController corners)
        {
            var floorLock = new FloorLockController(provider);
            Assert.IsTrue(floorLock.TryLockFloor(out FloorLockRejection rejection), rejection.ToString());
            corners = new CornerCaptureController(provider, floorLock);
            frame = floorLock.Frame;
            MapScreenToFloor(provider, frame);
            return new FurnitureIdentificationController(provider, floorLock, corners);
        }

        private static void CloseRoom(FakeSpatialProvider provider, GhostCoordinateFrame frame, CornerCaptureController corners)
        {
            provider.ScreenRayAt = null;
            foreach (Vector2 corner in RoomCorners)
            {
                AimAtGhost(provider, frame, corner.x, corner.y);
                Assert.IsTrue(corners.TryCaptureCorner(out _), corners.LastError);
            }

            Assert.IsTrue(corners.IsComplete);
            MapScreenToFloor(provider, frame);
        }

        /// <summary>A box whose bottom-centre looks at floor point (x, z).</summary>
        private static ObjectDetection Seen(string className, float x, float z, float confidence = 0.9f)
        {
            var box = new Rect(x * PixelsPerMetre - 30f, z * PixelsPerMetre, 60f, 80f);
            return new ObjectDetection(className, confidence, box);
        }

        private static List<ObjectDetection> Frame(params ObjectDetection[] detections)
            => new List<ObjectDetection>(detections);

        private static FurnitureCandidate DeskTop(float x = 2f, float z = 1.5f, float width = 1.4f, float depth = 0.7f,
            float height = 0.74f, float yaw = 0f)
            => new FurnitureCandidate(new TrackableId(nextSurfaceId++, 0), new Vector3(x, height, z), width, depth, height, yaw);

        /// <summary>Feeds the same frame <paramref name="count"/> times, 0.1 s apart from <paramref name="start"/>.</summary>
        private static void Repeat(
            FurnitureIdentificationController identifier,
            List<ObjectDetection> frame,
            int count,
            IReadOnlyList<FurnitureCandidate> surfaces = null,
            IReadOnlyList<SceneObjectModel> existing = null,
            float start = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(identifier.Update(frame, surfaces ?? NoSurfaces, existing ?? NoObjects, start + 0.1f * i));
            }
        }

        private static SceneObjectModel Existing(string type, float x, float z, float width, float depth)
        {
            return new SceneObjectModel
            {
                id = System.Guid.NewGuid().ToString(),
                type = type,
                center = Vec3Dto.FromVector3(new Vector3(x, 0f, z)),
                widthM = width,
                depthM = depth,
                heightM = 0.8f
            };
        }

        // -------------------------------------------------------------------
        // DetectionGeometry
        // -------------------------------------------------------------------

        [Test]
        public void UprightToRawImage_TurnsTheUprightImageBackToTheSensorImage()
        {
            AssertNear(new Vector2(1f, 1f), DetectionGeometry.UprightToRawImage(new Vector2(0f, 0f)));
            AssertNear(new Vector2(1f, 0f), DetectionGeometry.UprightToRawImage(new Vector2(1f, 0f)));
            AssertNear(new Vector2(0f, 1f), DetectionGeometry.UprightToRawImage(new Vector2(0f, 1f)));
            AssertNear(new Vector2(0f, 0f), DetectionGeometry.UprightToRawImage(new Vector2(1f, 1f)));
        }

        [Test]
        public void VisionBoxToScreen_WithThePortraitDisplayTransform_KeepsTheBoxWhereVisionSawIt()
        {
            // ARKit's displayTransform for a portrait full-screen view.
            var portrait = new DisplayTransform(0f, 1f, -1f, 0f, 1f, 0f);
            var screen = new Vector2(1170f, 2532f);

            Rect box = DetectionGeometry.VisionBoxToScreen(new Rect(0.1f, 0.2f, 0.3f, 0.4f), portrait, screen);

            Assert.AreEqual(0.1f * screen.x, box.xMin, 0.01f);
            Assert.AreEqual(0.2f * screen.y, box.yMin, 0.01f);
            Assert.AreEqual(0.3f * screen.x, box.width, 0.01f);
            Assert.AreEqual(0.4f * screen.y, box.height, 0.01f);
        }

        [Test]
        public void VisionBoxToScreen_AppliesTheTransformsScaleAndOffset()
        {
            // An aspect-fill crop: the view shows the middle half of the image width.
            var cropped = new DisplayTransform(0f, 1f, -2f, 0f, 1.5f, 0f);

            Rect box = DetectionGeometry.VisionBoxToScreen(new Rect(0.25f, 0f, 0.5f, 1f), cropped, new Vector2(100f, 200f));

            Assert.AreEqual(0f, box.xMin, 0.01f);
            Assert.AreEqual(100f, box.xMax, 0.01f);
            Assert.AreEqual(0f, box.yMin, 0.01f);
            Assert.AreEqual(200f, box.yMax, 0.01f);
        }

        [Test]
        public void BottomCentre_IsTheMiddleOfTheBoxesLowerEdge()
        {
            AssertNear(new Vector2(40f, 20f), DetectionGeometry.BottomCentre(new Rect(10f, 20f, 60f, 80f)));
        }

        // -------------------------------------------------------------------
        // FurnitureClassMap
        // -------------------------------------------------------------------

        [TestCase("bed", "bed")]
        [TestCase("chair", "chair")]
        [TestCase("couch", "couch")]
        [TestCase("tv", "tv")]
        [TestCase("refrigerator", "generic")]
        [TestCase("potted plant", "generic")]
        [TestCase("toilet", "generic")]
        [TestCase("sink", "generic")]
        [TestCase("oven", "generic")]
        [TestCase("microwave", "generic")]
        [TestCase("bench", "generic")]
        public void ClassMap_MapsEachFurnitureClass(string className, string type)
        {
            Assert.IsTrue(FurnitureClassMap.IsFurniture(className));
            Assert.IsTrue(FurnitureClassMap.TryMap(className, null, out string mapped));
            Assert.AreEqual(type, mapped);
            Assert.IsTrue(FurnitureValidator.IsSupportedType(mapped));
        }

        [TestCase("person")]
        [TestCase("laptop")]
        [TestCase("cup")]
        [TestCase("")]
        [TestCase(null)]
        public void ClassMap_IgnoresEverythingElse(string className)
        {
            Assert.IsFalse(FurnitureClassMap.IsFurniture(className));
            Assert.IsFalse(FurnitureClassMap.TryMap(className, null, out _));
        }

        [Test]
        public void ClassMap_DiningTableWithoutASurface_IsATable()
        {
            Assert.IsTrue(FurnitureClassMap.TryMap("dining table", null, out string type));
            Assert.AreEqual("table", type);
        }

        [TestCase(0.74f, 1.4f, 0.7f, "desk")]
        [TestCase(0.74f, 0.7f, 1.4f, "desk")]
        [TestCase(0.65f, 1.2f, 0.6f, "desk")]
        [TestCase(0.80f, 1.2f, 0.6f, "desk")]
        [TestCase(0.74f, 1.2f, 0.80f, "desk")]
        [TestCase(0.64f, 1.2f, 0.6f, "table")]
        [TestCase(0.81f, 1.2f, 0.6f, "table")]
        [TestCase(0.74f, 1.5f, 0.81f, "table")]
        [TestCase(0.45f, 0.5f, 0.5f, "table")]
        public void ClassMap_DiningTableOnASurface_IsADeskOnlyWhenDeskShaped(
            float height, float width, float depth, string expected)
        {
            Assert.IsTrue(FurnitureClassMap.TryMap("dining table", DeskTop(width: width, depth: depth, height: height), out string type));
            Assert.AreEqual(expected, type);
        }

        // -------------------------------------------------------------------
        // Preconditions
        // -------------------------------------------------------------------

        [Test]
        public void Update_IsRefusedWhileTrackingIsPoor()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureIdentificationController identifier = Identifier(provider);
            provider.IsTrackingGood = false;

            Assert.IsFalse(identifier.Update(Frame(Seen("chair", 1f, 1f)), NoSurfaces, NoObjects, 0f));

            Assert.AreEqual(0, identifier.FramesProcessed);
            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        [Test]
        public void Update_IsRefusedBeforeFloorLock()
        {
            FakeSpatialProvider provider = Provider();
            var floorLock = new FloorLockController(provider);
            var identifier = new FurnitureIdentificationController(
                provider, floorLock, new CornerCaptureController(provider, floorLock));

            Assert.IsFalse(identifier.Update(Frame(Seen("chair", 1f, 1f)), NoSurfaces, NoObjects, 0f));
            Assert.AreEqual(0, identifier.FramesProcessed);
        }

        // -------------------------------------------------------------------
        // From floor lock: furniture found while the room is being scanned
        // -------------------------------------------------------------------

        [Test]
        public void BeforeTheRoomCloses_FurnitureIsAlreadyCollected()
        {
            FurnitureIdentificationController identifier = IdentifierBeforeRoom(Provider(), out _, out _);

            // Even where the room will turn out not to be: there is no room yet.
            Repeat(identifier, Frame(Seen("bed", 1f, 0.5f), Seen("chair", -1f, 1f)), 5);

            Assert.AreEqual(2, identifier.Proposals.Count);
        }

        [Test]
        public void OnceTheRoomCloses_FurnitureOutsideItIsDropped()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureIdentificationController identifier =
                IdentifierBeforeRoom(provider, out GhostCoordinateFrame frame, out CornerCaptureController corners);
            Repeat(identifier, Frame(Seen("bed", 1f, 0.5f), Seen("chair", -1f, 1f)), 5);

            CloseRoom(provider, frame, corners);
            Assert.IsTrue(identifier.Update(Frame(), NoSurfaces, NoObjects, 1f));

            Assert.AreEqual(1, identifier.Proposals.Count);
            Assert.AreEqual("bed", identifier.Proposals[0].Type);
        }

        [Test]
        public void FurnitureFurtherThanTheMaximumRange_IsIgnored()
        {
            FurnitureIdentificationController identifier = IdentifierBeforeRoom(Provider(), out _, out _);

            Repeat(identifier, Frame(Seen("bed", 0f, FurnitureIdentificationController.MaxRangeM + 0.5f)), 5);

            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        [Test]
        public void ATableSeenBeforeSurfacesExist_TakesItsSurfaceOnceMeasured()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("dining table", 2f, 1.1f)), 5);
            Assert.AreEqual("table", identifier.Proposals[0].Type);
            Assert.IsFalse(identifier.Proposals[0].Surface.HasValue);

            // Out of view now, but the furniture step has measured its top.
            FurnitureCandidate desk = DeskTop();
            Assert.IsTrue(identifier.Update(Frame(), new[] { desk }, NoObjects, 5f));

            IdentifiedFurniture item = identifier.Proposals[0];
            Assert.AreEqual("desk", item.Type);
            Assert.AreEqual(desk.SurfaceId, item.Surface.Value.SurfaceId);
            Assert.AreEqual(1.5f, item.CenterGhost.z, Tolerance);
        }

        // -------------------------------------------------------------------
        // What counts as a sighting
        // -------------------------------------------------------------------

        [Test]
        public void AChair_IsProposedOnItsFifthFrame_NotBefore()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            List<ObjectDetection> frame = Frame(Seen("chair", 1f, 1f));

            Repeat(identifier, frame, FurnitureIdentificationController.MinHits - 1);
            Assert.AreEqual(1, identifier.Tracked.Count);
            Assert.AreEqual(0, identifier.Proposals.Count);

            Assert.IsTrue(identifier.Update(frame, NoSurfaces, NoObjects, 1f));
            Assert.AreEqual(1, identifier.Proposals.Count);
            Assert.AreEqual("chair", identifier.Proposals[0].Type);
            Assert.AreEqual(FurnitureIdentificationController.MinHits, identifier.FramesProcessed);
        }

        [Test]
        public void ALowConfidenceDetection_IsIgnored()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("chair", 1f, 1f, 0.49f)), 10);

            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        [Test]
        public void SomethingThatIsNotFurniture_IsIgnored()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("person", 1f, 1f), Seen("laptop", 2f, 2f)), 10);

            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        [Test]
        public void ABoxWhoseBottomIsAboveTheHorizon_IsIgnored()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureIdentificationController identifier = Identifier(provider, out GhostCoordinateFrame frame);
            Vector3 eye = frame.GhostToWorld(new Vector3(2f, 1.5f, 1f));
            provider.ScreenRayAt = _ => new Ray(eye, frame.GhostDirectionToWorld(new Vector3(0f, 0.2f, 1f).normalized));

            Repeat(identifier, Frame(Seen("bed", 2f, 2f)), 10);

            Assert.AreEqual(0, identifier.Tracked.Count);
            Assert.AreEqual(10, identifier.FramesProcessed);
        }

        [TestCase(-0.5f, 1f)]
        [TestCase(4.5f, 1f)]
        [TestCase(2f, 3.4f)]
        public void FurnitureOutsideTheRoom_IsIgnored(float x, float z)
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("chair", x, z)), 10);

            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        // -------------------------------------------------------------------
        // Where it is
        // -------------------------------------------------------------------

        [Test]
        public void WithoutASurface_TheCentreIsHalfADefaultDepthBeyondTheFloorPoint()
        {
            FakeSpatialProvider provider = Provider();
            FurnitureIdentificationController identifier = Identifier(provider, out GhostCoordinateFrame frame);

            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), 1);

            Vector3 forward = frame.WorldDirectionToGhost(provider.CameraPose.rotation * Vector3.forward);
            forward.y = 0f;
            Assert.IsTrue(FurnitureValidator.TryGetDefaultDimensions("chair", out _, out float depth, out _));
            Vector3 expected = new Vector3(1f, 0f, 1f) + forward.normalized * (depth * 0.5f);

            Vector3 center = identifier.Tracked[0].CenterGhost;
            Assert.AreEqual(expected.x, center.x, Tolerance);
            Assert.AreEqual(expected.z, center.z, Tolerance);
            Assert.IsFalse(identifier.Tracked[0].Surface.HasValue);
        }

        [Test]
        public void ADiningTableOnADeskTop_IsADeskAtTheSurface()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            FurnitureCandidate desk = DeskTop();

            // Seen from in front: the box's bottom edge meets the floor at the desk's front edge.
            Repeat(identifier, Frame(Seen("dining table", 2f, 1.1f)), 5, new[] { desk });

            IdentifiedFurniture item = identifier.Proposals[0];
            Assert.AreEqual("desk", item.Type);
            Assert.AreEqual("dining table", item.ClassName);
            Assert.IsTrue(item.Surface.HasValue);
            Assert.AreEqual(desk.SurfaceId, item.Surface.Value.SurfaceId);
            Assert.AreEqual(2f, item.CenterGhost.x, Tolerance);
            Assert.AreEqual(1.5f, item.CenterGhost.z, Tolerance);
        }

        [Test]
        public void ASightingWithinTheMarginOfASurface_MatchesIt_OneFurtherDoesNot()
        {
            FurnitureCandidate desk = DeskTop();
            float edge = 2f + 0.7f;

            // Beside it (along x), so the estimated centre does not land on it either.
            FurnitureIdentificationController near = Identifier(Provider());
            Repeat(near, Frame(Seen("dining table", edge + 0.28f, 1.5f)), 1, new[] { desk });
            Assert.IsTrue(near.Tracked[0].Surface.HasValue);

            FurnitureIdentificationController far = Identifier(Provider());
            Repeat(far, Frame(Seen("dining table", edge + 0.32f, 1.5f)), 1, new[] { desk });
            Assert.IsFalse(far.Tracked[0].Surface.HasValue);
            Assert.AreEqual("table", far.Tracked[0].Type);
        }

        [Test]
        public void ASurfaceIsMatchedAlongItsOwnRotatedAxes()
        {
            // A 1.4 x 0.7 top turned 90 degrees is 0.7 wide in x.
            FurnitureCandidate turned = DeskTop(yaw: 90f);

            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("dining table", 2f + 0.35f + 0.5f, 1.5f)), 1, new[] { turned });

            Assert.IsFalse(identifier.Tracked[0].Surface.HasValue);
        }

        [Test]
        public void OfTwoSurfacesInReach_TheNearestIsMatched()
        {
            FurnitureCandidate left = DeskTop(x: 1.2f, width: 0.6f, depth: 0.6f);
            FurnitureCandidate right = DeskTop(x: 2.0f, width: 0.6f, depth: 0.6f);

            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("dining table", 1.75f, 1.5f)), 1, new[] { left, right });

            Assert.AreEqual(right.SurfaceId, identifier.Tracked[0].Surface.Value.SurfaceId);
        }

        [Test]
        public void AChairBesideADesk_DoesNotTakeTheDesksTop()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("chair", 2f, 1.0f)), 1, new[] { DeskTop() });

            Assert.IsFalse(identifier.Tracked[0].Surface.HasValue);
        }

        [Test]
        public void AChairOnItsSeat_TakesTheSeatsFootprint_ButKeepsAChairsHeight()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            FurnitureCandidate seat = DeskTop(x: 1f, z: 1f, width: 0.45f, depth: 0.42f, height: 0.46f, yaw: 15f);

            Repeat(identifier, Frame(Seen("chair", 1f, 0.8f)), 5, new[] { seat });

            Assert.IsTrue(FurnitureIdentificationController.TryBuild(identifier.Proposals[0], out SceneObjectModel model, out string error), error);
            Assert.IsTrue(FurnitureValidator.TryGetDefaultDimensions("chair", out _, out _, out float chairHeight));
            Assert.AreEqual(0.45f, model.widthM, Tolerance);
            Assert.AreEqual(0.42f, model.depthM, Tolerance);
            Assert.AreEqual(15f, model.yawDeg, Tolerance);
            Assert.AreEqual(chairHeight, model.heightM, Tolerance);
        }

        [TestCase("dining table", 0.74f, true)]
        [TestCase("dining table", 0.45f, false)]
        [TestCase("chair", 0.45f, true)]
        [TestCase("chair", 0.74f, false)]
        [TestCase("couch", 0.42f, true)]
        [TestCase("bed", 0.55f, true)]
        [TestCase("tv", 0.74f, false)]
        [TestCase("refrigerator", 0.9f, false)]
        public void ClassMap_SurfaceHeightsEachClassCanSitUnder(string className, float height, bool expected)
        {
            Assert.AreEqual(expected, FurnitureClassMap.CanSitUnder(className, height));
        }

        // -------------------------------------------------------------------
        // Tracking across frames
        // -------------------------------------------------------------------

        [Test]
        public void AJitteringChair_StaysOneChair()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            for (int i = 0; i < 10; i++)
            {
                float jitter = (i % 2 == 0 ? 1f : -1f) * 0.1f;
                Assert.IsTrue(identifier.Update(Frame(Seen("chair", 1f + jitter, 1f - jitter)), NoSurfaces, NoObjects, 0.1f * i));
            }

            Assert.AreEqual(1, identifier.Tracked.Count);
            Assert.AreEqual(10, identifier.Tracked[0].Hits);
        }

        [Test]
        public void TwoChairsAMetreApart_StayTwoChairs()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("chair", 1f, 1f), Seen("chair", 2f, 1f)), 5);

            Assert.AreEqual(2, identifier.Proposals.Count);
        }

        [Test]
        public void TwoSightingsInOneFrame_AreNeverFoldedIntoOneTrack()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("chair", 1f, 1f), Seen("chair", 1.3f, 1f)), 1);

            Assert.AreEqual(2, identifier.Tracked.Count);
        }

        [Test]
        public void DifferentTypesInTheSamePlace_AreSeparateTracks()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), 1);
            Repeat(identifier, Frame(Seen("couch", 1f, 1f)), 1, start: 0.1f);

            Assert.AreEqual(2, identifier.Tracked.Count);
        }

        [Test]
        public void ASightingNotYetProposed_IsForgottenAfterThreeSecondsUnseen()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), FurnitureIdentificationController.MinHits - 1);

            Assert.IsTrue(identifier.Update(Frame(), NoSurfaces, NoObjects, 0.3f + 2.9f));
            Assert.AreEqual(1, identifier.Tracked.Count, "2.9 s unseen is kept");

            Assert.IsTrue(identifier.Update(Frame(), NoSurfaces, NoObjects, 0.3f + 3.1f));
            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        [Test]
        public void AProposal_IsKeptWhileTheUserLooksElsewhere()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), 5);

            Assert.IsTrue(identifier.Update(Frame(), NoSurfaces, NoObjects, 60f));

            Assert.AreEqual(1, identifier.Proposals.Count);
        }

        // -------------------------------------------------------------------
        // Furniture already in the room
        // -------------------------------------------------------------------

        [Test]
        public void ASightingOfFurnitureAlreadyAdded_IsNotOfferedAgain()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            SceneObjectModel bed = Existing("bed", 2f, 1.5f, 1.5f, 2f);

            Repeat(identifier, Frame(Seen("bed", 2f, 1.2f), Seen("chair", 2.2f, 1.6f)), 10, existing: new[] { bed });

            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        [Test]
        public void AProposal_DisappearsOnceItsObjectIsAdded()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), 5);
            Vector3 center = identifier.Proposals[0].CenterGhost;

            SceneObjectModel chair = Existing("chair", center.x, center.z, 0.5f, 0.5f);
            Assert.IsTrue(identifier.Update(Frame(), NoSurfaces, new[] { chair }, 0.5f));

            Assert.AreEqual(0, identifier.Tracked.Count);
        }

        [Test]
        public void IsOnExistingObject_UsesTheObjectsRotatedFootprint()
        {
            // 0.6 wide, 2 deep, turned to run along x.
            SceneObjectModel bed = Existing("bed", 2f, 1.5f, 0.6f, 2f);
            bed.yawDeg = 90f;

            Assert.IsTrue(FurnitureIdentificationController.IsOnExistingObject(new Vector3(2.9f, 0f, 1.5f), "chair", new[] { bed }));
            Assert.IsFalse(FurnitureIdentificationController.IsOnExistingObject(new Vector3(2f, 0f, 1.9f), "chair", new[] { bed }));
            Assert.IsTrue(FurnitureIdentificationController.IsOnExistingObject(new Vector3(2f, 0f, 1.9f), "bed", new[] { bed }),
                "the same type nearby is the same object");
        }

        // -------------------------------------------------------------------
        // What a proposal becomes
        // -------------------------------------------------------------------

        [Test]
        public void TryBuild_OnASurface_TakesTheMeasuredSizeAndYaw()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            FurnitureCandidate desk = DeskTop(width: 1.2f, depth: 0.6f, height: 0.72f, yaw: 30f);
            Repeat(identifier, Frame(Seen("dining table", 2f, 1.5f)), 5, new[] { desk });

            Assert.IsTrue(FurnitureIdentificationController.TryBuild(identifier.Proposals[0], out SceneObjectModel model, out string error), error);

            Assert.AreEqual("desk", model.type);
            Assert.AreEqual(1.2f, model.widthM, Tolerance);
            Assert.AreEqual(0.6f, model.depthM, Tolerance);
            Assert.AreEqual(0.72f, model.heightM, Tolerance);
            Assert.AreEqual(30f, model.yawDeg, Tolerance);
            Assert.AreEqual(0f, model.center.y, Tolerance);
            Assert.AreEqual(2f, model.center.x, Tolerance);
            Assert.AreEqual(1.5f, model.center.z, Tolerance);
        }

        [Test]
        public void TryBuild_WithoutASurface_UsesTheTypesDefaultSize()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("bed", 2f, 0.6f)), 5);

            Assert.IsTrue(FurnitureIdentificationController.TryBuild(identifier.Proposals[0], out SceneObjectModel model, out string error), error);

            Assert.IsTrue(FurnitureValidator.TryGetDefaultDimensions("bed", out float width, out float depth, out float height));
            Assert.AreEqual("bed", model.type);
            Assert.AreEqual(width, model.widthM, Tolerance);
            Assert.AreEqual(depth, model.depthM, Tolerance);
            Assert.AreEqual(height, model.heightM, Tolerance);
            Assert.AreEqual(0f, model.yawDeg, Tolerance);
            Assert.IsTrue(FurnitureValidator.Validate(model).IsValid);
        }

        [Test]
        public void SurfaceTypes_NamesOnlyProposedSurfaces()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            FurnitureCandidate desk = DeskTop();
            Repeat(identifier, Frame(Seen("dining table", 2f, 1.5f)), 4, new[] { desk });
            Assert.AreEqual(0, identifier.SurfaceTypes().Count);

            Repeat(identifier, Frame(Seen("dining table", 2f, 1.5f), Seen("chair", 1f, 0.5f)), 1, new[] { desk }, start: 1f);

            IReadOnlyDictionary<TrackableId, string> types = identifier.SurfaceTypes();
            Assert.AreEqual(1, types.Count);
            Assert.AreEqual("desk", types[desk.SurfaceId]);
        }

        [Test]
        public void RemoveAndClear_StopOffering()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("chair", 1f, 1f), Seen("bed", 3f, 1f)), 5);

            Assert.IsTrue(identifier.Remove(identifier.Proposals[0].Id));
            Assert.AreEqual(1, identifier.Proposals.Count);
            Assert.IsFalse(identifier.Remove(-1));

            identifier.Clear();
            Assert.AreEqual(0, identifier.Tracked.Count);
            Assert.AreEqual(0, identifier.FramesProcessed);
        }

        [Test]
        public void Describe_CountsInTheOrderSeen()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            FurnitureCandidate desk = DeskTop(x: 3f, z: 2f);
            Repeat(
                identifier,
                Frame(Seen("bed", 1f, 0.5f), Seen("dining table", 3f, 2f), Seen("chair", 2f, 1f), Seen("chair", 1.2f, 2.4f),
                    Seen("refrigerator", 3.5f, 0.5f)),
                5,
                new[] { desk });

            Assert.AreEqual("bed, desk, 2 chairs, refrigerator", FurnitureIdentificationController.Describe(identifier.Proposals));
        }

        [Test]
        public void Describe_Plurals()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("couch", 1f, 0.5f), Seen("couch", 3f, 0.5f), Seen("tv", 1f, 2f), Seen("tv", 3f, 2f)), 5);

            Assert.AreEqual("2 couches, 2 TVs", FurnitureIdentificationController.Describe(identifier.Proposals));
            Assert.AreEqual(string.Empty, FurnitureIdentificationController.Describe(new IdentifiedFurniture[0]));
        }

        // -------------------------------------------------------------------
        // Through the workflow: Add All
        // -------------------------------------------------------------------

        private static ScanWorkflowController AddObjectsWorkflow(FakeSpatialProvider provider)
        {
            var floorLock = new FloorLockController(provider);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var workflow = new ScanWorkflowController(
                floorLock,
                new WallSweepController(provider, floorLock),
                corners,
                height,
                new OpeningCaptureController(provider, floorLock, corners, height),
                new ObjectPlacementController(provider, floorLock),
                new FurnitureDetectionController(provider, floorLock, corners));

            workflow.Tick(true);
            Assert.IsTrue(workflow.TryLockFloor(out _));
            Assert.IsTrue(workflow.BeginCornerCapture());

            foreach (Vector2 corner in RoomCorners)
            {
                AimAtGhost(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _), workflow.Corners.LastError);
            }

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Assert.IsTrue(workflow.FinishAddingOpenings());
            Assert.AreEqual(ScanPhase.AddObjects, workflow.Phase);

            MapScreenToFloor(provider, workflow.Frame);
            return workflow;
        }

        private static void See(ScanWorkflowController workflow, List<ObjectDetection> frame, int count, float start = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(workflow.UpdateFurnitureIdentification(frame, start + 0.1f * i));
            }
        }

        private static DetectedSurface DeskPlane(GhostCoordinateFrame frame, float x, float z)
        {
            Vector3 worldAxis = frame.GhostDirectionToWorld(Vector3.forward);
            return new DetectedSurface(
                new TrackableId(nextSurfaceId++, 0),
                frame.GhostToWorld(new Vector3(x, 0.74f, z)),
                Quaternion.LookRotation(worldAxis, Vector3.up) * Quaternion.Euler(0f, -90f, 0f),
                new Vector2(1.4f, 0.7f),
                PlaneAlignment.HorizontalUp,
                PlaneClassifications.Seat);
        }

        [Test]
        public void Workflow_FurnitureSeenWhileScanningTheRoom_IsOfferedAtTheFurnitureStep()
        {
            FakeSpatialProvider provider = Provider();
            var floorLock = new FloorLockController(provider);
            var corners = new CornerCaptureController(provider, floorLock);
            var height = new HeightCaptureController(provider, floorLock, corners);
            var workflow = new ScanWorkflowController(
                floorLock,
                new WallSweepController(provider, floorLock),
                corners,
                height,
                new OpeningCaptureController(provider, floorLock, corners, height),
                new ObjectPlacementController(provider, floorLock),
                new FurnitureDetectionController(provider, floorLock, corners));
            workflow.Tick(true);
            Assert.IsFalse(workflow.UpdateFurnitureIdentification(Frame(Seen("bed", 1f, 0.4f)), 0f), "no floor yet");
            Assert.IsTrue(workflow.TryLockFloor(out _));

            // Turning to scan the room: the bed goes by once and is not seen again.
            MapScreenToFloor(provider, workflow.Frame);
            int revision = workflow.Revision;
            See(workflow, Frame(Seen("bed", 1f, 0.4f)), 5);
            Assert.AreEqual(revision, workflow.Revision, "a proposal is not furniture");

            provider.ScreenRayAt = null;
            Assert.IsTrue(workflow.BeginCornerCapture());
            foreach (Vector2 corner in RoomCorners)
            {
                AimAtGhost(provider, workflow.Frame, corner.x, corner.y);
                Assert.IsTrue(workflow.TryCaptureCorner(out _), workflow.Corners.LastError);
            }

            AimAtGhost(provider, workflow.Frame, 0f, 0f);
            Assert.IsTrue(workflow.TryVerifyClosure(out _, out _));
            Assert.IsTrue(workflow.TrySetManualHeight(2.5f, out _));
            Assert.IsTrue(workflow.UpdateFurnitureIdentification(Frame(), 10f), "still running while adding openings");
            Assert.IsTrue(workflow.FinishAddingOpenings());

            Assert.AreEqual(1, workflow.FurnitureIdentification.Proposals.Count);
            Assert.AreEqual(1, workflow.TryAcceptAllDetectedFurniture());
            Assert.AreEqual("bed", workflow.Objects.Objects[0].type);
        }

        [Test]
        public void Workflow_IdentificationStopsWhenFurnitureIsDone_AndNeverPublishes()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            int revision = workflow.Revision;

            See(workflow, Frame(Seen("chair", 1f, 1f)), 5);
            Assert.AreEqual(revision, workflow.Revision, "a proposal is not furniture");
            Assert.AreEqual(1, workflow.FurnitureIdentification.Proposals.Count);

            Assert.IsTrue(workflow.FinishAddingObjects());
            Assert.IsFalse(workflow.UpdateFurnitureIdentification(Frame(Seen("chair", 1f, 1f)), 1f));
        }

        [Test]
        public void Workflow_AddAll_AddsEachIdentifiedPiece_OnePublishEach()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            See(workflow, Frame(Seen("bed", 1f, 0.4f), Seen("chair", 3f, 1f), Seen("chair", 3f, 2.2f)), 5);
            int revision = workflow.Revision;

            Assert.AreEqual(3, workflow.TryAcceptAllDetectedFurniture());

            Assert.AreEqual(revision + 3, workflow.Revision);
            Assert.AreEqual(3, workflow.Objects.ObjectCount);
            Assert.AreEqual("bed", workflow.Objects.Objects[0].type);
            Assert.AreEqual("chair", workflow.Objects.Objects[1].type);
            Assert.AreEqual(3, workflow.Snapshot.room.objects.Length);
            Assert.AreEqual(0, workflow.FurnitureIdentification.Proposals.Count);

            // Still in view: not offered again.
            See(workflow, Frame(Seen("bed", 1f, 0.4f), Seen("chair", 3f, 1f), Seen("chair", 3f, 2.2f)), 5, 1f);
            Assert.AreEqual(0, workflow.FurnitureIdentification.Tracked.Count);
            Assert.AreEqual(0, workflow.TryAcceptAllDetectedFurniture());
        }

        [Test]
        public void Workflow_AnIdentifiedSurface_IsAddedOnceWithItsMeasuredSize()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            provider.DetectedSurfaces.Add(DeskPlane(workflow.Frame, 2f, 1.5f));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));

            See(workflow, Frame(Seen("dining table", 2f, 1.1f)), 5);
            Assert.AreEqual("desk", workflow.FurnitureDetection.SelectedType, "the surface takes the identified type");

            Assert.AreEqual(1, workflow.TryAcceptAllDetectedFurniture());

            SceneObjectModel desk = workflow.Objects.Objects[0];
            Assert.AreEqual("desk", desk.type);
            Assert.AreEqual(1.4f, Mathf.Max(desk.widthM, desk.depthM), 0.01f);
            Assert.AreEqual(0.74f, desk.heightM, 0.01f);
        }

        [Test]
        public void Workflow_AnUnidentifiedSurface_IsAddedAsGeneric_WhateverARKitCallsIt()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            provider.DetectedSurfaces.Add(DeskPlane(workflow.Frame, 2f, 1.5f));

            Assert.AreEqual(1, workflow.TryAcceptAllDetectedFurniture());

            Assert.AreEqual("generic", workflow.Objects.Objects[0].type);
        }

        [Test]
        public void Workflow_ManualPlacement_IsUnaffectedByProposals()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            See(workflow, Frame(Seen("chair", 1f, 1f)), 5);

            provider.ScreenRayAt = null;
            AimAtGhost(provider, workflow.Frame, 3f, 2f);
            Assert.IsTrue(workflow.SetObjectType("dresser"));
            Assert.IsTrue(workflow.TryPlaceObject(out _));

            Assert.AreEqual(1, workflow.Objects.ObjectCount);
            Assert.AreEqual("dresser", workflow.Objects.Objects[0].type);
            Assert.AreEqual(1, workflow.FurnitureIdentification.Proposals.Count);
        }

        // -------------------------------------------------------------------
        // Task R11: photos
        // -------------------------------------------------------------------

        private static byte[] Jpeg(byte marker) => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, marker };

        [Test]
        public void AFirstSighting_AsksForAPhotoOfItsBox()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());

            Repeat(identifier, Frame(Seen("chair", 1f, 1f, 0.6f)), 1);

            IReadOnlyList<IdentifiedFurniture> wanting = identifier.WantingPhotos();
            Assert.AreEqual(1, wanting.Count);
            Assert.AreEqual(Seen("chair", 1f, 1f).ScreenBox, wanting[0].PhotoBox);
            Assert.IsTrue(identifier.SetPhoto(wanting[0].Id, Jpeg(1)));
            Assert.AreEqual(0.6f, identifier.Tracked[0].PhotoConfidence, Tolerance);
            Assert.AreEqual(0, identifier.WantingPhotos().Count);
        }

        [Test]
        public void ANewPhoto_IsWantedOnlyForAClearlyBetterSighting()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("chair", 1f, 1f, 0.6f)), 1);
            Assert.IsTrue(identifier.SetPhoto(identifier.Tracked[0].Id, Jpeg(1)));

            Repeat(identifier, Frame(Seen("chair", 1f, 1f, 0.64f)), 1, start: 0.1f);
            Assert.AreEqual(0, identifier.WantingPhotos().Count, "0.04 better is not worth a photo");

            Repeat(identifier, Frame(Seen("chair", 1f, 1f, 0.7f)), 1, start: 0.2f);
            Assert.AreEqual(1, identifier.WantingPhotos().Count);
            Assert.IsTrue(identifier.SetPhoto(identifier.Tracked[0].Id, Jpeg(2)));
            Assert.AreEqual(Jpeg(2), identifier.Tracked[0].Photo);
        }

        [Test]
        public void AFailedCapture_IsAskedForAgainNextFrame()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), 1);

            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), 1, start: 0.1f);

            Assert.AreEqual(1, identifier.WantingPhotos().Count);
        }

        [Test]
        public void SetPhoto_RefusesNonJpegs_AndTracksThatDidNotAsk()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("chair", 1f, 1f)), 1);
            int id = identifier.Tracked[0].Id;

            Assert.IsFalse(identifier.SetPhoto(id, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0 }));
            Assert.IsFalse(identifier.SetPhoto(id + 100, Jpeg(1)));

            Repeat(identifier, Frame(), 1, start: 0.1f);
            Assert.IsFalse(identifier.SetPhoto(id, Jpeg(1)), "not seen this frame");
        }

        [Test]
        public void Workflow_AddAll_KeepsEachIdentifiedPhoto_WithItsObject()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            See(workflow, Frame(Seen("bed", 1f, 0.4f), Seen("chair", 3f, 1f)), 1);
            foreach (IdentifiedFurniture item in workflow.FurnitureIdentification.WantingPhotos())
            {
                Assert.IsTrue(workflow.FurnitureIdentification.SetPhoto(item.Id, Jpeg((byte)item.Id)));
            }

            See(workflow, Frame(Seen("bed", 1f, 0.4f), Seen("chair", 3f, 1f)), 4, 0.1f);
            Assert.AreEqual(2, workflow.TryAcceptAllDetectedFurniture());

            Assert.AreEqual(2, workflow.ObjectPhotos.Count);
            foreach (SceneObjectModel model in workflow.Objects.Objects)
            {
                Assert.IsTrue(workflow.ObjectPhotos.ContainsKey(model.id), model.type);
            }
        }

        [Test]
        public void Workflow_AnIdentifiedSurface_KeepsItsPhoto()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            provider.DetectedSurfaces.Add(DeskPlane(workflow.Frame, 2f, 1.5f));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            See(workflow, Frame(Seen("dining table", 2f, 1.1f)), 1);
            Assert.IsTrue(workflow.FurnitureIdentification.SetPhoto(workflow.FurnitureIdentification.Tracked[0].Id, Jpeg(7)));
            See(workflow, Frame(Seen("dining table", 2f, 1.1f)), 4, 0.1f);

            Assert.AreEqual(1, workflow.TryAcceptAllDetectedFurniture());

            CollectionAssert.AreEqual(Jpeg(7), workflow.ObjectPhotos[workflow.Objects.Objects[0].id]);
        }

        [Test]
        public void Workflow_SetObjectPhoto_OnlyForAPlacedObject_AndUndoDropsIt()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            provider.ScreenRayAt = null;
            AimAtGhost(provider, workflow.Frame, 3f, 2f);
            Assert.IsTrue(workflow.SetObjectType("dresser"));
            Assert.IsTrue(workflow.TryPlaceObject(out _));
            string id = workflow.Objects.Objects[0].id;
            int revision = workflow.Revision;

            Assert.IsFalse(workflow.SetObjectPhoto("no-such-object", Jpeg(1)));
            Assert.IsFalse(workflow.SetObjectPhoto(id, new byte[] { 1, 2, 3, 4 }));
            Assert.IsTrue(workflow.SetObjectPhoto(id, Jpeg(1)));
            Assert.AreEqual(revision, workflow.Revision, "a photo is not scene state");

            Assert.IsTrue(workflow.TryUndoLastObject(out _));
            Assert.AreEqual(0, workflow.ObjectPhotos.Count);
        }

        [Test]
        public void Workflow_SetObjectPhoto_IsRefusedOnceFurnitureIsDone()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            provider.ScreenRayAt = null;
            AimAtGhost(provider, workflow.Frame, 3f, 2f);
            Assert.IsTrue(workflow.TryPlaceObject(out _));
            Assert.IsTrue(workflow.FinishAddingObjects());

            Assert.IsFalse(workflow.SetObjectPhoto(workflow.Objects.Objects[0].id, Jpeg(1)));
        }

        // -------------------------------------------------------------------
        // Task R5: the detector's label
        // -------------------------------------------------------------------

        [Test]
        public void TryBuild_LabelsTheObjectWithTheDetectorsClassName()
        {
            FurnitureIdentificationController identifier = Identifier(Provider());
            Repeat(identifier, Frame(Seen("potted plant", 1f, 1f), Seen("dining table", 3f, 1f)), 5);

            Assert.IsTrue(FurnitureIdentificationController.TryBuild(identifier.Proposals[0], out SceneObjectModel plant, out _));
            Assert.IsTrue(FurnitureIdentificationController.TryBuild(identifier.Proposals[1], out SceneObjectModel table, out _));

            Assert.AreEqual("generic", plant.type);
            Assert.AreEqual("potted plant", plant.label);
            Assert.AreEqual("table", table.type);
            Assert.AreEqual("dining table", table.label);
        }

        [Test]
        public void LabelFor_CutsToTheSchemaLimit()
        {
            Assert.IsNull(FurnitureIdentificationController.LabelFor(null));
            Assert.IsNull(FurnitureIdentificationController.LabelFor(""));
            Assert.AreEqual(FurnitureValidator.MaxLabelLength,
                FurnitureIdentificationController.LabelFor(new string('a', 100)).Length);
        }

        [Test]
        public void Workflow_AddAll_KeepsLabelsInTheSnapshot()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            See(workflow, Frame(Seen("potted plant", 1f, 1f), Seen("chair", 3f, 1f)), 5);

            Assert.AreEqual(2, workflow.TryAcceptAllDetectedFurniture());

            SceneObjectModel[] published = workflow.Snapshot.room.objects;
            Assert.AreEqual("potted plant", published[0].label);
            Assert.AreEqual("chair", published[1].label);
            Assert.IsTrue(FurnitureValidator.Validate(published[0]).IsValid);
        }

        [Test]
        public void Workflow_AnIdentifiedSurface_KeepsTheLabel_UnlessTheUserPickedAnotherType()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            provider.DetectedSurfaces.Add(DeskPlane(workflow.Frame, 2f, 1.5f));
            provider.DetectedSurfaces.Add(DeskPlane(workflow.Frame, 2f, 2.6f));
            Assert.IsTrue(workflow.TryRefreshFurnitureDetection(out _));
            See(workflow, Frame(Seen("dining table", 2f, 1.1f), Seen("dining table", 2f, 2.2f)), 5);

            // The first surface as identified; the second retyped by the user.
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _));
            Assert.IsTrue(workflow.SetDetectedFurnitureType("dresser"));
            Assert.IsTrue(workflow.TryAcceptDetectedFurniture(out _, out _), workflow.FurnitureDetection.LastError);

            SceneObjectModel[] objects = workflow.Snapshot.room.objects;
            Assert.AreEqual("desk", objects[0].type);
            Assert.AreEqual("dining table", objects[0].label);
            Assert.AreEqual("dresser", objects[1].type);
            Assert.IsTrue(string.IsNullOrEmpty(objects[1].label));
        }

        [Test]
        public void Workflow_AnObjectPlacedByHand_HasNoLabel_AndResizingKeepsALabel()
        {
            FakeSpatialProvider provider = Provider();
            ScanWorkflowController workflow = AddObjectsWorkflow(provider);
            See(workflow, Frame(Seen("potted plant", 1f, 1f)), 5);
            Assert.AreEqual(1, workflow.TryAcceptAllDetectedFurniture());

            provider.ScreenRayAt = null;
            AimAtGhost(provider, workflow.Frame, 3f, 2f);
            Assert.IsTrue(workflow.TryPlaceObject(out _));
            Assert.IsTrue(workflow.TrySetObjectWidth(0, 0.6f, out _));

            SceneObjectModel[] objects = workflow.Snapshot.room.objects;
            Assert.AreEqual("potted plant", objects[0].label);
            Assert.AreEqual(0.6f, objects[0].widthM, Tolerance);
            Assert.IsTrue(string.IsNullOrEmpty(objects[1].label));
        }

        // -------------------------------------------------------------------

        private static void AssertNear(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, $"x of {actual}");
            Assert.AreEqual(expected.y, actual.y, Tolerance, $"y of {actual}");
        }
    }
}
