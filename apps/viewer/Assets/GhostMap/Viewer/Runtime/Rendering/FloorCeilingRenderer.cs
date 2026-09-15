using System.Collections.Generic;
using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Task V2: turns a room's footprint into a floor mesh at y = 0 and a
    /// ceiling mesh at y = <see cref="RoomModel.heightM"/>.
    ///
    /// Triangulates by a fan from corner 0, which is only correct for a
    /// convex polygon. <c>RoomValidator</c>'s interior-angle rule (35-145
    /// degrees at every corner, enforced once all four MVP corners exist)
    /// makes every closed room convex by construction, so this does not
    /// re-check convexity itself; a partial (fewer than four corner) chain is
    /// always trivially convex.
    /// </summary>
    public static class FloorCeilingRenderer
    {
        public static bool TryBuildFloorMesh(RoomModel room, out Mesh mesh)
            => TryBuildFootprintMesh(room, yLevel: 0f, desiredNormal: Vector3.up, meshName: "FloorMesh", out mesh);

        public static bool TryBuildCeilingMesh(RoomModel room, out Mesh mesh)
        {
            if (room == null || room.heightM <= 0f)
            {
                mesh = null;
                return false;
            }

            return TryBuildFootprintMesh(room, room.heightM, Vector3.down, "CeilingMesh", out mesh);
        }

        private static bool TryBuildFootprintMesh(
            RoomModel room,
            float yLevel,
            Vector3 desiredNormal,
            string meshName,
            out Mesh mesh)
        {
            if (room?.corners == null || room.corners.Length < 3)
            {
                mesh = null;
                return false;
            }

            int count = room.corners.Length;
            var vertices = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 position = room.corners[i].position.ToVector3();
                vertices[i] = new Vector3(position.x, yLevel, position.z);
            }

            var triangles = new List<int>((count - 2) * 3);

            for (int i = 1; i < count - 1; i++)
            {
                AddFanTriangle(triangles, vertices, 0, i, i + 1, desiredNormal);
            }

            mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return true;
        }

        /// <summary>
        /// Adds one fan triangle, choosing whichever of the two winding
        /// orders makes its geometric normal agree with
        /// <paramref name="desiredNormal"/>. Only the index order changes —
        /// vertex positions are never negated — so a "wrong winding" input
        /// can never come out as a mirrored footprint.
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
