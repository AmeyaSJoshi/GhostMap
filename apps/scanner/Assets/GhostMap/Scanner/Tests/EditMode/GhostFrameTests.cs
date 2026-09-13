using GhostMap.Scanner.Capture;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// The GhostMap coordinate frame as Task S2 builds it: orientation,
    /// handedness, round tripping, and the ray/floor arithmetic every later
    /// capture task depends on.
    ///
    /// These assert the composition the scanner performs. The shared package
    /// owns <see cref="GhostCoordinateFrame"/> and <see cref="RayPlaneMath"/>
    /// and tests them in isolation; what is verified here is that the scanner
    /// feeds them axes that are orthonormal, right-handed in Unity's sense, and
    /// anchored on the floor.
    /// </summary>
    public sealed class GhostFrameTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>
        /// A camera 1.5 m up, tilted 30 degrees down and yawed 40 degrees, which
        /// is roughly how a phone is held when framing a floor a metre ahead.
        /// Non-axis-aligned on purpose: a yaw of zero would let a frame that
        /// silently ignored the camera still pass.
        /// </summary>
        private static GhostCoordinateFrame BuildFrame(
            Vector3 floorOrigin = default,
            float yawDeg = 40f,
            float pitchDeg = 30f)
        {
            Vector3 forward = Quaternion.Euler(pitchDeg, yawDeg, 0f) * Vector3.forward;

            Assert.IsTrue(
                FloorLockController.TryBuildFrame(floorOrigin, forward, out GhostCoordinateFrame frame),
                "The test fixture's own camera aim should always produce a frame.");

            return frame;
        }

        [Test]
        public void OriginMapsToGhostZero()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(3.1f, -1.4f, -0.7f));

            Vector3 ghost = frame.WorldToGhost(frame.Origin);

            Assert.That(ghost.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ghost.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ghost.z, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void UpMapsToPlusY()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(3.1f, -1.4f, -0.7f));

            Vector3 ghost = frame.WorldToGhost(frame.Origin + Vector3.up * 2.5f);

            Assert.That(ghost.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ghost.y, Is.EqualTo(2.5f).Within(Tolerance));
            Assert.That(ghost.z, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(frame.Up, Is.EqualTo(Vector3.up).Using(Vector3EqualityComparer()));
        }

        /// <summary>
        /// The world direction the camera was facing, flattened onto the floor,
        /// worked out independently of the frame. Asserting against
        /// <c>frame.Forward</c> instead would let a frame that picked its axes
        /// wrongly agree with itself and pass.
        /// </summary>
        private static Vector3 ExpectedWorldForward(float yawDeg)
            => Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;

        private static Vector3 ExpectedWorldRight(float yawDeg)
            => Quaternion.Euler(0f, yawDeg, 0f) * Vector3.right;

        [Test]
        public void CameraForwardAtLockMapsToPlusZ()
        {
            const float yaw = 40f;
            var origin = new Vector3(-2f, 0.3f, 5f);
            GhostCoordinateFrame frame = BuildFrame(origin, yaw);

            // Four metres along the direction the camera was facing must read
            // as four metres of pure +Z.
            Vector3 ghost = frame.WorldToGhost(origin + ExpectedWorldForward(yaw) * 4f);

            Assert.That(ghost.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ghost.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ghost.z, Is.EqualTo(4f).Within(Tolerance));
        }

        /// <summary>
        /// Ninety degrees clockwise of the camera's aim, seen from above, is +X.
        /// Getting the cross product backwards mirrors the room, and this is
        /// the assertion that catches it in world terms rather than by the
        /// frame's own bookkeeping.
        /// </summary>
        [Test]
        public void RightMapsToPlusX()
        {
            const float yaw = 40f;
            var origin = new Vector3(-2f, 0.3f, 5f);
            GhostCoordinateFrame frame = BuildFrame(origin, yaw);

            Vector3 ghost = frame.WorldToGhost(origin + ExpectedWorldRight(yaw) * 1.75f);

            Assert.That(ghost.x, Is.EqualTo(1.75f).Within(Tolerance));
            Assert.That(ghost.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ghost.z, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void FrameAxesMatchTheCameraYawTheLockWasTakenAt()
        {
            foreach (float yaw in new[] { -170f, -95f, -20f, 0f, 35f, 110f, 179f })
            {
                GhostCoordinateFrame frame = BuildFrame(Vector3.zero, yaw);

                Assert.That(
                    Vector3.Angle(frame.Forward, ExpectedWorldForward(yaw)),
                    Is.LessThan(0.05f),
                    $"Forward axis is wrong at yaw {yaw}.");
                Assert.That(
                    Vector3.Angle(frame.Right, ExpectedWorldRight(yaw)),
                    Is.LessThan(0.05f),
                    $"Right axis is wrong at yaw {yaw}; a mirrored frame lands 180 degrees out.");
            }
        }

        /// <summary>
        /// The frame must have the same handedness as Unity's world axes, where
        /// <c>Cross(right, up) == forward</c>. A mirrored frame would give -1
        /// and would reflect every captured room, which nothing downstream
        /// could detect: area, wall length and closure error are all unchanged
        /// by a reflection.
        /// </summary>
        [Test]
        public void FrameHasUnityHandednessForEveryYaw()
        {
            for (float yaw = -180f; yaw <= 180f; yaw += 17.5f)
            {
                GhostCoordinateFrame frame = BuildFrame(Vector3.zero, yaw);

                float handedness = Vector3.Dot(
                    Vector3.Cross(frame.Right, frame.Up),
                    frame.Forward);

                Assert.That(
                    handedness,
                    Is.EqualTo(1f).Within(1e-3f),
                    $"Frame at yaw {yaw} is mirrored; Cross(right, up) must equal forward.");
            }
        }

        [Test]
        public void FrameAxesAreOrthonormal()
        {
            GhostCoordinateFrame frame = BuildFrame(Vector3.zero, 123f, 55f);

            Assert.That(frame.Right.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(frame.Up.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(frame.Forward.magnitude, Is.EqualTo(1f).Within(Tolerance));

            Assert.That(Vector3.Dot(frame.Right, frame.Up), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(Vector3.Dot(frame.Up, frame.Forward), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(Vector3.Dot(frame.Forward, frame.Right), Is.EqualTo(0f).Within(Tolerance));
        }

        /// <summary>The forward axis lies on the floor, never tilted by camera pitch.</summary>
        [Test]
        public void ForwardAxisIsHorizontalRegardlessOfCameraPitch()
        {
            foreach (float pitch in new[] { -70f, -30f, 0f, 30f, 70f, 85f })
            {
                GhostCoordinateFrame frame = BuildFrame(Vector3.zero, 40f, pitch);

                Assert.That(
                    frame.Forward.y,
                    Is.EqualTo(0f).Within(Tolerance),
                    $"Camera pitch {pitch} leaked into the forward axis.");
                Assert.That(frame.Right.y, Is.EqualTo(0f).Within(Tolerance));
            }
        }

        [Test]
        public void WorldToGhostAndBackRoundTrips()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(1.25f, -0.9f, 4.5f), 62f);

            var samples = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(3.2f, 1.1f, -4.4f),
                new Vector3(-7.75f, 2.4f, 0.02f),
                new Vector3(1e-3f, -1e-3f, 1e-3f),
                new Vector3(120f, -35f, 88f)
            };

            foreach (Vector3 world in samples)
            {
                Vector3 roundTripped = frame.GhostToWorld(frame.WorldToGhost(world));

                Assert.That(
                    Vector3.Distance(roundTripped, world),
                    Is.LessThan(1e-3f),
                    $"World->Ghost->World lost {world}.");
            }
        }

        [Test]
        public void GhostToWorldAndBackRoundTrips()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(-3f, 2.2f, 1f), -95f);

            var samples = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2.5f, 0f, 3.5f),
                new Vector3(-1.1f, 2.45f, 0.75f)
            };

            foreach (Vector3 ghost in samples)
            {
                Vector3 roundTripped = frame.WorldToGhost(frame.GhostToWorld(ghost));

                Assert.That(
                    Vector3.Distance(roundTripped, ghost),
                    Is.LessThan(1e-3f),
                    $"Ghost->World->Ghost lost {ghost}.");
            }
        }

        /// <summary>
        /// Every point on the locked floor plane reads as Ghost y = 0, which is
        /// the invariant scene schema v1 states for corner positions.
        /// </summary>
        [Test]
        public void EveryPointOnTheLockedFloorMapsToGhostYZero()
        {
            var origin = new Vector3(2f, -1.32f, -4f);
            GhostCoordinateFrame frame = BuildFrame(origin, 77f);

            for (float x = -5f; x <= 5f; x += 2.5f)
            {
                for (float z = -5f; z <= 5f; z += 2.5f)
                {
                    var onFloor = new Vector3(origin.x + x, origin.y, origin.z + z);

                    Assert.That(
                        frame.WorldToGhost(onFloor).y,
                        Is.EqualTo(0f).Within(Tolerance),
                        $"Floor point {onFloor} did not land on Ghost y = 0.");
                }
            }
        }

        [Test]
        public void FloorWorldYIsTheLockedOriginHeight()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(2f, -1.32f, -4f));

            Assert.That(frame.FloorWorldY, Is.EqualTo(-1.32f).Within(Tolerance));
        }

        // ---- Camera Y offset -------------------------------------------------

        /// <summary>
        /// XR Origin puts the AR camera under a Camera Offset whose local Y is
        /// <c>CameraYOffset</c> (1.1176 in this project) because ARKit reports
        /// a Device tracking origin. <c>XROrigin.OnBeforeRender</c> assigns that
        /// same Camera Offset world pose to <c>TrackablesParent</c>, so raycast
        /// hits carry the identical offset.
        ///
        /// This test reproduces that: it shifts the floor hit and the camera by
        /// one shared offset and asserts nothing in Ghost space moves, and in
        /// particular that the floor stays at y = 0. The offset is subtracted
        /// away by the frame's own origin.
        /// </summary>
        [Test]
        public void CameraYOffsetDoesNotMoveTheNormalizedFloor()
        {
            const float cameraYOffset = 1.1176f;

            Vector3 forward = Quaternion.Euler(35f, 25f, 0f) * Vector3.forward;
            var rawFloor = new Vector3(0.4f, -1.45f, 2.2f);
            var rawCamera = new Vector3(0.1f, 0.02f, 0.3f);

            var offset = new Vector3(0f, cameraYOffset, 0f);

            Assert.IsTrue(FloorLockController.TryBuildFrame(rawFloor, forward, out var plain));
            Assert.IsTrue(FloorLockController.TryBuildFrame(rawFloor + offset, forward, out var offsetFrame));

            // The floor origin itself.
            Assert.That(offsetFrame.WorldToGhost(rawFloor + offset).y, Is.EqualTo(0f).Within(Tolerance));

            // The camera, read in the same offset world space.
            Vector3 plainCameraGhost = plain.WorldToGhost(rawCamera);
            Vector3 offsetCameraGhost = offsetFrame.WorldToGhost(rawCamera + offset);

            Assert.That(
                Vector3.Distance(plainCameraGhost, offsetCameraGhost),
                Is.LessThan(1e-4f),
                "A camera Y offset shared by the floor hit and the camera must cancel in Ghost space.");

            // And the eye height above the floor is still the true one.
            Assert.That(
                offsetCameraGhost.y,
                Is.EqualTo(rawCamera.y - rawFloor.y).Within(Tolerance));
        }

        /// <summary>
        /// The floor stays at Ghost y = 0 for any offset magnitude, not just
        /// 1.1176 — the offset is only a default and a future task may change it.
        /// </summary>
        [Test]
        public void FloorStaysAtGhostZeroForAnyCameraYOffset()
        {
            Vector3 forward = Quaternion.Euler(20f, -140f, 0f) * Vector3.forward;
            var rawFloor = new Vector3(-1f, -1.6f, 0.5f);

            foreach (float offset in new[] { 0f, 0.5f, 1.1176f, 1.8f, -0.4f })
            {
                var shiftedFloor = rawFloor + new Vector3(0f, offset, 0f);
                Assert.IsTrue(FloorLockController.TryBuildFrame(shiftedFloor, forward, out var frame));

                Assert.That(
                    frame.WorldToGhost(shiftedFloor).y,
                    Is.EqualTo(0f).Within(Tolerance),
                    $"Camera Y offset {offset} moved the floor off Ghost y = 0.");
                Assert.That(frame.FloorWorldY, Is.EqualTo(shiftedFloor.y).Within(Tolerance));
            }
        }

        // ---- Ray / floor intersection ---------------------------------------

        /// <summary>
        /// The capture arithmetic S3 will use: take a world camera ray, convert
        /// it into Ghost space, and intersect it with the floor at y = 0.
        /// </summary>
        [Test]
        public void CameraRayIntersectsTheGhostFloorAtTheAimedPoint()
        {
            var floorOrigin = new Vector3(1f, -1.5f, 2f);
            GhostCoordinateFrame frame = BuildFrame(floorOrigin, 30f);

            // Aim from 1.5 m above the floor at a point 2 m along +X and 3 m
            // along +Z of the locked frame.
            Vector3 targetWorld = frame.GhostToWorld(new Vector3(2f, 0f, 3f));
            Vector3 eyeWorld = frame.GhostToWorld(new Vector3(0f, 1.5f, 0f));
            var worldRay = new Ray(eyeWorld, (targetWorld - eyeWorld).normalized);

            Ray ghostRay = frame.WorldRayToGhost(worldRay);

            Assert.IsTrue(
                RayPlaneMath.TryIntersectHorizontalPlane(ghostRay, 0f, out Vector3 hit),
                "A ray aimed down at the floor must intersect it.");

            Assert.That(hit.x, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(hit.y, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(hit.z, Is.EqualTo(3f).Within(1e-3f));
        }

        [Test]
        public void RayParallelToTheFloorIsRejected()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(0f, -1.5f, 0f), 12f);

            // Dead level, one and a half metres up. It meets the floor nowhere,
            // or infinitely far away, and extrapolating either would corrupt a scan.
            Vector3 eyeWorld = frame.GhostToWorld(new Vector3(0f, 1.5f, 0f));
            var worldRay = new Ray(eyeWorld, frame.Forward);

            Ray ghostRay = frame.WorldRayToGhost(worldRay);

            Assert.That(Mathf.Abs(ghostRay.direction.y), Is.LessThan(RayPlaneMath.ParallelEpsilon));
            Assert.IsFalse(RayPlaneMath.TryIntersectHorizontalPlane(ghostRay, 0f, out _));
        }

        [Test]
        public void GrazingRayIsRejectedRatherThanExtrapolated()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(0f, -1.5f, 0f));

            // Below the parallel epsilon but not exactly zero: the case that
            // would otherwise place a corner kilometres away.
            var ghostRay = new Ray(
                new Vector3(0f, 1.5f, 0f),
                new Vector3(0f, -RayPlaneMath.ParallelEpsilon * 0.5f, 1f));

            Assert.IsFalse(RayPlaneMath.TryIntersectHorizontalPlane(ghostRay, 0f, out _));
        }

        /// <summary>
        /// A ray pointing up, away from the floor, meets the floor plane only
        /// behind the camera. That intersection is real arithmetically and
        /// nonsense physically, so it must be refused.
        /// </summary>
        [Test]
        public void RayAimedAwayFromTheFloorIsRejectedAsBehindTheCamera()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(0f, -1.5f, 0f), 48f);

            Vector3 eyeWorld = frame.GhostToWorld(new Vector3(0f, 1.5f, 0f));
            var worldRay = new Ray(eyeWorld, (frame.Forward + Vector3.up).normalized);

            Ray ghostRay = frame.WorldRayToGhost(worldRay);

            Assert.That(ghostRay.direction.y, Is.GreaterThan(0f));
            Assert.IsFalse(RayPlaneMath.TryIntersectHorizontalPlane(ghostRay, 0f, out _));
        }

        [Test]
        public void RayFromBelowTheFloorAimedDownIsRejected()
        {
            GhostCoordinateFrame frame = BuildFrame(new Vector3(0f, -1.5f, 0f));

            // Under the floor, still aiming down: the plane is behind the ray.
            var ghostRay = new Ray(new Vector3(0f, -0.5f, 0f), new Vector3(0f, -1f, 0.2f).normalized);

            Assert.IsFalse(RayPlaneMath.TryIntersectHorizontalPlane(ghostRay, 0f, out _));
        }

        // ---- Stability -------------------------------------------------------

        /// <summary>
        /// The frame is captured once. Moving the camera afterwards changes
        /// nothing about it, and a point captured before the move keeps the
        /// same Ghost coordinates after it.
        /// </summary>
        [Test]
        public void FrameIsUnchangedByLaterCameraMotion()
        {
            var floorOrigin = new Vector3(0.5f, -1.4f, 1.5f);
            GhostCoordinateFrame frame = BuildFrame(floorOrigin, 15f);

            Vector3 originBefore = frame.Origin;
            Vector3 rightBefore = frame.Right;
            Vector3 upBefore = frame.Up;
            Vector3 forwardBefore = frame.Forward;

            var probeWorld = new Vector3(2.4f, -1.4f, 3.9f);
            Vector3 ghostBefore = frame.WorldToGhost(probeWorld);

            // Building a second, wildly different frame must not disturb the
            // first: GhostCoordinateFrame is immutable, and nothing about it is
            // derived from live XR Origin state.
            Assert.IsTrue(FloorLockController.TryBuildFrame(
                new Vector3(3f, 1.9f, -2.5f),
                Quaternion.Euler(-60f, 200f, 12f) * Vector3.forward,
                out _));

            Assert.That(frame.Origin, Is.EqualTo(originBefore).Using(Vector3EqualityComparer()));
            Assert.That(frame.Right, Is.EqualTo(rightBefore).Using(Vector3EqualityComparer()));
            Assert.That(frame.Up, Is.EqualTo(upBefore).Using(Vector3EqualityComparer()));
            Assert.That(frame.Forward, Is.EqualTo(forwardBefore).Using(Vector3EqualityComparer()));

            Assert.That(
                Vector3.Distance(frame.WorldToGhost(probeWorld), ghostBefore),
                Is.LessThan(Tolerance),
                "A world point's Ghost coordinates must not drift when the camera moves.");
        }

        private static System.Collections.Generic.IEqualityComparer<Vector3> Vector3EqualityComparer()
        {
            return new ApproximateVector3Comparer(Tolerance);
        }

        private sealed class ApproximateVector3Comparer
            : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            private readonly float tolerance;

            public ApproximateVector3Comparer(float tolerance) => this.tolerance = tolerance;

            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

            public int GetHashCode(Vector3 value) => value.GetHashCode();
        }
    }
}
