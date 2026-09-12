using System.Collections.Generic;
using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Shared.Geometry
{
    /// <summary>
    /// Deterministic measurement over the structured scene.
    ///
    /// Because the scene is semantic rather than a mesh, these are exact
    /// arithmetic over captured values, not estimates from geometry.
    /// </summary>
    public static class MeasurementMath
    {
        /// <summary>Straight-line distance, including any vertical component.</summary>
        public static float Distance(Vector3 a, Vector3 b)
            => (b - a).magnitude;

        /// <summary>
        /// Horizontal distance, ignoring height. This is usually what a user
        /// means by "how far apart are these".
        /// </summary>
        public static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>Floor area in square meters.</summary>
        public static float RoomAreaM2(RoomModel room)
        {
            if (room?.corners == null)
            {
                return 0f;
            }

            return RoomGeometry.PolygonAreaXZ(ToPoints(room.corners));
        }

        /// <summary>Enclosed volume in cubic meters.</summary>
        public static float RoomVolumeM3(RoomModel room)
        {
            if (room == null)
            {
                return 0f;
            }

            return RoomAreaM2(room) * room.heightM;
        }

        /// <summary>Total wall length around the footprint, in meters.</summary>
        public static float RoomPerimeterM(RoomModel room)
        {
            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(room);

            float total = 0f;

            for (int i = 0; i < walls.Count; i++)
            {
                total += walls[i].LengthM;
            }

            return total;
        }

        internal static List<Vector3> ToPoints(CornerModel[] corners)
        {
            var points = new List<Vector3>(corners.Length);

            for (int i = 0; i < corners.Length; i++)
            {
                if (corners[i] != null)
                {
                    points.Add(corners[i].position.ToVector3());
                }
            }

            return points;
        }
    }
}
