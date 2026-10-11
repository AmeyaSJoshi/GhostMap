using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostMap.Shared.Geometry
{
    /// <summary>Why a wall line fit was refused. <see cref="None"/> means it succeeded.</summary>
    public enum WallFitRejection
    {
        None = 0,

        /// <summary>Fewer than <see cref="WallFitting.MinSampleCount"/> samples.</summary>
        TooFewSamples,

        /// <summary>
        /// The samples span less than <see cref="WallFitting.MinSpanM"/> along the
        /// fitted direction. A short span cannot determine a wall's angle.
        /// </summary>
        SpanTooShort,

        /// <summary>
        /// The samples have no dominant direction — they are coincident, or
        /// scattered isotropically. There is no line to fit.
        /// </summary>
        Degenerate,

        /// <summary>A sample was non-finite, so the scatter matrix is meaningless.</summary>
        NonFiniteSample
    }

    /// <summary>
    /// One fitted wall, as an infinite line in the Ghost XZ plane, plus the
    /// quality facts the fit itself reports.
    ///
    /// This is a capture-time intermediate, not a serialized type. Walls are
    /// still derived and never persisted (see <see cref="RoomGeometry"/>); this
    /// struct exists only long enough to intersect it with its neighbors and
    /// produce corners.
    /// </summary>
    public readonly struct WallLine
    {
        public WallLine(
            Vector3 point,
            Vector3 direction,
            float rmsResidualM,
            float spanM,
            int sampleCount)
        {
            Point = point;
            Direction = direction;
            RmsResidualM = rmsResidualM;
            SpanM = spanM;
            SampleCount = sampleCount;
        }

        /// <summary>
        /// A point on the line: the centroid of the samples, in Ghost space with
        /// <c>y = 0</c>.
        /// </summary>
        public Vector3 Point { get; }

        /// <summary>
        /// Unit direction along the wall, in Ghost space with <c>y = 0</c>.
        ///
        /// The sign is arbitrary — a line has no inherent orientation, and the
        /// eigenvector that produces this could legitimately come back either
        /// way. Nothing downstream may depend on it: corner derivation
        /// intersects lines, which is sign-independent, and the room's winding
        /// comes from sweep order rather than from these directions.
        /// </summary>
        public Vector3 Direction { get; }

        /// <summary>
        /// Root-mean-square perpendicular distance from the samples to the
        /// fitted line, in meters. The fit's own statement of how well the
        /// samples actually lay on a straight line.
        ///
        /// <para><b>This cannot detect a systematic aim bias.</b> A user who
        /// consistently aims slightly above the floor junction produces samples
        /// that are tightly collinear — low residual — but displaced outward.
        /// See <c>ADR-0005</c>.</para>
        /// </summary>
        public float RmsResidualM { get; }

        /// <summary>
        /// Extent of the samples along <see cref="Direction"/>, in meters: how
        /// much of the wall the sweep actually covered. A long span pins the
        /// angle down; a short one leaves it poorly determined however tidy the
        /// residual looks.
        /// </summary>
        public float SpanM { get; }

        public int SampleCount { get; }
    }

    /// <summary>
    /// Fits a wall to samples swept along its floor junction, and derives room
    /// corners by intersecting consecutive walls.
    ///
    /// <para>This is the capture math for <c>ADR-0005</c>. The user stands,
    /// turns, and sweeps the center-screen ray along each wall's floor
    /// junction; <see cref="RayPlaneMath.TryIntersectHorizontalPlane"/> turns
    /// each frame into a floor-plane point, and this class turns a run of those
    /// points into a wall.</para>
    ///
    /// <para><b>Total least squares, not ordinary least squares.</b> A wall runs
    /// in an arbitrary direction, including parallel to Ghost <c>Z</c>, where
    /// fitting <c>z = mx + c</c> is singular and a wall that happens to be
    /// axis-aligned would fail. Minimizing perpendicular distance instead is
    /// orientation-free, which is the only correctness-preserving choice
    /// here.</para>
    ///
    /// <para><b>Corners are derived, not captured.</b> That inverts the S3 flow,
    /// where walls derived from captured corners. The serialized contract is
    /// unchanged either way: a room is still exactly four ordered Ghost-space
    /// floor corners, and walls are still never persisted.</para>
    ///
    /// <para>Every method rejects rather than extrapolates, matching
    /// <see cref="RayPlaneMath"/>: a degenerate fit or a near-parallel
    /// intersection produces a wildly wrong room, and a wildly wrong room must
    /// never be silently rendered.</para>
    /// </summary>
    public static class WallFitting
    {
        /// <summary>
        /// Exactly four, per scene schema v1. A sweep produces one wall each.
        /// </summary>
        public const int RequiredWallCount = 4;

        /// <summary>
        /// Minimum samples for a fit. Two points define a line, but two points
        /// also define it perfectly, which makes the residual uninformative and
        /// leaves a single bad frame able to set the wall's angle. Eight gives
        /// the residual something to say.
        /// </summary>
        public const int MinSampleCount = 8;

        /// <summary>
        /// Minimum swept extent along the wall, in meters. Below this the angle
        /// is badly conditioned: tiny perpendicular noise over a short baseline
        /// rotates the line a long way, and the error shows up at the far
        /// corner rather than in the residual.
        ///
        /// Deliberately below <c>RoomValidator.MinWallLengthM</c> (0.50 m),
        /// because a sweep only sees the *visible* part of a junction — a
        /// wardrobe can legitimately hide most of a long wall.
        /// </summary>
        public const float MinSpanM = 0.40f;

        /// <summary>
        /// Below this the largest eigenvalue of the scatter matrix is treated as
        /// zero: the samples are effectively one point.
        /// </summary>
        public const float DegenerateScatterEpsilon = 1e-8f;

        /// <summary>
        /// Minimum angle between two walls for their intersection to be usable,
        /// in degrees. Two nearly parallel lines cross at a point that moves
        /// enormously under tiny changes in either fit.
        ///
        /// This is a conditioning guard, not a room rule. The real limit is
        /// <c>RoomValidator</c>'s 35-145 degree interior-angle range, which runs
        /// on the derived room afterwards.
        /// </summary>
        public const float MinIntersectionAngleDeg = 5f;

        /// <summary>
        /// Fits a wall line to floor-junction samples by total least squares.
        ///
        /// Samples are expected in Ghost space. <c>y</c> is ignored — the floor
        /// is <c>y = 0</c> by construction and the fit is purely XZ.
        /// </summary>
        /// <returns>False when the samples cannot produce a meaningful line.</returns>
        public static bool TryFitWallLine(
            IReadOnlyList<Vector3> samples,
            out WallLine line,
            out WallFitRejection rejection)
        {
            line = default;

            if (samples == null || samples.Count < MinSampleCount)
            {
                rejection = WallFitRejection.TooFewSamples;
                return false;
            }

            int count = samples.Count;

            // Accumulate in double. The scatter matrix sums squares of
            // meter-scale values over a sweep that can run to hundreds of
            // samples, and the smallest eigenvalue — the one the residual comes
            // from — is a difference of similar large numbers. In float that
            // difference loses most of its significant digits.
            double sumX = 0d;
            double sumZ = 0d;

            for (int i = 0; i < count; i++)
            {
                Vector3 sample = samples[i];

                if (float.IsNaN(sample.x) || float.IsInfinity(sample.x) ||
                    float.IsNaN(sample.z) || float.IsInfinity(sample.z))
                {
                    rejection = WallFitRejection.NonFiniteSample;
                    return false;
                }

                sumX += sample.x;
                sumZ += sample.z;
            }

            double centroidX = sumX / count;
            double centroidZ = sumZ / count;

            double sxx = 0d;
            double szz = 0d;
            double sxz = 0d;

            for (int i = 0; i < count; i++)
            {
                double dx = samples[i].x - centroidX;
                double dz = samples[i].z - centroidZ;

                sxx += dx * dx;
                szz += dz * dz;
                sxz += dx * dz;
            }

            // Eigenvalues of the symmetric 2x2 scatter matrix [[sxx, sxz], [sxz, szz]].
            // Because the matrix is unnormalized, the eigenvalues are sums of
            // squared projections: the larger onto the wall direction, the
            // smaller onto its perpendicular. That makes the smaller one
            // exactly the residual sum of squares.
            double trace = sxx + szz;
            double delta = sxx - szz;
            double discriminant = Math.Sqrt((delta * delta) + (4d * sxz * sxz));

            double eigenMax = 0.5d * (trace + discriminant);
            double eigenMin = 0.5d * (trace - discriminant);

            // Rounding can push a perfectly collinear fit a hair below zero.
            if (eigenMin < 0d)
            {
                eigenMin = 0d;
            }

            if (eigenMax <= DegenerateScatterEpsilon)
            {
                rejection = WallFitRejection.Degenerate;
                return false;
            }

            // Eigenvector for eigenMax. Both rows of (A - eigenMax * I) give it,
            // but either row can vanish — the first does for a wall parallel to
            // X, the second for a wall parallel to Z — so take whichever is
            // better conditioned rather than assuming one is safe.
            double candidate1X = sxz;
            double candidate1Z = eigenMax - sxx;
            double candidate2X = eigenMax - szz;
            double candidate2Z = sxz;

            double magnitude1 = (candidate1X * candidate1X) + (candidate1Z * candidate1Z);
            double magnitude2 = (candidate2X * candidate2X) + (candidate2Z * candidate2Z);

            double dirX;
            double dirZ;

            if (magnitude1 >= magnitude2)
            {
                dirX = candidate1X;
                dirZ = candidate1Z;
            }
            else
            {
                dirX = candidate2X;
                dirZ = candidate2Z;
            }

            double length = Math.Sqrt((dirX * dirX) + (dirZ * dirZ));

            // Both rows vanishing means the scatter is isotropic: sxz is zero and
            // sxx equals szz. A circular blob of samples has no wall direction.
            if (length <= DegenerateScatterEpsilon)
            {
                rejection = WallFitRejection.Degenerate;
                return false;
            }

            dirX /= length;
            dirZ /= length;

            // Swept extent along the fitted direction.
            double minProjection = double.MaxValue;
            double maxProjection = double.MinValue;

            for (int i = 0; i < count; i++)
            {
                double dx = samples[i].x - centroidX;
                double dz = samples[i].z - centroidZ;

                double projection = (dx * dirX) + (dz * dirZ);

                if (projection < minProjection)
                {
                    minProjection = projection;
                }

                if (projection > maxProjection)
                {
                    maxProjection = projection;
                }
            }

            float spanM = (float)(maxProjection - minProjection);

            if (spanM < MinSpanM)
            {
                rejection = WallFitRejection.SpanTooShort;
                return false;
            }

            float rmsResidualM = (float)Math.Sqrt(eigenMin / count);

            line = new WallLine(
                new Vector3((float)centroidX, 0f, (float)centroidZ),
                new Vector3((float)dirX, 0f, (float)dirZ),
                rmsResidualM,
                spanM,
                count);

            rejection = WallFitRejection.None;
            return true;
        }

        /// <summary>
        /// Intersects two fitted wall lines in XZ.
        ///
        /// Rejects a crossing too shallow to locate reliably — see
        /// <see cref="MinIntersectionAngleDeg"/>.
        /// </summary>
        public static bool TryIntersectWallLinesXZ(
            WallLine a,
            WallLine b,
            out Vector3 point)
        {
            point = Vector3.zero;

            Vector3 dirA = a.Direction;
            Vector3 dirB = b.Direction;

            // For unit directions this cross product is the sine of the angle
            // between the lines, so the angle gate is a direct comparison.
            float denominator = (dirA.x * dirB.z) - (dirA.z * dirB.x);

            float minSine = Mathf.Sin(MinIntersectionAngleDeg * Mathf.Deg2Rad);

            if (Mathf.Abs(denominator) < minSine)
            {
                return false;
            }

            Vector3 offset = b.Point - a.Point;

            float t = ((offset.x * dirB.z) - (offset.z * dirB.x)) / denominator;

            Vector3 result = a.Point + (dirA * t);

            if (float.IsNaN(result.x) || float.IsInfinity(result.x) ||
                float.IsNaN(result.z) || float.IsInfinity(result.z))
            {
                return false;
            }

            // The floor is y = 0 by construction, and corner y is forced to 0
            // by scene schema v1. Never let fitting arithmetic introduce drift.
            point = new Vector3(result.x, 0f, result.z);
            return true;
        }

        /// <summary>
        /// Derives room corners from four wall lines swept in order around the
        /// room.
        ///
        /// <para><c>corner[i]</c> is the intersection of <c>wall[i - 1]</c> and
        /// <c>wall[i]</c>, so that <c>wall[i]</c> spans
        /// <c>corner[i] -> corner[i + 1]</c>. That is exactly the convention
        /// <see cref="RoomGeometry.BuildWalls"/> uses, so a room built from
        /// these corners re-derives the same walls that produced them.</para>
        ///
        /// <para>Winding follows sweep order. This does not validate the
        /// resulting room — <c>RoomValidator.ValidateRoom</c> is the only
        /// authority on whether a footprint is legal, and the caller must run
        /// it.</para>
        /// </summary>
        /// <returns>False with a user-facing reason in <paramref name="error"/>.</returns>
        public static bool TryDeriveCorners(
            IReadOnlyList<WallLine> walls,
            out Vector3[] corners,
            out string error)
        {
            corners = null;

            if (walls == null || walls.Count != RequiredWallCount)
            {
                error = $"Need exactly {RequiredWallCount} swept walls, got {walls?.Count ?? 0}.";
                return false;
            }

            var derived = new Vector3[RequiredWallCount];

            for (int i = 0; i < RequiredWallCount; i++)
            {
                WallLine previous = walls[(i - 1 + RequiredWallCount) % RequiredWallCount];
                WallLine current = walls[i];

                if (!TryIntersectWallLinesXZ(previous, current, out Vector3 corner))
                {
                    error =
                        $"Walls {(i - 1 + RequiredWallCount) % RequiredWallCount + 1} and {i + 1} " +
                        "are too close to parallel to form a corner. Re-sweep one of them.";
                    return false;
                }

                derived[i] = corner;
            }

            corners = derived;
            error = string.Empty;
            return true;
        }
    }
}
