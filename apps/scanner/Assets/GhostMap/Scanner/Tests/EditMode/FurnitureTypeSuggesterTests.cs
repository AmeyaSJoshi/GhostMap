using GhostMap.Scanner.Capture;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// ADR-0007: ARKit's plane label plus measured size → a pre-selected
    /// furniture type. Each case is a real-world object as ARKit would see it.
    /// </summary>
    public sealed class FurnitureTypeSuggesterTests
    {
        private const PlaneClassifications None = PlaneClassifications.None;
        private const PlaneClassifications Table = PlaneClassifications.Table;
        private const PlaneClassifications Seat = PlaneClassifications.Seat;

        [TestCase(Table, 1.40f, 0.70f, 0.74f, "desk", TestName = "Table label, desk-sized, desk height → desk")]
        [TestCase(Table, 1.60f, 0.90f, 0.75f, "table", TestName = "Table label, deep top → table")]
        [TestCase(Table, 1.00f, 0.55f, 0.42f, "table", TestName = "Table label, coffee-table height → table")]
        [TestCase(Table, 1.10f, 0.50f, 0.95f, "dresser", TestName = "Table label, above desk height → dresser")]
        [TestCase(Seat, 0.50f, 0.50f, 0.46f, "chair", TestName = "Seat label, small → chair")]
        [TestCase(Seat, 1.90f, 0.60f, 0.45f, "couch", TestName = "Seat label, long → couch")]
        [TestCase(PlaneClassifications.Couch, 0.90f, 0.60f, 0.45f, "couch", TestName = "Couch label → couch")]
        [TestCase(Seat, 1.90f, 1.40f, 0.55f, "bed", TestName = "Seat label, bed-sized → bed")]
        [TestCase(None, 1.95f, 1.35f, 0.58f, "bed", TestName = "No label, bed-sized and low → bed")]
        [TestCase(None, 1.20f, 0.60f, 0.74f, "desk", TestName = "No label, desk-sized at desk height → desk")]
        [TestCase(None, 1.20f, 0.45f, 1.00f, "dresser", TestName = "No label, chest height → dresser")]
        [TestCase(None, 0.60f, 0.50f, 0.45f, "generic", TestName = "No label, low and small → generic")]
        public void Suggests(PlaneClassifications label, float widthM, float depthM, float heightM, string expected)
        {
            Assert.AreEqual(expected, FurnitureTypeSuggester.Suggest(label, widthM, depthM, heightM));
        }

        [Test]
        public void WidthAndDepthOrder_DoesNotMatter()
        {
            // A plane's axes are arbitrary, so a desk may come out 0.7 x 1.4.
            Assert.AreEqual(
                FurnitureTypeSuggester.Suggest(Table, 1.4f, 0.7f, 0.74f),
                FurnitureTypeSuggester.Suggest(Table, 0.7f, 1.4f, 0.74f));
            Assert.AreEqual(
                FurnitureTypeSuggester.Suggest(None, 2.0f, 1.4f, 0.55f),
                FurnitureTypeSuggester.Suggest(None, 1.4f, 2.0f, 0.55f));
        }

        [Test]
        public void EverySuggestion_IsASupportedType()
        {
            PlaneClassifications[] labels =
            {
                None, Table, Seat, PlaneClassifications.Couch, PlaneClassifications.Floor, PlaneClassifications.Other
            };

            foreach (PlaneClassifications label in labels)
            {
                for (float h = 0.2f; h <= 1.4f; h += 0.05f)
                {
                    for (float w = 0.3f; w <= 3f; w += 0.3f)
                    {
                        string type = FurnitureTypeSuggester.Suggest(label, w, w * 0.6f, h);
                        Assert.IsTrue(FurnitureValidator.IsSupportedType(type), $"{label} {w}x{h} → {type}");
                    }
                }
            }
        }
    }
}
