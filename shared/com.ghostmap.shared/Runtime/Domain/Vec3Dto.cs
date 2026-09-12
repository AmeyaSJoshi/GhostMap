using System;
using UnityEngine;

namespace GhostMap.Shared.Domain
{
    /// <summary>
    /// Serializable 3D vector in GhostMap coordinates.
    ///
    /// All distances are meters. +Y is up, the floor is Y = 0 after
    /// normalization, +Z is forward from the floor-lock moment and +X is right
    /// from the floor-lock moment.
    ///
    /// This exists instead of <see cref="Vector3"/> because the wire format is
    /// produced by JsonUtility, which serializes public fields of concrete
    /// [Serializable] types. Keeping the DTO separate from the engine type also
    /// keeps the schema stable if Unity ever changes Vector3's layout.
    /// </summary>
    [Serializable]
    public struct Vec3Dto
    {
        public float x;
        public float y;
        public float z;

        public Vec3Dto(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3 ToVector3() => new Vector3(x, y, z);

        public static Vec3Dto FromVector3(Vector3 value)
            => new Vec3Dto(value.x, value.y, value.z);

        public override string ToString()
            => $"({x:F3}, {y:F3}, {z:F3})";
    }
}
