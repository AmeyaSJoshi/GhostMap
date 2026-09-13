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
    /// Task S5 Part 2: placing parametric furniture on the locked floor plane
    /// by intersecting the center-screen ray with it, applying the type's
    /// default dimensions, and letting the user adjust width, depth, height
    /// and yaw.
    ///
    /// <para>The fixture frame is the same deliberately hostile one every
    /// Task S2-S5 controller test uses: floor 1.4 m below Unity's origin, 40
    /// degree yaw.</para>
    /// </summary>
    public sealed class ObjectPlacementControllerTests
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3 FloorOrigin = new Vector3(2.3f, -1.4f, -0.8f);
        private const float YawDeg = 40f;

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

        private static void AimAtFloor(FakeSpatialProvider provider, GhostCoordinateFrame frame, float ghostX, float ghostZ)
        {
            Vector3 target = frame.GhostToWorld(new Vector3(ghostX, 0f, ghostZ));
            Vector3 eye = frame.GhostToWorld(new Vector3(ghostX, 1.5f, ghostZ - 1f));

            provider.ScreenRay = new Ray(eye, (target - eye).normalized);
        }

        private static ObjectPlacementController LockedController(FakeSpatialProvider provider, out FloorLockController floorLock)
        {
            floorLock = new FloorLockController(provider);
            Assert.IsTrue(
                floorLock.TryLockFloor(out FloorLockRejection rejection),
                $"The fixture's own floor lock should succeed, got {rejection}.");

            return new ObjectPlacementController(provider, floorLock);
        }

        private static ObjectPlacementController LockedController(FakeSpatialProvider provider)
            => LockedController(provider, out _);

        // -------------------------------------------------------------------
        // Floor-ray placement
        // -------------------------------------------------------------------

        [Test]
        public void PlacementIntersectsTheCenterScreenRayWithTheFloorPlane()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = LockedController(provider, out FloorLockController floorLock);

            AimAtFloor(provider, floorLock.Frame, 1.2f, 0.8f);

            Assert.IsTrue(placement.TryProjectCrosshairToFloor(out Vector3 ghost));
            Assert.That(ghost.x, Is.EqualTo(1.2f).Within(Tolerance));
            Assert.That(ghost.z, Is.EqualTo(0.8f).Within(Tolerance));
        }

        [Test]
        public void APlacedObjectsCenterLiesExactlyOnGhostFloorY()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = LockedController(provider, out FloorLockController floorLock);
            Assert.IsTrue(placement.SetType("bed"));

            AimAtFloor(provider, floorLock.Frame, 1.2f, 0.8f);
            Assert.IsTrue(placement.TryPlaceObject(out ObjectPlacementRejection rejection));

            Assert.AreEqual(ObjectPlacementRejection.None, rejection);
            Assert.AreEqual(0f, placement.Objects[0].center.y, "An object's floor reference must be exactly Ghost y = 0, not merely close to it.");
        }

        [Test]
        public void RayMissingTheFloorIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = LockedController(provider, out FloorLockController floorLock);

            // Aimed level with the horizon: never reaches the floor plane.
            Vector3 eye = floorLock.Frame.GhostToWorld(new Vector3(1.5f, 1.5f, 1.25f));
            Vector3 direction = floorLock.Frame.GhostDirectionToWorld(new Vector3(0f, 0f, 1f));
            provider.ScreenRay = new Ray(eye, direction);

            Assert.IsFalse(placement.TryPlaceObject(out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.RayMissedFloor, rejection);
        }

        // -------------------------------------------------------------------
        // Types and defaults
        // -------------------------------------------------------------------

        [Test]
        public void EveryApprovedFurnitureTypeCanBePlaced()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = LockedController(provider, out FloorLockController floorLock);

            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                AimAtFloor(provider, floorLock.Frame, 1.0f, 1.0f);
                Assert.IsTrue(placement.SetType(type), $"{type} should be a supported type.");

                Assert.IsTrue(
                    placement.TryPlaceObject(out ObjectPlacementRejection rejection),
                    $"{type} should place successfully, got {rejection}: {placement.LastError}");
            }

            Assert.AreEqual(FurnitureValidator.SupportedTypes.Count, placement.ObjectCount);
        }

        [Test]
        public void DefaultDimensionsAreAppliedAndArePositive()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = LockedController(provider, out FloorLockController floorLock);
            Assert.IsTrue(placement.SetType("desk"));

            AimAtFloor(provider, floorLock.Frame, 1.0f, 1.0f);
            Assert.IsTrue(placement.TryPlaceObject(out _));

            FurnitureValidator.TryGetDefaultDimensions("desk", out float w, out float d, out float h);
            SceneObjectModel placed = placement.Objects[0];

            Assert.Greater(placed.widthM, 0f);
            Assert.Greater(placed.depthM, 0f);
            Assert.Greater(placed.heightM, 0f);
            Assert.AreEqual(w, placed.widthM);
            Assert.AreEqual(d, placed.depthM);
            Assert.AreEqual(h, placed.heightM);
            Assert.AreEqual(0f, placed.yawDeg);
        }

        [Test]
        public void UnsupportedTypeCannotBeSelected()
        {
            ObjectPlacementController placement = LockedController(Provider());

            Assert.IsFalse(placement.SetType("sofa-bed-2000"));
            Assert.AreEqual("bed", placement.SelectedType, "An unsupported type must not overwrite the current selection.");
        }

        // -------------------------------------------------------------------
        // Adjustment
        // -------------------------------------------------------------------

        private static ObjectPlacementController PlacedBed(FakeSpatialProvider provider, out FloorLockController floorLock)
        {
            ObjectPlacementController placement = LockedController(provider, out floorLock);
            Assert.IsTrue(placement.SetType("bed"));
            AimAtFloor(provider, floorLock.Frame, 1.0f, 1.0f);
            Assert.IsTrue(placement.TryPlaceObject(out _));
            return placement;
        }

        [Test]
        public void WidthCanBeAdjusted()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);

            Assert.IsTrue(placement.TrySetWidth(0, 2.0f, out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.None, rejection);
            Assert.That(placement.Objects[0].widthM, Is.EqualTo(2.0f).Within(Tolerance));
        }

        [Test]
        public void DepthCanBeAdjusted()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);

            Assert.IsTrue(placement.TrySetDepth(0, 1.8f, out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.None, rejection);
            Assert.That(placement.Objects[0].depthM, Is.EqualTo(1.8f).Within(Tolerance));
        }

        [Test]
        public void HeightCanBeAdjusted()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);

            Assert.IsTrue(placement.TrySetHeight(0, 0.75f, out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.None, rejection);
            Assert.That(placement.Objects[0].heightM, Is.EqualTo(0.75f).Within(Tolerance));
        }

        [Test]
        public void YawCanBeAdjusted()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);

            Assert.IsTrue(placement.TrySetYaw(0, 90f, out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.None, rejection);
            Assert.That(placement.Objects[0].yawDeg, Is.EqualTo(90f).Within(Tolerance));
        }

        [Test]
        public void AnInvalidAdjustmentIsRejectedByTheSharedValidatorAndLeavesTheObjectUnchanged()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);
            float widthBefore = placement.Objects[0].widthM;

            Assert.IsFalse(placement.TrySetWidth(0, -1f, out ObjectPlacementRejection rejection));

            Assert.AreEqual(ObjectPlacementRejection.ValidationFailed, rejection);
            Assert.IsNotEmpty(placement.LastError);
            Assert.That(placement.Objects[0].widthM, Is.EqualTo(widthBefore).Within(Tolerance));
        }

        [Test]
        public void AdjustingAnOutOfRangeIndexIsRejected()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);

            Assert.IsFalse(placement.TrySetWidth(5, 1.0f, out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.IndexOutOfRange, rejection);
        }

        // -------------------------------------------------------------------
        // Undo
        // -------------------------------------------------------------------

        [Test]
        public void UndoRemovesTheMostRecentlyPlacedObject()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);

            Assert.IsTrue(placement.TryUndoLastObject(out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.None, rejection);
            Assert.AreEqual(0, placement.ObjectCount);
        }

        [Test]
        public void UndoWithNoObjectsIsRejected()
        {
            ObjectPlacementController placement = LockedController(Provider());

            Assert.IsFalse(placement.TryUndoLastObject(out ObjectPlacementRejection rejection));
            Assert.AreEqual(ObjectPlacementRejection.NoObjectsToUndo, rejection);
        }

        /// <summary>
        /// Deleting an object is purely a furniture-list mutation. It must
        /// not touch the frame, corners or captured height — the room's
        /// structural geometry established by Task S2-S4.
        /// </summary>
        [Test]
        public void DeletingAnObjectDoesNotAlterTheRoomsGeometry()
        {
            FakeSpatialProvider provider = Provider();
            var floorLock = new FloorLockController(provider);
            Assert.IsTrue(floorLock.TryLockFloor(out _));

            var corners = new CornerCaptureController(provider, floorLock);
            foreach (Vector2 corner in new[] { new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 2.5f), new Vector2(0f, 2.5f) })
            {
                AimAtFloor(provider, floorLock.Frame, corner.x, corner.y);
                Assert.IsTrue(corners.TryCaptureCorner(out _));
            }

            var height = new HeightCaptureController(provider, floorLock, corners);
            Assert.IsTrue(height.SelectWall(0));

            var placement = new ObjectPlacementController(provider, floorLock);
            AimAtFloor(provider, floorLock.Frame, 1.0f, 1.0f);
            Assert.IsTrue(placement.SetType("chair"));
            Assert.IsTrue(placement.TryPlaceObject(out _));

            GhostCoordinateFrame frameBefore = floorLock.Frame;
            int cornerCountBefore = corners.CornerCount;
            var cornerPositionsBefore = new Vector3[corners.CornerCount];
            for (int i = 0; i < corners.CornerCount; i++)
            {
                cornerPositionsBefore[i] = corners.Corners[i].position.ToVector3();
            }

            Assert.IsTrue(placement.TryUndoLastObject(out _));

            Assert.AreSame(frameBefore, floorLock.Frame);
            Assert.AreEqual(cornerCountBefore, corners.CornerCount);
            for (int i = 0; i < corners.CornerCount; i++)
            {
                Assert.AreEqual(cornerPositionsBefore[i], corners.Corners[i].position.ToVector3());
            }
        }

        // -------------------------------------------------------------------
        // Frame immutability
        // -------------------------------------------------------------------

        [Test]
        public void TheFrameIsUnchangedByObjectPlacement()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = LockedController(provider, out FloorLockController floorLock);

            GhostCoordinateFrame frame = floorLock.Frame;
            AimAtFloor(provider, floorLock.Frame, 1.0f, 1.0f);
            Assert.IsTrue(placement.SetType("table"));
            Assert.IsTrue(placement.TryPlaceObject(out _));

            Assert.AreSame(frame, floorLock.Frame);
        }

        // -------------------------------------------------------------------
        // Snapshot support
        // -------------------------------------------------------------------

        [Test]
        public void CopyObjectsProducesIndependentValuesNotLiveReferences()
        {
            FakeSpatialProvider provider = Provider();
            ObjectPlacementController placement = PlacedBed(provider, out _);

            SceneObjectModel[] copy = placement.CopyObjects();

            Assert.AreEqual(1, copy.Length);
            Assert.AreNotSame(placement.Objects[0], copy[0]);
            Assert.AreEqual(placement.Objects[0].id, copy[0].id);

            Assert.IsTrue(placement.TryUndoLastObject(out _));
            Assert.AreEqual(1, copy.Length, "A previously copied array must survive an undo of the live list.");
        }
    }
}
