using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using UnityEngine;

namespace GhostMap.Shared.Validation
{
    /// <summary>Closure-error bands from implementation plan section 9.2.</summary>
    public enum ClosureQuality
    {
        Excellent,
        Acceptable,
        Rejected
    }

    /// <summary>
    /// Deterministic room validation. GhostMap rejects bad scans rather than
    /// rendering them, so every rule here produces a reason the UI can show.
    ///
    /// <see cref="ValidateRoom"/> validates a room <em>as far as it has been
    /// captured</em>. Live snapshots arrive mid-scan with zero corners, a partial
    /// corner chain, or an uncaptured height, and none of those are errors.
    /// Structural rules apply only once the relevant data exists.
    /// </summary>
    public static class RoomValidator
    {
        public const int RequiredCornerCount = 4;

        public const float MinCornerSpacingM = 0.50f;
        public const float MinNonNeighborSpacingM = 0.20f;
        public const float CornerFloorToleranceM = 0.05f;

        public const float MinWallLengthM = 0.50f;
        public const float MaxWallLengthM = 20.0f;

        public const float MinRoomAreaM2 = 2.0f;

        public const float MinRoomHeightM = 2.0f;
        public const float MaxRoomHeightM = 4.0f;

        public const float MinInteriorAngleDeg = 35f;
        public const float MaxInteriorAngleDeg = 145f;

        public const float ClosureExcellentM = 0.08f;
        public const float ClosureAcceptableM = 0.15f;

        // -------------------------------------------------------------------
        // Closure
        // -------------------------------------------------------------------

        /// <summary>
        /// Classifies closure error. Boundaries are inclusive: exactly 0.08 m is
        /// Excellent and exactly 0.15 m is Acceptable.
        /// </summary>
        public static ClosureQuality ClassifyClosure(float closureErrorM)
        {
            if (closureErrorM <= ClosureExcellentM)
            {
                return ClosureQuality.Excellent;
            }

            if (closureErrorM <= ClosureAcceptableM)
            {
                return ClosureQuality.Acceptable;
            }

            return ClosureQuality.Rejected;
        }

        // -------------------------------------------------------------------
        // Incremental corner capture
        // -------------------------------------------------------------------

        /// <summary>
        /// Validates a candidate corner about to be appended.
        ///
        /// Checks spacing against the previous corner, separation from
        /// non-neighbors, and — when the candidate closes the polygon — that the
        /// result is not self-intersecting. Area and interior angles are left to
        /// <see cref="ValidateRoom"/>.
        /// </summary>
        public static ValidationResult ValidateNewCorner(
            IReadOnlyList<CornerModel> existing,
            Vector3 candidate)
        {
            if (!IsFinite(candidate))
            {
                return ValidationResult.Invalid("Corner position must be finite.");
            }

            if (Mathf.Abs(candidate.y) > CornerFloorToleranceM)
            {
                return ValidationResult.Invalid(
                    $"Corner must lie on the floor plane (Y within {CornerFloorToleranceM:F2} m of 0).");
            }

            int count = existing?.Count ?? 0;

            if (count >= RequiredCornerCount)
            {
                return ValidationResult.Invalid(
                    $"The room already has all four corners.");
            }

            if (count == 0)
            {
                return ValidationResult.Valid();
            }

            // Spacing from the immediately previous corner.
            Vector3 previous = existing[count - 1].position.ToVector3();

            float toPrevious = MeasurementMath.DistanceXZ(previous, candidate);

            if (toPrevious < MinCornerSpacingM)
            {
                return ValidationResult.Invalid(
                    $"Corner is {toPrevious:F2} m from the previous corner; " +
                    $"minimum spacing is {MinCornerSpacingM:F2} m.");
            }

            bool closesPolygon = count == RequiredCornerCount - 1;

            // The closing corner also forms a wall back to the first corner.
            if (closesPolygon)
            {
                Vector3 first = existing[0].position.ToVector3();
                float toFirst = MeasurementMath.DistanceXZ(first, candidate);

                if (toFirst < MinCornerSpacingM)
                {
                    return ValidationResult.Invalid(
                        $"Corner is {toFirst:F2} m from the first corner; " +
                        $"the closing wall needs at least {MinCornerSpacingM:F2} m.");
                }
            }

            // Separation from every non-neighbor corner.
            for (int i = 0; i < count; i++)
            {
                bool isPrevious = i == count - 1;
                bool isFirstAndClosing = closesPolygon && i == 0;

                if (isPrevious || isFirstAndClosing)
                {
                    continue;
                }

                Vector3 other = existing[i].position.ToVector3();
                float distance = MeasurementMath.DistanceXZ(other, candidate);

                if (distance < MinNonNeighborSpacingM)
                {
                    return ValidationResult.Invalid(
                        $"Corner is {distance:F2} m from corner {existing[i].id}; " +
                        $"minimum separation is {MinNonNeighborSpacingM:F2} m.");
                }
            }

            if (closesPolygon)
            {
                var points = new List<Vector3>();
                for (int i = 0; i < count; i++)
                {
                    points.Add(existing[i].position.ToVector3());
                }
                points.Add(candidate);

                if (RoomGeometry.HasSelfIntersectionXZ(points))
                {
                    return ValidationResult.Invalid(
                        "These corners cross over each other. Redo the corners.");
                }
            }

            return ValidationResult.Valid();
        }

