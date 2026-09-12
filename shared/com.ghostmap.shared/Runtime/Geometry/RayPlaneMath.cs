using UnityEngine;

namespace GhostMap.Shared.Geometry
{
    /// <summary>
    /// Ray/plane intersection. This is the arithmetic that stands in for depth
    /// sensing on a non-LiDAR iPhone: the user aims, and GhostMap intersects the
    /// camera ray with a plane it already knows.
    ///
    /// Every method rejects rather than extrapolates. A grazing or parallel ray
    /// produces a wildly distant point, which would silently corrupt a scan.
    /// </summary>
    public static class RayPlaneMath
    {
        /// <summary>
        /// Below this denominator the ray is treated as parallel to the plane.
        /// </summary>
        public const float ParallelEpsilon = 1e-4f;

        /// <summary>
        /// Intersects a ray with a horizontal plane at <paramref name="planeY"/>.
        /// Used for corner capture and furniture placement.
        /// </summary>
        /// <returns>
        /// False if the ray is parallel to the plane, or if the intersection lies
        /// behind the ray origin.
        /// </returns>
        public static bool TryIntersectHorizontalPlane(
            Ray ray,
            float planeY,
            out Vector3 point)
        {
            point = Vector3.zero;

            float denom = ray.direction.y;

            if (Mathf.Abs(denom) < ParallelEpsilon)
            {
                return false;
            }

            float t = (planeY - ray.origin.y) / denom;

            if (t <= 0f)
            {
                return false;
            }

            point = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>
        /// Intersects a ray with an arbitrary plane. Used for room height and
        /// opening capture against derived wall planes.
        ///
        /// The sign of the plane normal is irrelevant.
        /// </summary>
        public static bool TryIntersectPlane(
            Ray ray,
            Plane plane,
            out Vector3 point)
        {
            point = Vector3.zero;

            float denom = Vector3.Dot(plane.normal, ray.direction);

            if (Mathf.Abs(denom) < ParallelEpsilon)
            {
                return false;
            }

            if (!plane.Raycast(ray, out float distance) || distance <= 0f)
            {
                return false;
            }

            point = ray.GetPoint(distance);
            return true;
        }
    }
}
