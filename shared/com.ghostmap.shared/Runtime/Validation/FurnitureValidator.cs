using System.Collections.Generic;
using GhostMap.Shared.Domain;

namespace GhostMap.Shared.Validation
{
    /// <summary>
    /// Validates parametric furniture and supplies the MVP default dimensions.
    ///
    /// Defaults are starting points the user is expected to correct, not
    /// measurements. GhostMap does not infer furniture dimensions automatically
    /// in the MVP.
    /// </summary>
    public static class FurnitureValidator
    {
        public const float MinDimensionM = 0.05f;
        public const float MaxDimensionM = 5.0f;

        private static readonly string[] Types =
        {
            "bed",
            "desk",
            "chair",
            "couch",
            "table",
            "dresser",
            "tv",
            "generic"
        };

        /// <summary>width x depth x height, in meters.</summary>
        private static readonly Dictionary<string, (float W, float D, float H)> Defaults =
            new Dictionary<string, (float, float, float)>
            {
                { "bed",     (1.52f, 2.03f, 0.60f) },
                { "desk",    (1.40f, 0.70f, 0.75f) },
                { "chair",   (0.50f, 0.50f, 0.90f) },
                { "couch",   (2.10f, 0.90f, 0.85f) },
                { "table",   (1.50f, 0.90f, 0.75f) },
                { "dresser", (1.20f, 0.50f, 0.90f) },
                { "tv",      (1.10f, 0.10f, 0.70f) },
                { "generic", (1.00f, 1.00f, 1.00f) }
            };

        public static IReadOnlyList<string> SupportedTypes => Types;

        public static bool IsSupportedType(string type)
            => !string.IsNullOrEmpty(type) && Defaults.ContainsKey(type);

        public static bool TryGetDefaultDimensions(
            string type,
            out float widthM,
            out float depthM,
            out float heightM)
        {
            if (!string.IsNullOrEmpty(type) &&
                Defaults.TryGetValue(type, out (float W, float D, float H) value))
            {
                widthM = value.W;
                depthM = value.D;
                heightM = value.H;
                return true;
            }

            widthM = 0f;
            depthM = 0f;
            heightM = 0f;
            return false;
        }

        public static ValidationResult Validate(SceneObjectModel model)
        {
            if (model == null)
            {
                return ValidationResult.Invalid("Object is null.");
            }

            if (!IsSupportedType(model.type))
            {
                return ValidationResult.Invalid(
                    $"Object type '{model.type}' is not supported.");
            }

            if (!RoomValidator.IsFinite(model.center.ToVector3()))
            {
                return ValidationResult.Invalid("Object center must be finite.");
            }

            if (!RoomValidator.IsFinite(model.yawDeg))
            {
                return ValidationResult.Invalid("Object yaw must be finite.");
            }

            ValidationResult width = ValidateDimension(model.widthM, "width");
            if (!width.IsValid)
            {
                return width;
            }

            ValidationResult depth = ValidateDimension(model.depthM, "depth");
            if (!depth.IsValid)
            {
                return depth;
            }

            ValidationResult height = ValidateDimension(model.heightM, "height");
            if (!height.IsValid)
            {
                return height;
            }

            return ValidationResult.Valid();
        }

        private static ValidationResult ValidateDimension(float value, string label)
        {
            if (!RoomValidator.IsFinite(value))
            {
                return ValidationResult.Invalid($"Object {label} must be finite.");
            }

            if (value <= 0f)
            {
                return ValidationResult.Invalid(
                    $"Object {label} must be positive; got {value:F2} m.");
            }

            if (value < MinDimensionM)
            {
                return ValidationResult.Invalid(
                    $"Object {label} {value:F2} m is below the minimum {MinDimensionM:F2} m.");
            }

            if (value > MaxDimensionM)
            {
                return ValidationResult.Invalid(
                    $"Object {label} {value:F2} m is above the maximum {MaxDimensionM:F1} m.");
            }

            return ValidationResult.Valid();
        }
    }
}
