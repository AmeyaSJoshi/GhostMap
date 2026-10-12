using System;
using GhostMap.Scanner.Capture;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Task R11: what makes an acceptable object photo, where an object is on
    /// screen for one, and the screen-to-image mapping used to crop it.
    /// </summary>
    public sealed class ObjectPhotoTests
    {
        private const float Tolerance = 1e-3f;
        private static readonly Vector2 Screen = new Vector2(1000f, 2000f);

        /// <summary>A pinhole camera at the world origin looking down +Z.</summary>
        private static Vector3 Pinhole(Vector3 world)
            => new Vector3(Screen.x / 2f + 800f * world.x / world.z, Screen.y / 2f + 800f * world.y / world.z, world.z);

        private static GhostCoordinateFrame Frame()
        {
            // The floor 1.5 m below the camera, Ghost axes on world axes.
            Assert.IsTrue(FloorLockController.TryBuildFrame(new Vector3(0f, -1.5f, 0f), Vector3.forward, out GhostCoordinateFrame frame));
            return frame;
        }

        private static SceneObjectModel Box(float x, float z, float size = 1f) => new SceneObjectModel
        {
            id = "box",
            type = "generic",
            center = Vec3Dto.FromVector3(new Vector3(x, 0f, z)),
            widthM = size,
            depthM = size,
            heightM = size
        };

        // -------------------------------------------------------------------
        // IsAcceptable
        // -------------------------------------------------------------------

        [Test]
        public void IsAcceptable_TakesAJpegWithinTheSizeLimit()
        {
            Assert.IsTrue(ObjectPhotoRules.IsAcceptable(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }));

            var largest = new byte[ObjectPhotoRules.MaxBytes];
            largest[0] = 0xFF; largest[1] = 0xD8; largest[2] = 0xFF;
            Assert.IsTrue(ObjectPhotoRules.IsAcceptable(largest));
        }

        [Test]
        public void IsAcceptable_RefusesAnythingElse()
        {
            Assert.IsFalse(ObjectPhotoRules.IsAcceptable(null));
            Assert.IsFalse(ObjectPhotoRules.IsAcceptable(new byte[] { 0xFF, 0xD8, 0xFF }));
            Assert.IsFalse(ObjectPhotoRules.IsAcceptable(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }), "PNG");

            var tooLarge = new byte[ObjectPhotoRules.MaxBytes + 1];
            tooLarge[0] = 0xFF; tooLarge[1] = 0xD8; tooLarge[2] = 0xFF;
            Assert.IsFalse(ObjectPhotoRules.IsAcceptable(tooLarge));
        }

        [Test]
        public void Pad_GrowsTheBoxAndKeepsItOnScreen()
        {
            Rect padded = ObjectPhotoRules.Pad(new Rect(100f, 100f, 200f, 100f), Screen);
            Assert.AreEqual(Rect.MinMaxRect(80f, 90f, 320f, 210f), padded);

            Rect clamped = ObjectPhotoRules.Pad(new Rect(0f, 1950f, 1000f, 50f), Screen);
            Assert.AreEqual(Rect.MinMaxRect(0f, 1945f, 1000f, 2000f), clamped);
        }

        // -------------------------------------------------------------------
        // TryFrameObject
        // -------------------------------------------------------------------

        [Test]
        public void TryFrameObject_BoxesAnObjectInView_Padded()
        {
            Assert.IsTrue(ObjectPhotoRules.TryFrameObject(Box(0f, 4f), Frame(), Pinhole, Screen, out Rect box));

            // Nearest face at z 3.5: x from -0.5 to 0.5 -> 500 +- 114.3 px; padded by 10 %.
            Assert.AreEqual(500f, box.center.x, 0.5f);
            Assert.AreEqual(2f * 114.29f * 1.2f, box.width, 1f);
            Assert.Less(box.yMin, Screen.y / 2f, "the box reaches below the horizon, where the floor is");
        }

        [Test]
        public void TryFrameObject_RefusesAnObjectBehindTheCamera()
        {
            Assert.IsFalse(ObjectPhotoRules.TryFrameObject(Box(0f, -4f), Frame(), Pinhole, Screen, out _));
            Assert.IsFalse(ObjectPhotoRules.TryFrameObject(Box(0f, 0.3f), Frame(), Pinhole, Screen, out _), "straddling the camera");
        }

        [Test]
        public void TryFrameObject_RefusesAnObjectMostlyOffScreen()
        {
            // Half of x = 500 px of screen at z 4 is 2.5 m; an object at x 2.7 is mostly past the edge.
            Assert.IsFalse(ObjectPhotoRules.TryFrameObject(Box(2.7f, 4f), Frame(), Pinhole, Screen, out _));
            Assert.IsTrue(ObjectPhotoRules.TryFrameObject(Box(1.8f, 4f), Frame(), Pinhole, Screen, out _));
        }

        [Test]
        public void TryFrameObject_RefusesMissingInputs()
        {
            Assert.IsFalse(ObjectPhotoRules.TryFrameObject(null, Frame(), Pinhole, Screen, out _));
            Assert.IsFalse(ObjectPhotoRules.TryFrameObject(Box(0f, 4f), null, Pinhole, Screen, out _));
            Assert.IsFalse(ObjectPhotoRules.TryFrameObject(Box(0f, 4f), Frame(), null, Screen, out _));
            Assert.IsFalse(ObjectPhotoRules.TryFrameObject(Box(0f, 4f), Frame(), Pinhole, Vector2.zero, out _));
        }

        // -------------------------------------------------------------------
        // DetectionGeometry.TryScreenToVisionBox
        // -------------------------------------------------------------------

        [TestCase(0f, 1f, -1f, 0f, 1f, 0f)]
        [TestCase(0f, 1f, -2f, 0f, 1.5f, 0f)]
        [TestCase(1f, 0f, 0f, 1f, 0f, 0f)]
        public void TryScreenToVisionBox_UndoesVisionBoxToScreen(float a, float b, float c, float d, float tx, float ty)
        {
            var transform = new DisplayTransform(a, b, c, d, tx, ty);
            var vision = new Rect(0.3f, 0.35f, 0.2f, 0.25f);

            Rect screen = DetectionGeometry.VisionBoxToScreen(vision, transform, Screen);
            Assert.IsTrue(DetectionGeometry.TryScreenToVisionBox(screen, transform, Screen, out Rect back));

            Assert.AreEqual(vision.xMin, back.xMin, Tolerance);
            Assert.AreEqual(vision.yMin, back.yMin, Tolerance);
            Assert.AreEqual(vision.xMax, back.xMax, Tolerance);
            Assert.AreEqual(vision.yMax, back.yMax, Tolerance);
        }

        [Test]
        public void TryScreenToVisionBox_ClampsToTheImage()
        {
            // The cropped transform shows only the middle half of the image's
            // width, so the screen's width is that half; a box taller than the
            // screen runs past the image's top and bottom and is clamped.
            var cropped = new DisplayTransform(0f, 1f, -2f, 0f, 1.5f, 0f);

            Assert.IsTrue(DetectionGeometry.TryScreenToVisionBox(new Rect(0f, -100f, 1000f, 2200f), cropped, Screen, out Rect box));

            Assert.AreEqual(0.25f, box.xMin, Tolerance);
            Assert.AreEqual(0.75f, box.xMax, Tolerance);
            Assert.AreEqual(0f, box.yMin, Tolerance);
            Assert.AreEqual(1f, box.yMax, Tolerance);
        }

        [Test]
        public void TryScreenToVisionBox_RefusesADegenerateTransformOrScreen()
        {
            var flat = new DisplayTransform(1f, 1f, 1f, 1f, 0f, 0f);
            Assert.IsFalse(DetectionGeometry.TryScreenToVisionBox(new Rect(0f, 0f, 10f, 10f), flat, Screen, out _));
            Assert.IsFalse(DetectionGeometry.TryScreenToVisionBox(new Rect(0f, 0f, 10f, 10f), DisplayTransform.Identity, Vector2.zero, out _));
        }
    }
}
