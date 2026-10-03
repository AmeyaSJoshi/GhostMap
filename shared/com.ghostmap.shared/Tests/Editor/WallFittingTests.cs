using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// ADR-0005 tests for <see cref="WallFitting"/>: fitting a wall to samples
    /// swept along its floor junction, and deriving corners from four walls.
    /// </summary>
    public sealed class WallFittingTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>
        /// Looser bound for values that come out of the eigen decomposition
        /// rather than straight arithmetic.
        /// </summary>
        private const float FitTolerance = 1e-3f;

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Samples spaced evenly along a line, exactly collinear.
        /// </summary>
        private static List<Vector3> Collinear(
            Vector3 start,
            Vector3 direction,
            float step,
            int count)
        {
            Vector3 unit = direction.normalized;
            var samples = new List<Vector3>(count);

            for (int i = 0; i < count; i++)
            {
                samples.Add(start + (unit * (step * i)));
            }

            return samples;
        }

        private static WallLine LineThrough(Vector3 point, Vector3 direction)
            => new WallLine(point, direction.normalized, 0f, 10f, 64);

        /// <summary>
        /// The four wall lines of an axis-aligned rectangle, in sweep order, so
        /// that wall[i] spans corner[i] -> corner[i + 1].
        /// </summary>
        private static List<WallLine> RectangleWalls(float width, float depth)
        {
            return new List<WallLine>
            {
                LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f)),
                LineThrough(new Vector3(width, 0f, 0f), new Vector3(0f, 0f, 1f)),
                LineThrough(new Vector3(0f, 0f, depth), new Vector3(1f, 0f, 0f)),
                LineThrough(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f))
            };
        }

        // -------------------------------------------------------------------
        // Fitting: the three orientations that matter
        // -------------------------------------------------------------------

        [Test]
        public void Fit_RecoversWallParallelToX()
        {
            List<Vector3> samples = Collinear(
                new Vector3(0f, 0f, 3f), new Vector3(1f, 0f, 0f), 0.5f, 8);

            Assert.IsTrue(WallFitting.TryFitWallLine(
                samples, out WallLine line, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.None, rejection);

            // Centroid of x = 0 .. 3.5.
            Assert.AreEqual(1.75f, line.Point.x, FitTolerance);
            Assert.AreEqual(3f, line.Point.z, FitTolerance);
            Assert.AreEqual(0f, line.Point.y, Tolerance);

            // Direction sign is arbitrary, so compare magnitude.
            Assert.AreEqual(1f, Mathf.Abs(line.Direction.x), FitTolerance);
            Assert.AreEqual(0f, line.Direction.z, FitTolerance);
            Assert.AreEqual(0f, line.Direction.y, Tolerance);

            Assert.AreEqual(0f, line.RmsResidualM, FitTolerance);
            Assert.AreEqual(3.5f, line.SpanM, FitTolerance);
            Assert.AreEqual(8, line.SampleCount);
        }

        /// <summary>
        /// The case that breaks ordinary least squares: a wall parallel to Z has
        /// no single-valued <c>z = mx + c</c> form. Total least squares must not
        /// care.
        /// </summary>
        [Test]
        public void Fit_RecoversWallParallelToZ()
        {
            List<Vector3> samples = Collinear(
                new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 1f), 0.5f, 8);

            Assert.IsTrue(WallFitting.TryFitWallLine(
                samples, out WallLine line, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.None, rejection);

            Assert.AreEqual(2f, line.Point.x, FitTolerance);
            Assert.AreEqual(1.75f, line.Point.z, FitTolerance);

            Assert.AreEqual(0f, line.Direction.x, FitTolerance);
            Assert.AreEqual(1f, Mathf.Abs(line.Direction.z), FitTolerance);

            Assert.AreEqual(0f, line.RmsResidualM, FitTolerance);
            Assert.AreEqual(3.5f, line.SpanM, FitTolerance);
        }

        [Test]
        public void Fit_RecoversWallAtArbitraryAngle()
        {
            var samples = new List<Vector3>();

            for (int i = 0; i < 8; i++)
            {
                samples.Add(new Vector3(i, 0f, i));
            }

            Assert.IsTrue(WallFitting.TryFitWallLine(
                samples, out WallLine line, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.None, rejection);

            Assert.AreEqual(3.5f, line.Point.x, FitTolerance);
            Assert.AreEqual(3.5f, line.Point.z, FitTolerance);

            // 45 degrees: both components equal in magnitude, and the direction
            // must lie along (1, 0, 1), not across it.
            Assert.AreEqual(
                Mathf.Abs(line.Direction.x),
                Mathf.Abs(line.Direction.z),
                FitTolerance);
            Assert.Greater(line.Direction.x * line.Direction.z, 0f);

            Assert.AreEqual(0f, line.RmsResidualM, FitTolerance);

            // (0,0,0) to (7,0,7) is 7 * sqrt(2).
            Assert.AreEqual(7f * Mathf.Sqrt(2f), line.SpanM, FitTolerance);
        }

        // -------------------------------------------------------------------
        // Residual
        // -------------------------------------------------------------------

        /// <summary>
        /// Perpendicular offsets of +/-0.02 m arranged so their correlation with
        /// position is exactly zero. The fitted direction therefore stays exactly
        /// along X, every sample sits exactly 0.02 m off the line, and the RMS
        /// residual must be exactly 0.02.
        /// </summary>
        [Test]
        public void Fit_ReportsRmsResidualForKnownScatter()
        {
            var signs = new[] { 1f, -1f, -1f, 1f, 1f, -1f, -1f, 1f };
            var samples = new List<Vector3>();

            for (int i = 0; i < 8; i++)
            {
                samples.Add(new Vector3(0.5f * i, 0f, 3f + (0.02f * signs[i])));
            }

            Assert.IsTrue(WallFitting.TryFitWallLine(
                samples, out WallLine line, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.None, rejection);

            Assert.AreEqual(1f, Mathf.Abs(line.Direction.x), FitTolerance);
            Assert.AreEqual(3f, line.Point.z, FitTolerance);
            Assert.AreEqual(0.02f, line.RmsResidualM, FitTolerance);
            Assert.AreEqual(3.5f, line.SpanM, FitTolerance);
        }

        [Test]
        public void Fit_ReportsZeroResidualForExactlyCollinearSamples()
        {
            List<Vector3> samples = Collinear(
                new Vector3(-1f, 0f, 0.5f), new Vector3(2f, 0f, -1f), 0.4f, 20);

            Assert.IsTrue(WallFitting.TryFitWallLine(
                samples, out WallLine line, out _));
            Assert.AreEqual(0f, line.RmsResidualM, FitTolerance);
        }

        // -------------------------------------------------------------------
        // Invariance
        // -------------------------------------------------------------------

        [Test]
        public void Fit_IsIndependentOfSampleOrder()
        {
            List<Vector3> forward = Collinear(
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 2f), 0.3f, 12);

            var reversed = new List<Vector3>(forward);
            reversed.Reverse();

            Assert.IsTrue(WallFitting.TryFitWallLine(forward, out WallLine a, out _));
            Assert.IsTrue(WallFitting.TryFitWallLine(reversed, out WallLine b, out _));

            Assert.AreEqual(a.Point.x, b.Point.x, FitTolerance);
            Assert.AreEqual(a.Point.z, b.Point.z, FitTolerance);
            Assert.AreEqual(a.SpanM, b.SpanM, FitTolerance);

            // Parallel, but possibly opposite in sign.
            float dot = Vector3.Dot(a.Direction, b.Direction);
            Assert.AreEqual(1f, Mathf.Abs(dot), FitTolerance);
        }

        [Test]
        public void Fit_IsTranslationInvariant()
        {
            List<Vector3> samples = Collinear(
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0.5f), 0.4f, 10);

            var offset = new Vector3(13.5f, 0f, -7.25f);
            var shifted = new List<Vector3>();

            for (int i = 0; i < samples.Count; i++)
            {
                shifted.Add(samples[i] + offset);
            }

            Assert.IsTrue(WallFitting.TryFitWallLine(samples, out WallLine a, out _));
            Assert.IsTrue(WallFitting.TryFitWallLine(shifted, out WallLine b, out _));

            Assert.AreEqual(a.Point.x + offset.x, b.Point.x, FitTolerance);
            Assert.AreEqual(a.Point.z + offset.z, b.Point.z, FitTolerance);
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(a.Direction, b.Direction)), FitTolerance);
            Assert.AreEqual(a.SpanM, b.SpanM, FitTolerance);
        }

        // -------------------------------------------------------------------
        // Fit rejections
        // -------------------------------------------------------------------

        [Test]
        public void Fit_RejectsTooFewSamples()
        {
            List<Vector3> samples = Collinear(
                Vector3.zero, new Vector3(1f, 0f, 0f), 0.5f, WallFitting.MinSampleCount - 1);

            Assert.IsFalse(WallFitting.TryFitWallLine(
                samples, out _, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.TooFewSamples, rejection);
        }

        [Test]
        public void Fit_RejectsNullSamples()
        {
            Assert.IsFalse(WallFitting.TryFitWallLine(
                null, out _, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.TooFewSamples, rejection);
        }

        [Test]
        public void Fit_RejectsCoincidentSamples()
        {
            var samples = new List<Vector3>();

            for (int i = 0; i < 16; i++)
            {
                samples.Add(new Vector3(1f, 0f, 2f));
            }

            Assert.IsFalse(WallFitting.TryFitWallLine(
                samples, out _, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.Degenerate, rejection);
        }

        /// <summary>
        /// A sweep that barely moved. The samples are perfectly collinear, so the
        /// residual looks excellent; only the span gate catches it.
        /// </summary>
        [Test]
        public void Fit_RejectsSpanShorterThanMinimum()
        {
            List<Vector3> samples = Collinear(
                new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), 0.03f, 8);

            Assert.IsFalse(WallFitting.TryFitWallLine(
                samples, out _, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.SpanTooShort, rejection);
        }

        [Test]
        public void Fit_AcceptsSpanAtMinimum()
        {
            // 8 samples, 0.06 m apart, spanning exactly 0.42 m.
            List<Vector3> samples = Collinear(
                new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), 0.06f, 8);

            Assert.Greater(0.42f, WallFitting.MinSpanM);
            Assert.IsTrue(WallFitting.TryFitWallLine(
                samples, out WallLine line, out _));
            Assert.AreEqual(0.42f, line.SpanM, FitTolerance);
        }

        [Test]
        public void Fit_RejectsNonFiniteSample()
        {
            List<Vector3> samples = Collinear(
                Vector3.zero, new Vector3(1f, 0f, 0f), 0.5f, 10);
            samples[4] = new Vector3(float.NaN, 0f, 0f);

            Assert.IsFalse(WallFitting.TryFitWallLine(
                samples, out _, out WallFitRejection rejection));
            Assert.AreEqual(WallFitRejection.NonFiniteSample, rejection);
        }

        [Test]
        public void Fit_IgnoresSampleY()
        {
            List<Vector3> flat = Collinear(
                new Vector3(0f, 0f, 2f), new Vector3(1f, 0f, 0f), 0.5f, 8);

            var lifted = new List<Vector3>();

            for (int i = 0; i < flat.Count; i++)
            {
                lifted.Add(new Vector3(flat[i].x, 0.37f * i, flat[i].z));
            }

            Assert.IsTrue(WallFitting.TryFitWallLine(flat, out WallLine a, out _));
            Assert.IsTrue(WallFitting.TryFitWallLine(lifted, out WallLine b, out _));

            Assert.AreEqual(a.Point.x, b.Point.x, FitTolerance);
            Assert.AreEqual(a.Point.z, b.Point.z, FitTolerance);
            Assert.AreEqual(a.SpanM, b.SpanM, FitTolerance);
            Assert.AreEqual(0f, b.Point.y, Tolerance);
        }

        // -------------------------------------------------------------------
        // Intersection
        // -------------------------------------------------------------------

        [Test]
        public void Intersect_RecoversKnownCrossing()
        {
            WallLine a = LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            WallLine b = LineThrough(new Vector3(4f, 0f, 0f), new Vector3(0f, 0f, 1f));

            Assert.IsTrue(WallFitting.TryIntersectWallLinesXZ(a, b, out Vector3 point));

            Assert.AreEqual(4f, point.x, Tolerance);
            Assert.AreEqual(0f, point.z, Tolerance);
            Assert.AreEqual(0f, point.y, Tolerance);
        }

        [Test]
        public void Intersect_RecoversCrossingAwayFromBothSamplePoints()
        {
            // Lines z = 3 and x = -2 cross at (-2, 0, 3), which is nowhere near
            // either defining point.
            WallLine a = LineThrough(new Vector3(10f, 0f, 3f), new Vector3(1f, 0f, 0f));
            WallLine b = LineThrough(new Vector3(-2f, 0f, -9f), new Vector3(0f, 0f, 1f));

            Assert.IsTrue(WallFitting.TryIntersectWallLinesXZ(a, b, out Vector3 point));

            Assert.AreEqual(-2f, point.x, Tolerance);
            Assert.AreEqual(3f, point.z, Tolerance);
        }

        [Test]
        public void Intersect_IsOrderIndependent()
        {
            WallLine a = LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0.4f));
            WallLine b = LineThrough(new Vector3(2f, 0f, 5f), new Vector3(-0.3f, 0f, 1f));

            Assert.IsTrue(WallFitting.TryIntersectWallLinesXZ(a, b, out Vector3 first));
            Assert.IsTrue(WallFitting.TryIntersectWallLinesXZ(b, a, out Vector3 second));

            Assert.AreEqual(first.x, second.x, FitTolerance);
            Assert.AreEqual(first.z, second.z, FitTolerance);
        }

        [Test]
        public void Intersect_RejectsParallelLines()
        {
            WallLine a = LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            WallLine b = LineThrough(new Vector3(0f, 0f, 3f), new Vector3(1f, 0f, 0f));

            Assert.IsFalse(WallFitting.TryIntersectWallLinesXZ(a, b, out _));
        }

        [Test]
        public void Intersect_RejectsAntiParallelLines()
        {
            WallLine a = LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            WallLine b = LineThrough(new Vector3(0f, 0f, 3f), new Vector3(-1f, 0f, 0f));

            Assert.IsFalse(WallFitting.TryIntersectWallLinesXZ(a, b, out _));
        }

        [Test]
        public void Intersect_RejectsCrossingShallowerThanMinimumAngle()
        {
            const float shallowDeg = 3f;

            WallLine a = LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            WallLine b = LineThrough(
                new Vector3(0f, 0f, 1f),
                new Vector3(
                    Mathf.Cos(shallowDeg * Mathf.Deg2Rad),
                    0f,
                    Mathf.Sin(shallowDeg * Mathf.Deg2Rad)));

            Assert.Less(shallowDeg, WallFitting.MinIntersectionAngleDeg);
            Assert.IsFalse(WallFitting.TryIntersectWallLinesXZ(a, b, out _));
        }

        [Test]
        public void Intersect_AcceptsCrossingAboveMinimumAngle()
        {
            const float openDeg = 10f;

            WallLine a = LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            WallLine b = LineThrough(
                new Vector3(0f, 0f, 1f),
                new Vector3(
                    Mathf.Cos(openDeg * Mathf.Deg2Rad),
                    0f,
                    Mathf.Sin(openDeg * Mathf.Deg2Rad)));

            Assert.Greater(openDeg, WallFitting.MinIntersectionAngleDeg);
            Assert.IsTrue(WallFitting.TryIntersectWallLinesXZ(a, b, out _));
        }

        // -------------------------------------------------------------------
        // Corner derivation
        // -------------------------------------------------------------------

        [Test]
        public void DeriveCorners_ReproducesRectangle()
        {
            List<WallLine> walls = RectangleWalls(4f, 3f);

            Assert.IsTrue(WallFitting.TryDeriveCorners(
                walls, out Vector3[] corners, out string error));
            Assert.IsEmpty(error);
            Assert.AreEqual(4, corners.Length);

            Assert.AreEqual(0f, corners[0].x, FitTolerance);
            Assert.AreEqual(0f, corners[0].z, FitTolerance);

            Assert.AreEqual(4f, corners[1].x, FitTolerance);
            Assert.AreEqual(0f, corners[1].z, FitTolerance);

            Assert.AreEqual(4f, corners[2].x, FitTolerance);
            Assert.AreEqual(3f, corners[2].z, FitTolerance);

            Assert.AreEqual(0f, corners[3].x, FitTolerance);
            Assert.AreEqual(3f, corners[3].z, FitTolerance);
        }

        [Test]
        public void DeriveCorners_ForcesCornerYToZero()
        {
            List<WallLine> walls = RectangleWalls(4f, 3f);

            Assert.IsTrue(WallFitting.TryDeriveCorners(walls, out Vector3[] corners, out _));

            for (int i = 0; i < corners.Length; i++)
            {
                Assert.AreEqual(0f, corners[i].y, Tolerance, $"corner {i}");
            }
        }

        /// <summary>
        /// A trapezoid: corners (0,0), (5,0), (4,3), (1,3). Nothing about the
        /// derivation assumes a rectangle.
        /// </summary>
        [Test]
        public void DeriveCorners_ReproducesNonRectangularQuadrilateral()
        {
            var walls = new List<WallLine>
            {
                LineThrough(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f)),
                LineThrough(new Vector3(5f, 0f, 0f), new Vector3(-1f, 0f, 3f)),
                LineThrough(new Vector3(4f, 0f, 3f), new Vector3(-1f, 0f, 0f)),
                LineThrough(new Vector3(1f, 0f, 3f), new Vector3(-1f, 0f, -3f))
            };

            Assert.IsTrue(WallFitting.TryDeriveCorners(
                walls, out Vector3[] corners, out string error));
            Assert.IsEmpty(error);

            Assert.AreEqual(0f, corners[0].x, FitTolerance);
            Assert.AreEqual(0f, corners[0].z, FitTolerance);

            Assert.AreEqual(5f, corners[1].x, FitTolerance);
            Assert.AreEqual(0f, corners[1].z, FitTolerance);

            Assert.AreEqual(4f, corners[2].x, FitTolerance);
            Assert.AreEqual(3f, corners[2].z, FitTolerance);

            Assert.AreEqual(1f, corners[3].x, FitTolerance);
            Assert.AreEqual(3f, corners[3].z, FitTolerance);
        }

        [Test]
        public void DeriveCorners_RejectsWrongWallCount()
        {
            List<WallLine> walls = RectangleWalls(4f, 3f);
            walls.RemoveAt(3);

            Assert.IsFalse(WallFitting.TryDeriveCorners(walls, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void DeriveCorners_RejectsNullWalls()
        {
            Assert.IsFalse(WallFitting.TryDeriveCorners(null, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void DeriveCorners_RejectsParallelNeighbors()
        {
            List<WallLine> walls = RectangleWalls(4f, 3f);

            // Make wall 1 parallel to wall 0, so corner 1 cannot be formed.
            walls[1] = LineThrough(new Vector3(4f, 0f, 0f), new Vector3(1f, 0f, 0f));

            Assert.IsFalse(WallFitting.TryDeriveCorners(walls, out _, out string error));
            Assert.IsNotEmpty(error);
        }

        // -------------------------------------------------------------------
        // The derived room agrees with the rest of the shared package
        // -------------------------------------------------------------------

        /// <summary>
        /// The ordering contract from ADR-0005: <c>wall[i]</c> spans
        /// <c>corner[i] -> corner[i + 1]</c>. Proved against
        /// <see cref="RoomGeometry.BuildWalls"/> rather than restated, so the two
        /// conventions cannot drift apart.
        /// </summary>
        [Test]
        public void DeriveCorners_OrderMatchesRoomGeometryWallDerivation()
        {
            List<WallLine> fitted = RectangleWalls(4f, 3f);

            Assert.IsTrue(WallFitting.TryDeriveCorners(fitted, out Vector3[] corners, out _));

            var room = new RoomModel
            {
                id = "room-1",
                name = "Swept",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = Vec3Dto.FromVector3(corners[0]) },
                    new CornerModel { id = "c1", position = Vec3Dto.FromVector3(corners[1]) },
                    new CornerModel { id = "c2", position = Vec3Dto.FromVector3(corners[2]) },
                    new CornerModel { id = "c3", position = Vec3Dto.FromVector3(corners[3]) }
                },
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };

            IReadOnlyList<WallDefinition> rebuilt = RoomGeometry.BuildWalls(room);
            Assert.AreEqual(4, rebuilt.Count);

            for (int i = 0; i < 4; i++)
            {
                // Wall i starts at corner i.
                Assert.AreEqual(corners[i].x, rebuilt[i].Start.x, FitTolerance, $"wall {i} start x");
                Assert.AreEqual(corners[i].z, rebuilt[i].Start.z, FitTolerance, $"wall {i} start z");

                // And runs along the line that was fitted for it.
                float alignment = Vector3.Dot(rebuilt[i].Tangent, fitted[i].Direction);
                Assert.AreEqual(1f, Mathf.Abs(alignment), FitTolerance, $"wall {i} direction");
            }
        }

        [Test]
        public void DeriveCorners_ProducesRoomTheSharedValidatorAccepts()
        {
            List<WallLine> walls = RectangleWalls(4f, 3f);

            Assert.IsTrue(WallFitting.TryDeriveCorners(walls, out Vector3[] corners, out _));

            ValidationResult result = RoomValidator.ValidateRoom(
                BuildRoom(corners, heightM: 2.5f));

            Assert.IsTrue(result.IsValid, result.Error);
        }

        /// <summary>
        /// Sweeping is not a licence to skip room rules. A 0.6 x 0.6 m footprint
        /// derives cleanly — four good fits, four good intersections — and is
        /// still refused, by the unchanged shared validator, for area.
        /// </summary>
        [Test]
        public void DeriveCorners_DoesNotBypassRoomValidation()
        {
            List<WallLine> walls = RectangleWalls(0.6f, 0.6f);

            Assert.IsTrue(WallFitting.TryDeriveCorners(walls, out Vector3[] corners, out _));

            ValidationResult result = RoomValidator.ValidateRoom(
                BuildRoom(corners, heightM: 2.5f));

            Assert.IsFalse(result.IsValid);
            Assert.IsNotEmpty(result.Error);
        }

        // -------------------------------------------------------------------
        // End to end: swept samples -> walls -> corners
        // -------------------------------------------------------------------

        /// <summary>
        /// The whole pipeline on noise-free samples, as four sweeps would
        /// produce them for a 4 x 3 m room.
        /// </summary>
        [Test]
        public void SweptSamples_ProduceTheExpectedRoom()
        {
            var sweeps = new List<List<Vector3>>
            {
                Collinear(new Vector3(0.2f, 0f, 0f), new Vector3(1f, 0f, 0f), 0.2f, 18),
                Collinear(new Vector3(4f, 0f, 0.2f), new Vector3(0f, 0f, 1f), 0.2f, 13),
                Collinear(new Vector3(0.2f, 0f, 3f), new Vector3(1f, 0f, 0f), 0.2f, 18),
                Collinear(new Vector3(0f, 0f, 0.2f), new Vector3(0f, 0f, 1f), 0.2f, 13)
            };

            var walls = new List<WallLine>();

            for (int i = 0; i < sweeps.Count; i++)
            {
                Assert.IsTrue(
                    WallFitting.TryFitWallLine(sweeps[i], out WallLine line, out _),
                    $"sweep {i}");
                walls.Add(line);
            }

            Assert.IsTrue(WallFitting.TryDeriveCorners(
                walls, out Vector3[] corners, out string error), error);

            Assert.AreEqual(0f, corners[0].x, FitTolerance);
            Assert.AreEqual(0f, corners[0].z, FitTolerance);
            Assert.AreEqual(4f, corners[1].x, FitTolerance);
            Assert.AreEqual(0f, corners[1].z, FitTolerance);
            Assert.AreEqual(4f, corners[2].x, FitTolerance);
            Assert.AreEqual(3f, corners[2].z, FitTolerance);
            Assert.AreEqual(0f, corners[3].x, FitTolerance);
            Assert.AreEqual(3f, corners[3].z, FitTolerance);

            ValidationResult result = RoomValidator.ValidateRoom(
                BuildRoom(corners, heightM: 2.5f));
            Assert.IsTrue(result.IsValid, result.Error);
        }

        /// <summary>
        /// A sweep only ever sees the visible part of a junction. Two walls
        /// covered over a short, off-center segment still derive the full room,
        /// because the fit extends the line and the corner is an intersection
        /// rather than an observation. This is the occluded-corner case that the
        /// walked-corner flow cannot capture at all.
        /// </summary>
        [Test]
        public void PartiallyOccludedSweeps_StillDeriveTheFullRoom()
        {
            var sweeps = new List<List<Vector3>>
            {
                // Only the middle 1.6 m of the 4 m wall is visible.
                Collinear(new Vector3(1.2f, 0f, 0f), new Vector3(1f, 0f, 0f), 0.2f, 9),
                Collinear(new Vector3(4f, 0f, 1.4f), new Vector3(0f, 0f, 1f), 0.2f, 9),
                Collinear(new Vector3(1.2f, 0f, 3f), new Vector3(1f, 0f, 0f), 0.2f, 9),
                Collinear(new Vector3(0f, 0f, 1.4f), new Vector3(0f, 0f, 1f), 0.2f, 9)
            };

            var walls = new List<WallLine>();

            for (int i = 0; i < sweeps.Count; i++)
            {
                Assert.IsTrue(
                    WallFitting.TryFitWallLine(sweeps[i], out WallLine line, out _),
                    $"sweep {i}");
                walls.Add(line);
            }

            Assert.IsTrue(WallFitting.TryDeriveCorners(walls, out Vector3[] corners, out _));

            Assert.AreEqual(0f, corners[0].x, FitTolerance);
            Assert.AreEqual(0f, corners[0].z, FitTolerance);
            Assert.AreEqual(4f, corners[2].x, FitTolerance);
            Assert.AreEqual(3f, corners[2].z, FitTolerance);
        }

        private static RoomModel BuildRoom(Vector3[] corners, float heightM)
        {
            var models = new CornerModel[corners.Length];

            for (int i = 0; i < corners.Length; i++)
            {
                models[i] = new CornerModel
                {
                    id = $"c{i}",
                    position = Vec3Dto.FromVector3(corners[i])
                };
            }

            return new RoomModel
            {
                id = "room-1",
                name = "Swept",
                heightM = heightM,
                corners = models,
                openings = new OpeningModel[0],
                objects = new SceneObjectModel[0]
            };
        }
    }
}
