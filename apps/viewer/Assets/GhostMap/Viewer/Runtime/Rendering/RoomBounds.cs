using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Task V4: the axis-aligned world bounds of the room shell, used by the
    /// orbit camera to frame whatever room it is actually given.
    ///
    /// Deliberately computed from the room's own corners and captured height
    /// rather than from a hard-coded fixture footprint, so a small room, a
    /// large room and a room rotated off the world axes all frame correctly.
    /// Furniture is not included: objects live inside the shell, and framing
    /// on the shell keeps the home view stable as furniture is added during
    /// the Scanner's AddObjects phase.
    /// </summary>
    public static class RoomBounds
    {
        public static bool TryCompute(RoomModel room, out Bounds bounds)
        {
            bounds = default;

            if (room?.corners == null || room.corners.Length == 0)
            {
                return false;
            }

            bool any = false;

            for (int i = 0; i < room.corners.Length; i++)
            {
                CornerModel corner = room.corners[i];

                if (corner == null)
                {
                    continue;
                }

                Vector3 position = corner.position.ToVector3();

                // A single non-finite corner must not poison the whole framing.
                if (!IsFinite(position))
                {
                    continue;
                }

                var floorPoint = new Vector3(position.x, 0f, position.z);

                if (!any)
                {
                    bounds = new Bounds(floorPoint, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(floorPoint);
                }
            }

            if (!any)
            {
                return false;
            }

            // Height is 0 until S4 captures it; a partial scan still frames.
            if (IsFinite(room.heightM) && room.heightM > 0f)
            {
                bounds.Encapsulate(new Vector3(bounds.center.x, room.heightM, bounds.center.z));
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
