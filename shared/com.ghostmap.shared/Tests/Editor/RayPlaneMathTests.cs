using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task F2 tests for <see cref="RayPlaneMath"/>.
    ///
    /// This is the arithmetic that replaces depth sensing: a camera ray is
    /// intersected with a plane GhostMap already knows.
    /// </summary>
    public sealed class RayPlaneMathTests
    {
        private const float Tolerance = 1e-4f;

        // -------------------------------------------------------------------
        // Horizontal plane - used for corner and furniture capture.
        // -------------------------------------------------------------------

        [Test]
        public void HorizontalPlane_IntersectsWhenAimingDown()
        {
            // Eye at 1.5 m aiming down at 45 degrees toward +Z.
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), new Vector3(0f, -1f, 1f).normalized);

            bool hit = RayPlaneMath.TryIntersectHorizontalPlane(ray, 0f, out Vector3 point);

            Assert.IsTrue(hit, "A ray aimed down at the floor must intersect it.");
            Assert.AreEqual(0f, point.y, Tolerance, "Intersection must lie on the plane.");
            Assert.AreEqual(1.5f, point.z, Tolerance, "45 degrees from 1.5 m gives 1.5 m forward.");
            Assert.AreEqual(0f, point.x, Tolerance);
        }

        [Test]
        public void HorizontalPlane_IntersectsNonZeroPlaneHeight()
        {
            var ray = new Ray(new Vector3(0f, 0f, 0f), Vector3.up);

            bool hit = RayPlaneMath.TryIntersectHorizontalPlane(ray, 2.5f, out Vector3 point);

            Assert.IsTrue(hit);
            Assert.AreEqual(2.5f, point.y, Tolerance);
        }

        [Test]
        public void HorizontalPlane_RejectsParallelRay()
        {
            // Perfectly horizontal ray never meets a horizontal plane.
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.forward);

            bool hit = RayPlaneMath.TryIntersectHorizontalPlane(ray, 0f, out Vector3 point);

            Assert.IsFalse(hit, "A parallel ray must be rejected, not extrapolated.");
            Assert.AreEqual(Vector3.zero, point);
        }

        [Test]
        public void HorizontalPlane_RejectsNearlyParallelRay()
        {
            // Grazing ray: denominator below the epsilon guard.
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), new Vector3(0f, -0.00001f, 1f).normalized);

            bool hit = RayPlaneMath.TryIntersectHorizontalPlane(ray, 0f, out _);

            Assert.IsFalse(hit, "A nearly parallel ray must be rejected to avoid a wild intersection.");
        }

        [Test]
        public void HorizontalPlane_RejectsIntersectionBehindRay()
        {
            // Aiming up, so the floor is behind the camera.
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), new Vector3(0f, 1f, 1f).normalized);

            bool hit = RayPlaneMath.TryIntersectHorizontalPlane(ray, 0f, out _);

            Assert.IsFalse(hit, "An intersection behind the ray origin must be rejected.");
        }

        // -------------------------------------------------------------------
        // Arbitrary plane - used for height, door and window capture.
        // -------------------------------------------------------------------

        [Test]
        public void ArbitraryPlane_IntersectsVerticalWall()
        {
            // Wall plane at x = 4, normal pointing back toward origin.
            var plane = new Plane(new Vector3(-1f, 0f, 0f), new Vector3(4f, 0f, 0f));
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.right);

            bool hit = RayPlaneMath.TryIntersectPlane(ray, plane, out Vector3 point);

            Assert.IsTrue(hit);
            Assert.AreEqual(4f, point.x, Tolerance);
            Assert.AreEqual(1.5f, point.y, Tolerance);
        }

        [Test]
        public void ArbitraryPlane_NormalSignDoesNotMatter()
        {
            var planeA = new Plane(new Vector3(-1f, 0f, 0f), new Vector3(4f, 0f, 0f));
            var planeB = new Plane(new Vector3(1f, 0f, 0f), new Vector3(4f, 0f, 0f));
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.right);

            bool hitA = RayPlaneMath.TryIntersectPlane(ray, planeA, out Vector3 pointA);
            bool hitB = RayPlaneMath.TryIntersectPlane(ray, planeB, out Vector3 pointB);

            Assert.IsTrue(hitA);
            Assert.IsTrue(hitB, "Wall plane normal sign is irrelevant for ray intersection.");
            Assert.Less((pointA - pointB).magnitude, Tolerance);
        }

        [Test]
        public void ArbitraryPlane_RejectsParallelRay()
        {
            var plane = new Plane(new Vector3(-1f, 0f, 0f), new Vector3(4f, 0f, 0f));
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.forward);

            bool hit = RayPlaneMath.TryIntersectPlane(ray, plane, out _);

            Assert.IsFalse(hit);
        }

        [Test]
        public void ArbitraryPlane_RejectsIntersectionBehindRay()
        {
            var plane = new Plane(new Vector3(-1f, 0f, 0f), new Vector3(4f, 0f, 0f));
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.left);

            bool hit = RayPlaneMath.TryIntersectPlane(ray, plane, out _);

            Assert.IsFalse(hit);
        }

        /// <summary>
        /// Height capture: aim at the wall/ceiling line and read the Y of the
        /// intersection. This is the exact computation from plan section 8.7.
        /// </summary>
        [Test]
        public void ArbitraryPlane_ProducesRoomHeightFromWallIntersection()
        {
            var wallPlane = new Plane(new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 3f));
            // Eye at 1.5 m, aiming up at 45 degrees toward the wall 3 m away.
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), new Vector3(0f, 1f, 1f).normalized);

            bool hit = RayPlaneMath.TryIntersectPlane(ray, wallPlane, out Vector3 point);

            Assert.IsTrue(hit);
            Assert.AreEqual(3f, point.z, Tolerance);
            Assert.AreEqual(4.5f, point.y, Tolerance, "1.5 m eye + 3 m rise at 45 degrees.");
        }
    }
}
