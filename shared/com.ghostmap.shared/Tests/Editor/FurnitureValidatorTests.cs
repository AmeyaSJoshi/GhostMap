using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using NUnit.Framework;

namespace GhostMap.Shared.Tests
{
    /// <summary>Task F2 tests for <see cref="FurnitureValidator"/>.</summary>
    public sealed class FurnitureValidatorTests
    {
        private const float Tolerance = 1e-4f;

        private static SceneObjectModel Desk()
        {
            return new SceneObjectModel
            {
                id = "desk-1",
                type = "desk",
                center = new Vec3Dto(2f, 0f, 1f),
                yawDeg = 0f,
                widthM = 1.40f,
                depthM = 0.70f,
                heightM = 0.75f
            };
        }

        [Test]
        public void AcceptsValidDesk()
        {
            Assert.IsTrue(FurnitureValidator.Validate(Desk()).IsValid);
        }

        [Test]
        public void AcceptsEverySupportedType()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                SceneObjectModel model = Desk();
                model.type = type;

                Assert.IsTrue(FurnitureValidator.Validate(model).IsValid,
                    $"Type '{type}' must be accepted.");
            }
        }

        [Test]
        public void RejectsUnknownType()
        {
            SceneObjectModel model = Desk();
            model.type = "spaceship";

            ValidationResult result = FurnitureValidator.Validate(model);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("type", result.Error.ToLowerInvariant());
        }

        [Test]
        public void RejectsNegativeDimension()
        {
            SceneObjectModel model = Desk();
            model.widthM = -1f;

            ValidationResult result = FurnitureValidator.Validate(model);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("width", result.Error.ToLowerInvariant());
        }

        [Test]
        public void RejectsZeroDimension()
        {
            SceneObjectModel model = Desk();
            model.heightM = 0f;

            Assert.IsFalse(FurnitureValidator.Validate(model).IsValid);
        }

        [Test]
        public void RejectsImplausiblyLargeDimension()
        {
            SceneObjectModel model = Desk();
            model.depthM = 50f;

            Assert.IsFalse(FurnitureValidator.Validate(model).IsValid);
        }

        [Test]
        public void RejectsNonFiniteValues()
        {
            SceneObjectModel model = Desk();
            model.yawDeg = float.PositiveInfinity;

            Assert.IsFalse(FurnitureValidator.Validate(model).IsValid);
        }

        [Test]
        public void RejectsNullModel()
        {
            Assert.IsFalse(FurnitureValidator.Validate(null).IsValid);
        }

        [Test]
        public void ProvidesDefaultDimensionsForEverySupportedType()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                bool found = FurnitureValidator.TryGetDefaultDimensions(
                    type, out float w, out float d, out float h);

                Assert.IsTrue(found, $"Type '{type}' must have default dimensions.");
                Assert.Greater(w, 0f);
                Assert.Greater(d, 0f);
                Assert.Greater(h, 0f);
            }
        }

        [Test]
        public void BedDefaultsMatchThePlan()
        {
            FurnitureValidator.TryGetDefaultDimensions("bed", out float w, out float d, out float h);

            Assert.AreEqual(1.52f, w, Tolerance);
            Assert.AreEqual(2.03f, d, Tolerance);
            Assert.AreEqual(0.60f, h, Tolerance);
        }

        [Test]
        public void DefaultDimensionsAreThemselvesValid()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                FurnitureValidator.TryGetDefaultDimensions(type, out float w, out float d, out float h);

                SceneObjectModel model = Desk();
                model.type = type;
                model.widthM = w;
                model.depthM = d;
                model.heightM = h;

                Assert.IsTrue(FurnitureValidator.Validate(model).IsValid,
                    $"Default dimensions for '{type}' must pass validation.");
            }
        }
    }
}
