using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task F2 tests for <see cref="GhostCoordinateFrame"/>.
    /// Required accuracy: round-trip error below 1e-4.
    /// </summary>
    public sealed class CoordinateFrameTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>
        /// Builds a frame the way floor-lock does: origin at the floor hit,
        /// forward = camera forward projected onto the floor plane,
        /// right = cross(up, forward).
        /// </summary>
        private static GhostCoordinateFrame BuildFrame(Vector3 origin, float yawDeg)
        {
            Vector3 up = Vector3.up;
            Vector3 cameraForward = Quaternion.Euler(25f, yawDeg, 0f) * Vector3.forward;
            Vector3 forward = Vector3.ProjectOnPlane(cameraForward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            return new GhostCoordinateFrame(origin, right, up, forward);
        }

        [Test]
        public void Origin_MapsToZero()
        {
            var origin = new Vector3(3.1f, 1.4f, -2.7f);
            GhostCoordinateFrame frame = BuildFrame(origin, 37f);

            Vector3 ghost = frame.WorldToGhost(origin);

            Assert.AreEqual(0f, ghost.x, Tolerance);
            Assert.AreEqual(0f, ghost.y, Tolerance);
            Assert.AreEqual(0f, ghost.z, Tolerance);
        }

        [Test]
        public void WorldToGhost_ThenGhostToWorld_RoundTripsWithinTolerance()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(1f, 2f, 3f), 63f);

            var samples = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(5.5f, 1.2f, -3.3f),
                new Vector3(-12.25f, 8.75f, 4.5f),
                new Vector3(100f, -50f, 25f)
            };

            foreach (Vector3 world in samples)
            {
                Vector3 restored = frame.GhostToWorld(frame.WorldToGhost(world));
                Assert.Less((restored - world).magnitude, Tolerance,
                    $"Round trip failed for {world}.");
            }
        }

        [Test]
        public void GhostToWorld_ThenWorldToGhost_RoundTripsWithinTolerance()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(-4f, 0.5f, 6f), 145f);

            var ghost = new Vector3(2.5f, 1.75f, -0.25f);

            Vector3 restored = frame.WorldToGhost(frame.GhostToWorld(ghost));

            Assert.Less((restored - ghost).magnitude, Tolerance);
        }

        [Test]
        public void UpAxis_IsPreserved()
        {
            var origin = new Vector3(2f, 5f, 1f);
            GhostCoordinateFrame frame = BuildFrame(origin, 88f);

            // A point one meter above the origin must be at ghost Y = 1.
            Vector3 ghost = frame.WorldToGhost(origin + Vector3.up);

            Assert.AreEqual(0f, ghost.x, Tolerance);
            Assert.AreEqual(1f, ghost.y, Tolerance);
            Assert.AreEqual(0f, ghost.z, Tolerance);
        }

        [Test]
        public void ForwardAxis_MapsToPositiveZ()
        {
            var origin = new Vector3(0f, 0f, 0f);
            Vector3 up = Vector3.up;
            Vector3 forward = new Vector3(1f, 0f, 0f);
            Vector3 right = Vector3.Cross(up, forward).normalized;
            var frame = new GhostCoordinateFrame(origin, right, up, forward);

            Vector3 ghost = frame.WorldToGhost(forward);

            Assert.AreEqual(0f, ghost.x, Tolerance);
            Assert.AreEqual(0f, ghost.y, Tolerance);
            Assert.AreEqual(1f, ghost.z, Tolerance, "World forward must map to ghost +Z.");
        }

        [Test]
        public void Constructor_NormalizesAxes()
        {
            // Deliberately non-unit axes.
            var frame = new GhostCoordinateFrame(
                Vector3.zero,
                new Vector3(5f, 0f, 0f),
                new Vector3(0f, 3f, 0f),
                new Vector3(0f, 0f, 7f));

            Vector3 ghost = frame.WorldToGhost(new Vector3(0f, 0f, 2f));

            Assert.AreEqual(2f, ghost.z, Tolerance,
                "Axes must be normalized, otherwise distances are scaled.");
        }

        // -------------------------------------------------------------------
        // Directions ignore the origin translation.
        // -------------------------------------------------------------------

        [Test]
        public void DirectionConversion_IgnoresOrigin()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(10f, 20f, 30f), 42f);

            var worldDir = new Vector3(0f, 1f, 0f);
            Vector3 ghostDir = frame.WorldDirectionToGhost(worldDir);

            Assert.AreEqual(0f, ghostDir.x, Tolerance);
            Assert.AreEqual(1f, ghostDir.y, Tolerance);
            Assert.AreEqual(0f, ghostDir.z, Tolerance);
        }

        [Test]
        public void DirectionConversion_RoundTrips()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(1f, 1f, 1f), 17f);

            var dir = new Vector3(0.3f, -0.8f, 0.5f);

            Vector3 restored = frame.GhostDirectionToWorld(frame.WorldDirectionToGhost(dir));

            Assert.Less((restored - dir).magnitude, Tolerance);
        }

        // -------------------------------------------------------------------
        // Rays. This is what height/opening capture actually uses.
        // -------------------------------------------------------------------

        [Test]
        public void WorldRayToGhost_ConvertsOriginAndDirection()
        {
            var frameOrigin = new Vector3(2f, 1f, -1f);
            GhostCoordinateFrame frame = BuildFrame(frameOrigin, 55f);

            var worldRay = new Ray(frameOrigin + Vector3.up * 1.5f, Vector3.up);
            Ray ghostRay = frame.WorldRayToGhost(worldRay);

            Assert.AreEqual(0f, ghostRay.origin.x, Tolerance);
            Assert.AreEqual(1.5f, ghostRay.origin.y, Tolerance);
            Assert.AreEqual(0f, ghostRay.origin.z, Tolerance);

            Assert.AreEqual(1f, ghostRay.direction.y, Tolerance);
        }

        [Test]
        public void RayConversion_RoundTrips()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(-3f, 2f, 4f), 99f);

            var worldRay = new Ray(new Vector3(1f, 2f, 3f), new Vector3(0.5f, -0.5f, 0.7f).normalized);

            Ray restored = frame.GhostRayToWorld(frame.WorldRayToGhost(worldRay));

            Assert.Less((restored.origin - worldRay.origin).magnitude, Tolerance);
            Assert.Less((restored.direction - worldRay.direction).magnitude, Tolerance);
        }
    }
}
