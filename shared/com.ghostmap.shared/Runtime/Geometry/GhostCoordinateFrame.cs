using UnityEngine;

namespace GhostMap.Shared.Geometry
{
    /// <summary>
    /// The GhostMap coordinate frame, established once when the user locks the
    /// floor and used for every capture afterwards.
    ///
    /// Construction at floor-lock:
    /// <code>
    /// up      = Vector3.up
    /// forward = ProjectOnPlane(camera.forward, up).normalized
    /// right   = Cross(up, forward).normalized
    /// origin  = floor raycast hit
    /// </code>
    ///
    /// Mapping: origin becomes (0,0,0), right becomes +X, up becomes +Y and
    /// forward becomes +Z. The floor therefore sits at Y = 0, which is what lets
    /// corner capture reduce to a ray/plane intersection.
    ///
    /// This type is immutable. A reset produces a new frame, never a mutated one.
    /// </summary>
    public sealed class GhostCoordinateFrame
    {
        private readonly Vector3 _origin;
        private readonly Vector3 _right;
        private readonly Vector3 _up;
        private readonly Vector3 _forward;

        /// <summary>
        /// Axes are normalized on construction. Passing non-unit axes would
        /// otherwise scale every converted distance.
        /// </summary>
        public GhostCoordinateFrame(
            Vector3 worldOrigin,
            Vector3 worldRight,
            Vector3 worldUp,
            Vector3 worldForward)
        {
            _origin = worldOrigin;
            _right = worldRight.normalized;
            _up = worldUp.normalized;
            _forward = worldForward.normalized;
        }

        public Vector3 Origin => _origin;
        public Vector3 Right => _right;
        public Vector3 Up => _up;
        public Vector3 Forward => _forward;

        /// <summary>The AR-world Y of the locked floor plane.</summary>
        public float FloorWorldY => _origin.y;

        public Vector3 WorldToGhost(Vector3 world)
        {
            Vector3 delta = world - _origin;

            return new Vector3(
                Vector3.Dot(delta, _right),
                Vector3.Dot(delta, _up),
                Vector3.Dot(delta, _forward));
        }

        public Vector3 GhostToWorld(Vector3 ghost)
        {
            return _origin
                + _right * ghost.x
                + _up * ghost.y
                + _forward * ghost.z;
        }

        /// <summary>Rotates a direction into Ghost space, ignoring the origin.</summary>
        public Vector3 WorldDirectionToGhost(Vector3 direction)
        {
            return new Vector3(
                Vector3.Dot(direction, _right),
                Vector3.Dot(direction, _up),
                Vector3.Dot(direction, _forward));
        }

        /// <summary>Rotates a direction into world space, ignoring the origin.</summary>
        public Vector3 GhostDirectionToWorld(Vector3 direction)
        {
            return _right * direction.x
                + _up * direction.y
                + _forward * direction.z;
        }

        /// <summary>
        /// Converts an AR-world camera ray into Ghost space. Height, door and
        /// window capture use this before intersecting a derived wall plane.
        /// </summary>
        public Ray WorldRayToGhost(Ray worldRay)
        {
            return new Ray(
                WorldToGhost(worldRay.origin),
                WorldDirectionToGhost(worldRay.direction));
        }

        public Ray GhostRayToWorld(Ray ghostRay)
        {
            return new Ray(
                GhostToWorld(ghostRay.origin),
                GhostDirectionToWorld(ghostRay.direction));
        }
    }
}