        // -------------------------------------------------------------------
        // Whole-room validation
        // -------------------------------------------------------------------

        public static ValidationResult ValidateRoom(RoomModel room)
        {
            if (room == null)
            {
                return ValidationResult.Invalid("Room is null.");
            }

            if (room.corners == null)
            {
                return ValidationResult.Invalid("Room corners array is null.");
            }

            // Height, when captured, must be plausible.
            if (!IsFinite(room.heightM))
            {
                return ValidationResult.Invalid("Room height must be finite.");
            }

            if (room.heightM != 0f &&
                (room.heightM < MinRoomHeightM || room.heightM > MaxRoomHeightM))
            {
                return ValidationResult.Invalid(
                    $"Room height {room.heightM:F2} m is outside the supported " +
                    $"range {MinRoomHeightM:F1}-{MaxRoomHeightM:F1} m.");
            }

            int count = room.corners.Length;

            // No corners yet: the normal state right after floor lock.
            if (count == 0)
            {
                return ValidationResult.Valid();
            }

            var ids = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                CornerModel corner = room.corners[i];

                if (corner == null)
                {
                    return ValidationResult.Invalid($"Corner {i} is null.");
                }

                if (string.IsNullOrEmpty(corner.id))
                {
                    return ValidationResult.Invalid($"Corner {i} has no id.");
                }

                if (!ids.Add(corner.id))
                {
                    return ValidationResult.Invalid(
                        $"Duplicate corner id '{corner.id}'. Corner ids must be unique.");
                }

                Vector3 position = corner.position.ToVector3();

                if (!IsFinite(position))
                {
                    return ValidationResult.Invalid($"Corner {corner.id} position must be finite.");
                }

                if (Mathf.Abs(position.y) > CornerFloorToleranceM)
                {
                    return ValidationResult.Invalid(
                        $"Corner {corner.id} is not on the floor plane.");
                }
            }

            // Partial capture in progress: validate only what exists.
            if (count < RequiredCornerCount)
            {
                return ValidatePartialChain(room.corners);
            }

            if (count > RequiredCornerCount)
            {
                return ValidationResult.Invalid(
                    $"MVP supports exactly {RequiredCornerCount} corners; found {count}.");
            }

            List<Vector3> points = MeasurementMath.ToPoints(room.corners);

            if (RoomGeometry.HasSelfIntersectionXZ(points))
            {
                return ValidationResult.Invalid(
                    "Room footprint is self-intersecting.");
            }

            float area = RoomGeometry.PolygonAreaXZ(points);

            if (area < MinRoomAreaM2)
            {
                return ValidationResult.Invalid(
                    $"Room area {area:F2} m² is below the minimum {MinRoomAreaM2:F1} m².");
            }

            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(room);

            for (int i = 0; i < walls.Count; i++)
            {
                float length = walls[i].LengthM;

                if (length < MinWallLengthM)
                {
                    return ValidationResult.Invalid(
                        $"Wall {walls[i].StartCornerId}->{walls[i].EndCornerId} is " +
                        $"{length:F2} m, below the minimum {MinWallLengthM:F2} m.");
                }

                if (length > MaxWallLengthM)
                {
                    return ValidationResult.Invalid(
                        $"Wall {walls[i].StartCornerId}->{walls[i].EndCornerId} is " +
                        $"{length:F2} m, above the maximum {MaxWallLengthM:F1} m.");
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                float angle = RoomGeometry.InteriorAngleDeg(points, i);

                if (angle < MinInteriorAngleDeg || angle > MaxInteriorAngleDeg)
                {
                    return ValidationResult.Invalid(
                        $"Interior angle at corner {room.corners[i].id} is {angle:F1}°, " +
                        $"outside the supported range " +
                        $"{MinInteriorAngleDeg:F0}°-{MaxInteriorAngleDeg:F0}°.");
                }
            }

            return ValidationResult.Valid();
        }

        private static ValidationResult ValidatePartialChain(CornerModel[] corners)
        {
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 a = corners[i - 1].position.ToVector3();
                Vector3 b = corners[i].position.ToVector3();

                float length = MeasurementMath.DistanceXZ(a, b);

                if (length < MinCornerSpacingM)
                {
                    return ValidationResult.Invalid(
                        $"Corners {corners[i - 1].id} and {corners[i].id} are only " +
                        $"{length:F2} m apart.");
                }

                if (length > MaxWallLengthM)
                {
                    return ValidationResult.Invalid(
                        $"Wall {corners[i - 1].id}->{corners[i].id} is {length:F2} m, " +
                        $"above the maximum {MaxWallLengthM:F1} m.");
                }
            }

            return ValidationResult.Valid();
        }

        internal static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
