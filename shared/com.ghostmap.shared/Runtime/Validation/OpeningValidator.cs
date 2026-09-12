using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using UnityEngine;

namespace GhostMap.Shared.Validation
{
    /// <summary>
    /// Validates doors and windows against the wall they claim to belong to.
    ///
    /// An opening is rejected before it can mutate the scene, so the viewer
    /// never has to render a doorway hanging outside its wall or punched through
    /// the ceiling.
    /// </summary>
    public static class OpeningValidator
    {
        public const string TypeDoor = "door";
        public const string TypeWindow = "window";

        public const float MinWidthM = 0.30f;
        public const float MaxWidthM = 4.0f;
        public const float MinHeightM = 0.30f;

        /// <summary>Tolerance for floating-point comparisons on wall extents.</summary>
        private const float Epsilon = 1e-4f;

        public static ValidationResult Validate(OpeningModel opening, RoomModel room)
        {
            if (opening == null)
            {
                return ValidationResult.Invalid("Opening is null.");
            }

            if (room == null)
            {
                return ValidationResult.Invalid("Room is null.");
            }

            if (opening.type != TypeDoor && opening.type != TypeWindow)
            {
                return ValidationResult.Invalid(
                    $"Opening type '{opening.type}' is not supported. " +
                    $"Expected '{TypeDoor}' or '{TypeWindow}'.");
            }

            if (!RoomValidator.IsFinite(opening.offsetM) ||
                !RoomValidator.IsFinite(opening.widthM) ||
                !RoomValidator.IsFinite(opening.sillHeightM) ||
                !RoomValidator.IsFinite(opening.heightM))
            {
                return ValidationResult.Invalid("Opening dimensions must be finite.");
            }

            if (room.heightM <= 0f)
            {
                return ValidationResult.Invalid(
                    "Room height has not been captured yet, so openings cannot be validated.");
            }

            // Width bounds.
            if (opening.widthM < MinWidthM)
            {
                return ValidationResult.Invalid(
                    $"Opening width {opening.widthM:F2} m is below the minimum {MinWidthM:F2} m.");
            }

            if (opening.widthM > MaxWidthM)
            {
                return ValidationResult.Invalid(
                    $"Opening width {opening.widthM:F2} m is above the maximum {MaxWidthM:F1} m.");
            }

            // Vertical extent.
            if (opening.heightM < MinHeightM)
            {
                return ValidationResult.Invalid(
                    $"Opening height {opening.heightM:F2} m is below the minimum {MinHeightM:F2} m.");
            }

            if (opening.sillHeightM < 0f)
            {
                return ValidationResult.Invalid(
                    $"Opening sill height {opening.sillHeightM:F2} m cannot be negative.");
            }

            float top = opening.sillHeightM + opening.heightM;

            if (top > room.heightM + Epsilon)
            {
                return ValidationResult.Invalid(
                    $"Opening top is {top:F2} m but room height is {room.heightM:F2} m.");
            }

            // Wall lookup and containment.
            if (!RoomGeometry.TryFindWall(
                    room, opening.wallStartCornerId, opening.wallEndCornerId, out WallDefinition wall))
            {
                return ValidationResult.Invalid(
                    $"No wall runs from corner '{opening.wallStartCornerId}' to " +
                    $"'{opening.wallEndCornerId}'.");
            }

            if (opening.offsetM < -Epsilon)
            {
                return ValidationResult.Invalid(
                    $"Opening offset {opening.offsetM:F2} m cannot be negative.");
            }

            if (opening.offsetM + opening.widthM > wall.LengthM + Epsilon)
            {
                return ValidationResult.Invalid(
                    $"Opening spans {opening.offsetM:F2}-{opening.offsetM + opening.widthM:F2} m " +
                    $"but the wall is only {wall.LengthM:F2} m long.");
            }

            // Overlap with other openings on the same wall.
            if (room.openings != null)
            {
                float start = opening.offsetM;
                float end = opening.offsetM + opening.widthM;

                for (int i = 0; i < room.openings.Length; i++)
                {
                    OpeningModel other = room.openings[i];

                    if (other == null || ReferenceEquals(other, opening))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(other.id) && other.id == opening.id)
                    {
                        continue;
                    }

                    if (other.wallStartCornerId != opening.wallStartCornerId ||
                        other.wallEndCornerId != opening.wallEndCornerId)
                    {
                        continue;
                    }

                    float otherStart = other.offsetM;
                    float otherEnd = other.offsetM + other.widthM;

                    bool overlaps = start < otherEnd - Epsilon
                                 && otherStart < end - Epsilon;

                    if (overlaps)
                    {
                        return ValidationResult.Invalid(
                            $"Opening overlaps existing opening '{other.id}' on the same wall.");
                    }
                }
            }

            return ValidationResult.Valid();
        }
    }
}
