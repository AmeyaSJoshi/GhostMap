using System.Collections.Generic;
using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Shared.Export
{
    /// <summary>
    /// The floor (y = 0) and ceiling (y = <see cref="RoomModel.heightM"/>) of a
    /// room as plain vertex and triangle lists in Ghost space (Unity axes).
    ///
    /// Triangulates by a fan from corner 0, which is only correct for a convex
    /// polygon. <c>RoomValidator</c>'s interior-angle rule (35-145 degrees at
    /// every corner) makes every closed room convex, so convexity is not
    /// re-checked here. Moved from the Viewer's <c>FloorCeilingRenderer</c> in
    /// task R6 so the phone's room exporter and the renderer share it.
    /// </summary>
    public static class FootprintMesh
    {
        public static bool TryBuildFloor(RoomModel room, out Vector3[] vertices, out int[] triangles)
            => TryBuild(room, 0f, Vector3.up, out vertices, out triangles);

        public static bool TryBuildCeiling(RoomModel room, out Vector3[] vertices, out int[] triangles)
        {
            if (room == null || room.heightM <= 0f)
            {
                vertices = null;
                triangles = null;
                return false;
            }

            return TryBuild(room, room.heightM, Vector3.down, out vertices, out triangles);
        }

        /// <summary>
        /// Builds the footprint at <paramref name="yLevel"/>, wound so each
        /// triangle's Unity front face (<c>Cross(b - a, c - a)</c>) points along
        /// <paramref name="desiredNormal"/>.
        /// </summary>
        public static bool TryBuild(
            RoomModel room,
            float yLevel,
            Vector3 desiredNormal,
            out Vector3[] vertices,
            out int[] triangles)
        {
            if (room?.corners == null || room.corners.Length < 3)
            {
                vertices = null;
                triangles = null;
                return false;
            }

            int count = room.corners.Length;
            vertices = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 position = room.corners[i].position.ToVector3();
                vertices[i] = new Vector3(position.x, yLevel, position.z);
            }

            var list = new List<int>((count - 2) * 3);

            for (int i = 1; i < count - 1; i++)
            {
                AddFanTriangle(list, vertices, 0, i, i + 1, desiredNormal);
            }

            triangles = list.ToArray();
            return true;
        }

        /// <summary>
        /// Adds one fan triangle, choosing whichever of the two winding orders
        /// makes its geometric normal agree with <paramref name="desiredNormal"/>.
        /// Only the index order changes, never the vertex positions, so a
        /// "wrong winding" input can never come out as a mirrored footprint.
        /// </summary>
        private static void AddFanTriangle(
            List<int> triangles,
            Vector3[] vertices,
            int i0,
            int i1,
            int i2,
            Vector3 desiredNormal)
        {
            Vector3 normal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);

            if (Vector3.Dot(normal, desiredNormal) < 0f)
            {
                (i1, i2) = (i2, i1);
            }

            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
        }
    }
}
