using GhostMap.Shared.Domain;
using GhostMap.Shared.Export;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// Task V2: turns a room's footprint into a floor mesh at y = 0 and a
    /// ceiling mesh at y = <see cref="RoomModel.heightM"/>. The triangulation
    /// is the shared <see cref="FootprintMesh"/> (moved there in task R6 so
    /// the phone's room exporter uses the same one); this wraps it in a
    /// <see cref="Mesh"/>.
    /// </summary>
    public static class FloorCeilingRenderer
    {
        public static bool TryBuildFloorMesh(RoomModel room, out Mesh mesh)
        {
            mesh = FootprintMesh.TryBuildFloor(room, out Vector3[] vertices, out int[] triangles)
                ? ToMesh(vertices, triangles, "FloorMesh")
                : null;
            return mesh != null;
        }

        public static bool TryBuildCeilingMesh(RoomModel room, out Mesh mesh)
        {
            mesh = FootprintMesh.TryBuildCeiling(room, out Vector3[] vertices, out int[] triangles)
                ? ToMesh(vertices, triangles, "CeilingMesh")
                : null;
            return mesh != null;
        }

        private static Mesh ToMesh(Vector3[] vertices, int[] triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
