using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Shared.Geometry
{
    /// <summary>
    /// Wall planes and wall-local coordinates.
    ///
    /// Wall planes are generated mathematically from captured corners. GhostMap
    /// never waits for AR to detect a real wall, which is what makes height,
    /// door and window capture reliable on a non-LiDAR device.
    ///
    /// Wall-local coordinates:
    /// <code>
    /// u = distance along the wall from the start corner
    /// v = height above the floor
    /// </code>
    /// </summary>
    public static class WallGeometry
    {
        /// <summary>
        /// Horizontal normal of the wall. The sign is irrelevant for ray
        /// intersection.
        /// </summary>
        public static Vector3 Normal(WallDefinition wall)
            => Vector3.Cross(Vector3.up, wall.Tangent).normalized;

        /// <summary>The infinite vertical plane containing the wall.</summary>
        public static Plane PlaneFor(WallDefinition wall)
            => new Plane(Normal(wall), wall.Start);

        /// <summary>
        /// Converts a Ghost-space point into wall-local <c>u</c> (along the wall
        /// from the start corner) and <c>v</c> (height above the floor).
        /// </summary>
        public static void ToWallLocal(
            WallDefinition wall,
            Vector3 ghostPoint,
            out float u,
            out float v)
        {
            Vector3 delta = ghostPoint - wall.Start;

            u = Vector3.Dot(delta, wall.Tangent);
            v = ghostPoint.y;
        }

        /// <summary>Converts wall-local coordinates back into a Ghost-space point.</summary>
        public static Vector3 FromWallLocal(WallDefinition wall, float u, float v)
        {
            Vector3 basePoint = wall.Start + wall.Tangent * u;
            basePoint.y = v;
            return basePoint;
        }

        /// <summary>
        /// True when the wall-local span lies completely inside the wall.
        /// </summary>
        public static bool ContainsSpan(WallDefinition wall, float offsetM, float widthM)
            => offsetM >= 0f && (offsetM + widthM) <= wall.LengthM;
    }
}
