using GhostMap.Viewer.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V4's <see cref="OrbitCameraRig"/>: the orbit camera's state and
    /// maths, deliberately a plain C# class with no <c>MonoBehaviour</c> and no
    /// <c>Input</c> dependency, so every clamp and framing rule is testable in
    /// EditMode. <see cref="OrbitCameraController"/> is the thin layer that
    /// reads the mouse and pushes this onto a <c>Transform</c>.
    /// </summary>
    public sealed class OrbitCameraRigTests
    {
        private const float Fov = 60f;
        private const float Aspect = 16f / 9f;

        private static Bounds RoomBox(float width = 4f, float height = 2.5f, float depth = 3f)
            => new Bounds(new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f),
                          new Vector3(width, height, depth));

        private static OrbitCameraRig Framed(Bounds bounds)
        {
            var rig = new OrbitCameraRig();
            rig.Frame(bounds, Fov, Aspect);
            return rig;
        }

        [Test]
        public void FrameTargetsTheRoomCentre()
        {
            Bounds bounds = RoomBox();
            OrbitCameraRig rig = Framed(bounds);

            Assert.AreEqual(bounds.center.x, rig.Target.x, 1e-4f);
            Assert.AreEqual(bounds.center.y, rig.Target.y, 1e-4f);
            Assert.AreEqual(bounds.center.z, rig.Target.z, 1e-4f);
        }

        [Test]
        public void FrameFitsTheWholeRoomInsideTheFrustum()
        {
            Bounds bounds = RoomBox();
            OrbitCameraRig rig = Framed(bounds);

            // Every corner of the room box must fall inside the camera frustum.
            var camera = new GameObject("FrustumProbe", typeof(Camera));

            try
            {
                var cam = camera.GetComponent<Camera>();
                cam.fieldOfView = Fov;
                cam.aspect = Aspect;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;
                camera.transform.position = rig.Position;
                camera.transform.rotation = rig.Rotation;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z);

                    Vector3 viewport = cam.WorldToViewportPoint(corner);

                    Assert.Greater(viewport.z, 0f, $"Corner {corner} is behind the camera.");
                    Assert.GreaterOrEqual(viewport.x, -1e-3f, $"Corner {corner} is off-screen left.");
                    Assert.LessOrEqual(viewport.x, 1f + 1e-3f, $"Corner {corner} is off-screen right.");
                    Assert.GreaterOrEqual(viewport.y, -1e-3f, $"Corner {corner} is off-screen below.");
                    Assert.LessOrEqual(viewport.y, 1f + 1e-3f, $"Corner {corner} is off-screen above.");
                }
            }
            finally
            {
                Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void FrameProducesUsableFramingForVeryDifferentRoomSizes()
        {
            OrbitCameraRig small = Framed(RoomBox(2f, 2.2f, 2f));
            OrbitCameraRig large = Framed(RoomBox(9f, 3.6f, 7f));

            Assert.Less(small.Distance, large.Distance,
                "A bigger room must be framed from further away.");
            Assert.GreaterOrEqual(small.Distance, OrbitCameraRig.MinDistanceM);
            Assert.LessOrEqual(large.Distance, OrbitCameraRig.MaxDistanceM);
            Assert.IsTrue(small.IsValid);
            Assert.IsTrue(large.IsValid);
        }

        [Test]
        public void FrameIsDeterministicAndResetsTheViewingAngles()
        {
            OrbitCameraRig rig = Framed(RoomBox());
            rig.Orbit(123f, 20f);
            rig.Zoom(5f);

            rig.Frame(RoomBox(), Fov, Aspect);

            Assert.AreEqual(OrbitCameraRig.HomeYawDeg, rig.YawDeg, 1e-4f,
                "Frame doubles as the reset/home view.");
            Assert.AreEqual(OrbitCameraRig.HomePitchDeg, rig.PitchDeg, 1e-4f);
        }

        [Test]
        public void CameraIsAlwaysAboveTheFloorAndLooksDownward()
        {
            OrbitCameraRig rig = Framed(RoomBox());

            Assert.Greater(rig.Position.y, rig.FloorY, "The camera must never go below the floor.");
            Assert.Greater(rig.PitchDeg, 0f, "A downward pitch keeps the camera above the floor.");
            Assert.Less((rig.Rotation * Vector3.forward).y, 0f, "The home view looks downward.");
        }

        [Test]
        public void OrbitChangesYawAndPitch()
        {
            OrbitCameraRig rig = Framed(RoomBox());
            float yaw = rig.YawDeg;
            float pitch = rig.PitchDeg;

            rig.Orbit(30f, 10f);

            Assert.AreNotEqual(yaw, rig.YawDeg);
            Assert.AreNotEqual(pitch, rig.PitchDeg);
            Assert.IsTrue(rig.IsValid);
        }

        [Test]
        public void OrbitClampsPitchSoTheCameraNeverDropsBelowTheFloorOrFlipsOver()
        {
            OrbitCameraRig rig = Framed(RoomBox());

            for (int i = 0; i < 100; i++)
            {
                rig.Orbit(0f, -50f);
            }

            Assert.AreEqual(OrbitCameraRig.MinPitchDeg, rig.PitchDeg, 1e-4f);
            Assert.Greater(rig.Position.y, rig.FloorY);

            for (int i = 0; i < 100; i++)
            {
                rig.Orbit(0f, 50f);
            }

            Assert.AreEqual(OrbitCameraRig.MaxPitchDeg, rig.PitchDeg, 1e-4f);
            Assert.IsTrue(rig.IsValid);
        }

        [Test]
        public void OrbitKeepsYawInAPredictableRange()
        {
            OrbitCameraRig rig = Framed(RoomBox());

            for (int i = 0; i < 40; i++)
            {
                rig.Orbit(37f, 0f);
            }

            Assert.GreaterOrEqual(rig.YawDeg, 0f);
            Assert.Less(rig.YawDeg, 360f, "Yaw must be wrapped, not accumulated without bound.");
            Assert.IsTrue(rig.IsValid);
        }

        [Test]
        public void ZoomInReducesDistanceAndZoomOutIncreasesIt()
        {
            OrbitCameraRig rig = Framed(RoomBox());
            float start = rig.Distance;

            rig.Zoom(1f);
            Assert.Less(rig.Distance, start, "Scrolling forward zooms in.");

            rig.Zoom(-2f);
            Assert.Greater(rig.Distance, start, "Scrolling back zooms out.");
        }

        [Test]
        public void ZoomClampsToASafeRange()
        {
            OrbitCameraRig rig = Framed(RoomBox());

            for (int i = 0; i < 500; i++)
            {
                rig.Zoom(1f);
            }

            Assert.AreEqual(OrbitCameraRig.MinDistanceM, rig.Distance, 1e-4f);
            Assert.IsTrue(rig.IsValid);

            for (int i = 0; i < 500; i++)
            {
                rig.Zoom(-1f);
            }

            Assert.AreEqual(OrbitCameraRig.MaxDistanceM, rig.Distance, 1e-4f);
            Assert.IsTrue(rig.IsValid);
        }

        [Test]
        public void ZoomNeverReachesZeroOrNegativeDistance()
        {
            OrbitCameraRig rig = Framed(RoomBox());

            for (int i = 0; i < 1000; i++)
            {
                rig.Zoom(10f);
                Assert.Greater(rig.Distance, 0f);
            }
        }

        [Test]
        public void PanMovesTheOrbitTarget()
        {
            OrbitCameraRig rig = Framed(RoomBox());
            Vector3 before = rig.Target;

            rig.Pan(1f, 0.5f);

            Assert.AreNotEqual(before, rig.Target);
            Assert.IsTrue(rig.IsValid);
        }

        [Test]
        public void PanNeverPushesTheTargetOrTheCameraBelowTheFloor()
        {
            OrbitCameraRig rig = Framed(RoomBox());

            for (int i = 0; i < 200; i++)
            {
                rig.Pan(0f, -5f);
            }

            Assert.GreaterOrEqual(rig.Target.y, rig.FloorY - 1e-4f);
            Assert.Greater(rig.Position.y, rig.FloorY);
        }

        [Test]
        public void DollhouseFramesTheRoomFromASteeperOverheadAngle()
        {
            Bounds bounds = RoomBox();

            OrbitCameraRig framed = Framed(bounds);
            var dollhouse = new OrbitCameraRig();
            dollhouse.Dollhouse(bounds, Fov, Aspect);

            Assert.AreEqual(OrbitCameraRig.DollhousePitchDeg, dollhouse.PitchDeg, 1e-4f);
            Assert.Greater(dollhouse.PitchDeg, framed.PitchDeg, "Dollhouse looks down more steeply.");
            Assert.AreEqual(bounds.center.x, dollhouse.Target.x, 1e-4f);
            Assert.AreEqual(bounds.center.z, dollhouse.Target.z, 1e-4f);
            Assert.Greater(dollhouse.Position.y, framed.Position.y);
            Assert.IsTrue(dollhouse.IsValid);
        }

        [Test]
        public void NoOperationEverProducesANaNTransform()
        {
            var rig = new OrbitCameraRig();
            rig.Frame(RoomBox(), Fov, Aspect);

            for (int i = 0; i < 50; i++)
            {
                rig.Orbit(17f, -9f);
                rig.Zoom(i % 2 == 0 ? 1f : -1f);
                rig.Pan(0.3f, -0.2f);

                Assert.IsTrue(rig.IsValid, $"Rig became invalid at iteration {i}.");
                Assert.IsFalse(float.IsNaN(rig.Position.x + rig.Position.y + rig.Position.z));

                Quaternion rotation = rig.Rotation;
                Assert.IsFalse(float.IsNaN(rotation.x + rotation.y + rotation.z + rotation.w));
            }
        }

        [Test]
        public void DegenerateBoundsStillProduceAUsableCamera()
        {
            var rig = new OrbitCameraRig();

            rig.Frame(new Bounds(Vector3.zero, Vector3.zero), Fov, Aspect);

            Assert.IsTrue(rig.IsValid);
            Assert.GreaterOrEqual(rig.Distance, OrbitCameraRig.MinDistanceM);
            Assert.Greater(rig.Position.y, rig.FloorY);
        }

        [Test]
        public void NonFiniteBoundsAreRejectedWithoutCorruptingTheRig()
        {
            OrbitCameraRig rig = Framed(RoomBox());
            float distance = rig.Distance;
            Vector3 target = rig.Target;

            rig.Frame(new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one), Fov, Aspect);

            Assert.IsTrue(rig.IsValid);
            Assert.AreEqual(distance, rig.Distance, 1e-4f, "A bad bounds must not move the camera.");
            Assert.AreEqual(target, rig.Target);
        }

        [Test]
        public void PositionAndRotationAgreeWithEachOther()
        {
            OrbitCameraRig rig = Framed(RoomBox());
            rig.Orbit(41f, 7f);

            // Looking from Position along Rotation's forward must hit the target.
            Vector3 forward = rig.Rotation * Vector3.forward;
            Vector3 toTarget = (rig.Target - rig.Position).normalized;

            Assert.AreEqual(1f, Vector3.Dot(forward, toTarget), 1e-3f,
                "The camera must actually look at its orbit target.");
            Assert.AreEqual(rig.Distance, Vector3.Distance(rig.Position, rig.Target), 1e-3f);
        }
    }
}
