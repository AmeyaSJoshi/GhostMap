using System;

namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// One piece of parametric furniture.
    ///
    /// MVP furniture is axis-aligned to its own yaw around +Y; there is no
    /// pitch or roll. Dimensions are stored as width x depth x height in meters,
    /// and <c>center</c> is the object's center on the floor plane.
    ///
    /// Supported <c>type</c> values: bed, desk, chair, couch, table, dresser,
    /// tv, generic.
    /// </summary>
    [Serializable]
    public sealed class SceneObjectModel
    {
        public string id;

        /// <summary>bed, desk, chair, couch, table, dresser, tv, or generic.</summary>
        public string type;

        /// <summary>
        /// Optional (task R5, additive within schema v1): what the on-device
        /// detector called the object, e.g. "potted plant" or "dining table".
        /// Empty or null when it was placed by hand. At most
        /// <c>FurnitureValidator.MaxLabelLength</c> characters, no control
        /// characters. A name only: it never affects geometry.
        /// </summary>
        public string label;

        public Vec3Dto center;

        /// <summary>Rotation about +Y, in degrees.</summary>
        public float yawDeg;

        public float widthM;
        public float depthM;
        public float heightM;
    }
}
