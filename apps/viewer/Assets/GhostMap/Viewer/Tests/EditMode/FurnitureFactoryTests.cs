using System.Collections.Generic;
using System.Linq;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Validation;
using GhostMap.Viewer.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Viewer.Tests.EditMode
{
    /// <summary>
    /// Task V4's <see cref="FurnitureFactory"/> part geometry: deterministic,
    /// primitive-composed objects per implementation plan section 12.4.
    ///
    /// Parts are described in the object's own local frame — origin at the
    /// object's centre on the floor, +x width, +y up, +z depth — so yaw and
    /// world placement are applied exactly once, on the object root, and can
    /// be tested separately in <see cref="FurnitureRendererTests"/>.
    /// </summary>
    public sealed class FurnitureFactoryTests
    {
        private const float Tolerance = 1e-4f;

        private static SceneObjectModel Object(
            string type,
            float widthM = 1.2f,
            float depthM = 0.8f,
            float heightM = 0.9f,
            string id = "obj-1")
        {
            return new SceneObjectModel
            {
                id = id,
                type = type,
                center = new Vec3Dto(0f, 0f, 0f),
                yawDeg = 0f,
                widthM = widthM,
                depthM = depthM,
                heightM = heightM
            };
        }

        private static Bounds LocalBounds(IReadOnlyList<FurniturePartSpec> parts)
        {
            var bounds = new Bounds(parts[0].LocalCenter, parts[0].LocalSize);

            for (int i = 1; i < parts.Count; i++)
            {
                bounds.Encapsulate(new Bounds(parts[i].LocalCenter, parts[i].LocalSize));
            }

            return bounds;
        }

        [Test]
        public void EverySupportedTypeProducesParts()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                IReadOnlyList<FurniturePartSpec> parts = FurnitureFactory.BuildParts(Object(type));

                Assert.Greater(parts.Count, 0, $"Type '{type}' produced no geometry.");
            }
        }

        [Test]
        public void EverySupportedTypeFillsExactlyTheDeclaredBox()
        {
            // "Correct dimensions" is the headline requirement: whatever parts a
            // type is made of, their union must be exactly the W x D x H box the
            // SceneObjectModel declares, sitting on the floor.
            const float w = 1.2f;
            const float d = 0.8f;
            const float h = 0.9f;

            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                Bounds bounds = LocalBounds(FurnitureFactory.BuildParts(Object(type, w, d, h)));

                Assert.AreEqual(-w * 0.5f, bounds.min.x, Tolerance, $"{type} min x");
                Assert.AreEqual(w * 0.5f, bounds.max.x, Tolerance, $"{type} max x");
                Assert.AreEqual(0f, bounds.min.y, Tolerance, $"{type} must sit on the floor");
                Assert.AreEqual(h, bounds.max.y, Tolerance, $"{type} max y");
                Assert.AreEqual(-d * 0.5f, bounds.min.z, Tolerance, $"{type} min z");
                Assert.AreEqual(d * 0.5f, bounds.max.z, Tolerance, $"{type} max z");
            }
        }

        [Test]
        public void NoPartEscapesTheDeclaredBox()
        {
            const float w = 1.5f;
            const float d = 0.9f;
            const float h = 1.1f;

            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                foreach (FurniturePartSpec part in FurnitureFactory.BuildParts(Object(type, w, d, h)))
                {
                    Vector3 min = part.LocalCenter - part.LocalSize * 0.5f;
                    Vector3 max = part.LocalCenter + part.LocalSize * 0.5f;

                    Assert.GreaterOrEqual(min.x, -w * 0.5f - Tolerance, $"{type}/{part.Name}");
                    Assert.LessOrEqual(max.x, w * 0.5f + Tolerance, $"{type}/{part.Name}");
                    Assert.GreaterOrEqual(min.y, -Tolerance, $"{type}/{part.Name} dips below the floor");
                    Assert.LessOrEqual(max.y, h + Tolerance, $"{type}/{part.Name}");
                    Assert.GreaterOrEqual(min.z, -d * 0.5f - Tolerance, $"{type}/{part.Name}");
                    Assert.LessOrEqual(max.z, d * 0.5f + Tolerance, $"{type}/{part.Name}");
                }
            }
        }

        [Test]
        public void NoPartHasZeroOrNegativeSize()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                foreach (FurniturePartSpec part in FurnitureFactory.BuildParts(Object(type)))
                {
                    Assert.Greater(part.LocalSize.x, 0f, $"{type}/{part.Name}");
                    Assert.Greater(part.LocalSize.y, 0f, $"{type}/{part.Name}");
                    Assert.Greater(part.LocalSize.z, 0f, $"{type}/{part.Name}");
                }
            }
        }

        [Test]
        public void PartsScaleWithTheDeclaredDimensions()
        {
            Bounds small = LocalBounds(FurnitureFactory.BuildParts(Object("bed", 1f, 1f, 1f)));
            Bounds large = LocalBounds(FurnitureFactory.BuildParts(Object("bed", 2f, 3f, 1.5f)));

            Assert.AreEqual(1f, small.size.x, Tolerance);
            Assert.AreEqual(2f, large.size.x, Tolerance);
            Assert.AreEqual(3f, large.size.z, Tolerance);
            Assert.AreEqual(1.5f, large.size.y, Tolerance);
        }

        [Test]
        public void GenericIsASingleBoxMatchingWidthDepthHeight()
        {
            IReadOnlyList<FurniturePartSpec> parts =
                FurnitureFactory.BuildParts(Object("generic", 1.3f, 0.7f, 1.9f));

            Assert.AreEqual(1, parts.Count);
            Assert.AreEqual(new Vector3(1.3f, 1.9f, 0.7f), parts[0].LocalSize);
            Assert.AreEqual(new Vector3(0f, 0.95f, 0f), parts[0].LocalCenter);
        }

        [Test]
        public void EachTypeIsBuiltFromTheStructureTheImplementationPlanSpecifies()
        {
            // Section 12.4 names the parts per type. Asserting on the names keeps
            // the objects visually distinguishable rather than "anonymous boxes".
            void AssertHasParts(string type, params string[] expected)
            {
                string[] names = FurnitureFactory.BuildParts(Object(type))
                    .Select(p => p.Name)
                    .ToArray();

                foreach (string part in expected)
                {
                    Assert.IsTrue(
                        names.Any(n => n.StartsWith(part)),
                        $"Type '{type}' is missing part '{part}'; got: {string.Join(", ", names)}");
                }
            }

            AssertHasParts("bed", "Mattress", "Frame", "Headboard");
            AssertHasParts("desk", "Top", "Leg");
            AssertHasParts("chair", "Seat", "Back", "Leg");
            AssertHasParts("couch", "Base", "Back", "ArmLeft", "ArmRight", "Cushion");
            AssertHasParts("table", "Top", "Leg");
            AssertHasParts("dresser", "Body", "Drawer");
            AssertHasParts("tv", "Screen", "Stand");
        }

        [Test]
        public void DeskAndTableAreDistinguishableFromEachOther()
        {
            string[] desk = FurnitureFactory.BuildParts(Object("desk")).Select(p => p.Name).ToArray();
            string[] table = FurnitureFactory.BuildParts(Object("table")).Select(p => p.Name).ToArray();

            CollectionAssert.AreNotEqual(desk, table,
                "A desk and a table must not render as the same object.");
        }

        [Test]
        public void FourLeggedTypesGetFourLegs()
        {
            foreach (string type in new[] { "desk", "chair", "table" })
            {
                int legs = FurnitureFactory.BuildParts(Object(type))
                    .Count(p => p.Name.StartsWith("Leg"));

                Assert.AreEqual(4, legs, $"'{type}' should have four legs.");
            }
        }

        [Test]
        public void BuildPartsIsDeterministic()
        {
            foreach (string type in FurnitureValidator.SupportedTypes)
            {
                IReadOnlyList<FurniturePartSpec> first = FurnitureFactory.BuildParts(Object(type));
                IReadOnlyList<FurniturePartSpec> second = FurnitureFactory.BuildParts(Object(type));

                Assert.AreEqual(first.Count, second.Count, type);

                for (int i = 0; i < first.Count; i++)
                {
                    Assert.AreEqual(first[i].Name, second[i].Name);
                    Assert.AreEqual(first[i].LocalCenter, second[i].LocalCenter);
                    Assert.AreEqual(first[i].LocalSize, second[i].LocalSize);
                }
            }
        }

        [Test]
        public void UnsupportedTypeProducesNoPartsAndIsDiagnosed()
        {
            var diagnostics = new List<string>();

            IReadOnlyList<FurniturePartSpec> parts =
                FurnitureFactory.BuildParts(Object("spaceship"), diagnostics);

            Assert.AreEqual(0, parts.Count, "An unsupported type must not be invented into geometry.");
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void NullModelProducesNoPartsAndIsDiagnosed()
        {
            var diagnostics = new List<string>();

            Assert.AreEqual(0, FurnitureFactory.BuildParts(null, diagnostics).Count);
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void NonFiniteOrNonPositiveDimensionsProduceNoParts()
        {
            var diagnostics = new List<string>();

            Assert.AreEqual(0, FurnitureFactory.BuildParts(Object("bed", widthM: float.NaN), diagnostics).Count);
            Assert.AreEqual(0, FurnitureFactory.BuildParts(Object("bed", depthM: 0f), diagnostics).Count);
            Assert.AreEqual(0, FurnitureFactory.BuildParts(Object("bed", heightM: -1f), diagnostics).Count);
            Assert.AreEqual(3, diagnostics.Count);
        }

        [Test]
        public void BuildPartsDoesNotMutateTheModel()
        {
            SceneObjectModel model = Object("couch", 2.1f, 0.9f, 0.85f);
            model.yawDeg = 42f;

            FurnitureFactory.BuildParts(model);

            Assert.AreEqual(2.1f, model.widthM, Tolerance);
            Assert.AreEqual(0.9f, model.depthM, Tolerance);
            Assert.AreEqual(0.85f, model.heightM, Tolerance);
            Assert.AreEqual(42f, model.yawDeg, Tolerance);
        }
    }
}
