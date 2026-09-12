using System.Collections.Generic;
using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Shared.Geometry
{
    /// <summary>
    /// Footprint math and wall derivation.
    ///
    /// Walls are derived here and nowhere else. Nothing may persist a wall: if
    /// walls were stored alongside corners the two could disagree, and this is
    /// the class that exists to make that impossible.
    /// </summary>
    public static class RoomGeometry
    {
        /// <summary>Collinearity guard for the self-intersection test.</summary>
        private const float CrossEpsilon = 1e-6f;

        /// <summary>
        /// Signed footprint area in XZ, by the shoelace formula. The sign
        /// reveals winding, which the viewer needs to orient floor triangles.
        /// </summary>
        public static float SignedPolygonAreaXZ(IReadOnlyList<Vector3> corners)
        {
            if (corners == null || corners.Count < 3)
            {
                return 0f;
            }

            float sum = 0f;
            int count = corners.Count;

            for (int i = 0; i < count; i++)
            {
                Vector3 a = corners[i];
                Vector3 b = corners[(i + 1) % count];

                sum += (a.x * b.z) - (b.x * a.z);
            }

            return sum * 0.5f;
        }

        /// <summary>
        /// Absolute footprint area in XZ, in square meters. Y is ignored.
        /// </summary>
        public static float PolygonAreaXZ(IReadOnlyList<Vector3> corners)
            => Mathf.Abs(SignedPolygonAreaXZ(corners));

        /// <summary>
        /// True when any two non-adjacent edges of the closed polygon cross.
        /// A bow-tie footprint is the classic bad four-corner scan.
        /// </summary>
        public static bool HasSelfIntersectionXZ(IReadOnlyList<Vector3> corners)
        {
            if (corners == null || corners.Count < 4)
            {
                return false;
            }

            int count = corners.Count;

            for (int i = 0; i < count; i++)
            {
                int iNext = (i + 1) % count;

                for (int j = i + 1; j < count; j++)
                {
                    int jNext = (j + 1) % count;

                    // Skip edges that share a vertex.
                    if (i == j || iNext == j || jNext == i)
                    {
                        continue;
                    }

                    if (SegmentsIntersectXZ(
                            corners[i], corners[iNext],
                            corners[j], corners[jNext]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Derives the walls of a room from its consecutive corners, closing the
        /// loop from the last corner back to the first.
        /// </summary>
        public static IReadOnlyList<WallDefinition> BuildWalls(RoomModel room)
        {
            var walls = new List<WallDefinition>();

            if (room?.corners == null || room.corners.Length < 2)
            {
                return walls;
            }

            int count = room.corners.Length;

            for (int i = 0; i < count; i++)
            {
                CornerModel start = room.corners[i];
                CornerModel end = room.corners[(i + 1) % count];

                if (start == null || end == null)
                {
                    continue;
                }

                walls.Add(new WallDefinition(
                    start.id,
                    end.id,
                    start.position.ToVector3(),
                    end.position.ToVector3()));
            }

            return walls;
        }

        /// <summary>
        /// Finds the wall spanning the given ordered corner pair.
        /// The pair is matched in order, because an opening's offset is measured
        /// from its declared start corner.
        /// </summary>
        public static bool TryFindWall(
            RoomModel room,
            string startCornerId,
            string endCornerId,
            out WallDefinition wall)
        {
            wall = default;

            IReadOnlyList<WallDefinition> walls = BuildWalls(room);

            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i].StartCornerId == startCornerId &&
                    walls[i].EndCornerId == endCornerId)
                {
                    wall = walls[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Interior angle at corner <paramref name="index"/>, in degrees.
        /// </summary>
        public static float InteriorAngleDeg(IReadOnlyList<Vector3> corners, int index)
        {
            int count = corners.Count;

            Vector3 current = corners[index];
            Vector3 previous = corners[(index - 1 + count) % count];
            Vector3 next = corners[(index + 1) % count];

            Vector3 a = new Vector3(previous.x - current.x, 0f, previous.z - current.z);
            Vector3 b = new Vector3(next.x - current.x, 0f, next.z - current.z);

            if (a.sqrMagnitude < Mathf.Epsilon || b.sqrMagnitude < Mathf.Epsilon)
            {
                return 0f;
            }

            return Vector3.Angle(a, b);
        }

        // -------------------------------------------------------------------
        // Segment intersection, XZ only.
        // -------------------------------------------------------------------

        private static bool SegmentsIntersectXZ(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2)
        {
            float d1 = CrossXZ(q2 - q1, p1 - q1);
            float d2 = CrossXZ(q2 - q1, p2 - q1);
            float d3 = CrossXZ(p2 - p1, q1 - p1);
            float d4 = CrossXZ(p2 - p1, q2 - p1);

            if (Opposite(d1, d2) && Opposite(d3, d4))
            {
                return true;
            }

            // Collinear overlap.
            if (IsZero(d1) && OnSegmentXZ(q1, q2, p1)) return true;
            if (IsZero(d2) && OnSegmentXZ(q1, q2, p2)) return true;
            if (IsZero(d3) && OnSegmentXZ(p1, p2, q1)) return true;
            if (IsZero(d4) && OnSegmentXZ(p1, p2, q2)) return true;

            return false;
        }

        private static float CrossXZ(Vector3 a, Vector3 b)
            => (a.x * b.z) - (a.z * b.x);

        private static bool IsZero(float value)
            => Mathf.Abs(value) < CrossEpsilon;

        private static bool Opposite(float a, float b)
            => (a > CrossEpsilon && b < -CrossEpsilon)
            || (a < -CrossEpsilon && b > CrossEpsilon);

        private static bool OnSegmentXZ(Vector3 a, Vector3 b, Vector3 point)
        {
            return point.x <= Mathf.Max(a.x, b.x) + CrossEpsilon
                && point.x >= Mathf.Min(a.x, b.x) - CrossEpsilon
                && point.z <= Mathf.Max(a.z, b.z) + CrossEpsilon
                && point.z >= Mathf.Min(a.z, b.z) - CrossEpsilon;
        }
    }
}
