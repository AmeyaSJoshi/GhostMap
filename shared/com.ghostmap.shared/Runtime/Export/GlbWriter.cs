using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GhostMap.Shared.Domain;
using UnityEngine;

namespace GhostMap.Shared.Export
{
    /// <summary>
    /// The colours of the room shell, shared by the Viewer's renderer and the
    /// exported <c>room.glb</c> so the two match.
    /// </summary>
    public static class RoomPalette
    {
        public static readonly Color Floor = new Color(0.55f, 0.45f, 0.35f);
        public static readonly Color Ceiling = new Color(0.85f, 0.85f, 0.85f);
        public static readonly Color Wall = new Color(0.75f, 0.72f, 0.65f);
    }

    /// <summary>
    /// Writes GhostMap geometry as binary glTF 2.0 (<c>.glb</c>): one file per
    /// furniture object (ADR-0006) and one for the room shell (ADR-0013,
    /// task R6). Contract: <c>docs/contracts/export-bundle-v1.md</c>.
    ///
    /// <para><b>Assets are generated from the measurements, never authored or
    /// imported.</b> Furniture comes from <see cref="FurnitureParts"/>, walls
    /// from <see cref="WallLayout"/> and the floor and ceiling from
    /// <see cref="FootprintMesh"/>: the same pure functions the Viewer renders
    /// with, so an exported file and the screen cannot disagree.</para>
    ///
    /// <para><b>Axes.</b> Input is Ghost space (Unity axes: left-handed, +Y up,
    /// metres). glTF is right-handed with +Y up, so every position and normal
    /// has its Z negated. A mirror turns faces inside out, so boxes are built
    /// directly in glTF space with an explicit winding check, and triangle
    /// lists have their winding reversed.</para>
    ///
    /// <para>Written by hand: the container is a 12-byte header and two
    /// chunks, and the geometry is boxes and flat polygons. Pure C#: returns
    /// bytes, touches no filesystem. Moved from the Viewer's
    /// <c>GlbExporter</c> in task R6.</para>
    /// </summary>
    public static class GlbWriter
    {
        /// <summary>Node name of the ceiling in <c>room.glb</c> (export bundle v1, section 3.1).</summary>
        public const string CeilingNodeName = "ceiling";

        public const string FloorNodeName = "floor";
        public const string WallsNodeName = "walls";

        /// <summary>`glTF` in ASCII, little-endian.</summary>
        private const uint Magic = 0x46546C67;

        /// <summary>`JSON` in ASCII, little-endian.</summary>
        private const uint ChunkTypeJson = 0x4E4F534A;

        /// <summary>`BIN\0` in ASCII, little-endian.</summary>
        private const uint ChunkTypeBin = 0x004E4942;

        private const uint GltfVersion = 2;

        /// <summary>glTF `FLOAT`.</summary>
        private const int ComponentTypeFloat = 5126;

        /// <summary>glTF `UNSIGNED_SHORT`.</summary>
        private const int ComponentTypeUnsignedShort = 5123;

        /// <summary>glTF bufferView targets: vertex attributes and indices.</summary>
        private const int TargetArrayBuffer = 34962;
        private const int TargetElementArrayBuffer = 34963;

        private const int VerticesPerBox = 24;
        private const int IndicesPerBox = 36;

        /// <summary>
        /// A box has 24 vertices, so this is the most boxes one mesh can index
        /// with unsigned shorts. Furniture uses a handful; a wall a few dozen.
        /// </summary>
        public const int MaxBoxes = 65535 / VerticesPerBox;

        private const string Generator = "GhostMap glTF exporter (ADR-0006, ADR-0013)";

        // -------------------------------------------------------------------
        // Furniture
        // -------------------------------------------------------------------

        /// <summary>
        /// Exports one furniture object as <c>.glb</c> bytes.
        ///
        /// <para>The model is emitted in its own local space, sitting on
        /// <c>y = 0</c> and centred on the origin. Its node carries <b>no
        /// transform</b>: position and yaw are in <c>scene.json</c>, and
        /// whoever places the object (the browser viewer, a Unity user) reads
        /// them from there. That keeps each asset reusable in any scene.</para>
        /// </summary>
        /// <returns>False when the model cannot be built; <paramref name="error"/> says why.</returns>
        public static bool TryExportObject(SceneObjectModel model, out byte[] glb, out string error)
        {
            glb = null;

            if (model == null)
            {
                error = "No object to export.";
                return false;
            }

            IReadOnlyList<FurniturePartSpec> parts = FurnitureParts.BuildParts(model);

            if (parts == null || parts.Count == 0)
            {
                error =
                    $"Object '{model.id}' of type '{model.type}' produced no geometry, " +
                    "so there is nothing to export.";
                return false;
            }

            if (parts.Count > MaxBoxes)
            {
                error = $"Object has {parts.Count} parts; the exporter supports {MaxBoxes}.";
                return false;
            }

            var mesh = new MeshBuffers(parts.Count * VerticesPerBox, parts.Count * IndicesPerBox);

            for (int i = 0; i < parts.Count; i++)
            {
                mesh.AddBox(parts[i].LocalCenter, parts[i].LocalSize);
            }

            string name = string.IsNullOrEmpty(model.type) ? "object" : model.type;

            var nodes = new List<NodeSpec>
            {
                new NodeSpec($"{name}_{Shorten(model.id)}", mesh, FurnitureParts.ColorFor(model.type), false)
            };

            glb = Build(nodes, new[] { 0 });
            error = string.Empty;
            return true;
        }

        // -------------------------------------------------------------------
        // Room
        // -------------------------------------------------------------------

        /// <summary>
        /// Exports the room shell as <c>.glb</c> bytes, in room position:
        /// a <c>floor</c> node, a <c>ceiling</c> node, and a <c>walls</c> node
        /// whose children <c>wall_0</c>, <c>wall_1</c>, ... hold each wall's
        /// segments with its doors and windows cut out as real holes.
        /// </summary>
        /// <returns>False when the room has no usable footprint or height.</returns>
        public static bool TryExportRoom(RoomModel room, out byte[] glb, out string error)
        {
            glb = null;

            if (room == null)
            {
                error = "No room to export.";
                return false;
            }

            if (!(room.heightM > 0f))
            {
                error = "The room has no height, so it has no walls or ceiling to export.";
                return false;
            }

            if (!FootprintMesh.TryBuildFloor(room, out Vector3[] floorVertices, out int[] floorTriangles) ||
                !FootprintMesh.TryBuildCeiling(room, out Vector3[] ceilingVertices, out int[] ceilingTriangles))
            {
                error = "The room has fewer than three corners, so it has no floor to export.";
                return false;
            }

            IReadOnlyList<WallRenderSpec> walls = WallLayout.BuildWalls(room);

            if (walls.Count == 0)
            {
                error = "The room produced no walls.";
                return false;
            }

            var floor = new MeshBuffers(floorTriangles.Length, floorTriangles.Length);
            floor.AddTriangles(floorVertices, floorTriangles);

            var ceiling = new MeshBuffers(ceilingTriangles.Length, ceilingTriangles.Length);
            ceiling.AddTriangles(ceilingVertices, ceilingTriangles);

            var nodes = new List<NodeSpec>
            {
                new NodeSpec(FloorNodeName, floor, RoomPalette.Floor, true),
                new NodeSpec(CeilingNodeName, ceiling, RoomPalette.Ceiling, true),
                new NodeSpec(WallsNodeName, null, default, false)
            };

            int wallsIndex = nodes.Count - 1;

            for (int w = 0; w < walls.Count; w++)
            {
                IReadOnlyList<WallSegmentSpec> segments = walls[w].Segments;

                if (segments.Count == 0)
                {
                    continue;
                }

                if (segments.Count > MaxBoxes)
                {
                    error = $"Wall {w} has {segments.Count} segments; the exporter supports {MaxBoxes}.";
                    return false;
                }

                var mesh = new MeshBuffers(segments.Count * VerticesPerBox, segments.Count * IndicesPerBox);

                for (int s = 0; s < segments.Count; s++)
                {
                    WallSegmentSpec segment = segments[s];
                    mesh.AddOrientedBox(
                        segment.Position,
                        segment.Rotation * Vector3.right,
                        segment.Rotation * Vector3.up,
                        segment.Rotation * Vector3.forward,
                        segment.Scale);
                }

                nodes[wallsIndex].Children.Add(nodes.Count);
                nodes.Add(new NodeSpec($"wall_{w}", mesh, RoomPalette.Wall, false));
            }

            glb = Build(nodes, new[] { 0, 1, wallsIndex });
            error = string.Empty;
            return true;
        }

        // -------------------------------------------------------------------
        // Container
        // -------------------------------------------------------------------

        private sealed class NodeSpec
        {
            public NodeSpec(string name, MeshBuffers mesh, Color color, bool doubleSided)
            {
                Name = name;
                Mesh = mesh;
                Color = color;
                DoubleSided = doubleSided;
            }

            public string Name { get; }
            public MeshBuffers Mesh { get; }
            public Color Color { get; }
            public bool DoubleSided { get; }
            public List<int> Children { get; } = new List<int>();
        }

        /// <summary>
        /// Writes the nodes, one mesh and one material per node that has
        /// geometry, into one binary buffer. Each mesh gets three accessors in
        /// order: POSITION, NORMAL, indices.
        /// </summary>
        private static byte[] Build(List<NodeSpec> nodes, IReadOnlyList<int> sceneRoots)
        {
            var meshNodes = new List<NodeSpec>();
            var bufferViewOffsets = new List<int>();
            var bufferViewLengths = new List<int>();
            int binLength = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                MeshBuffers mesh = nodes[i].Mesh;

                if (mesh == null)
                {
                    continue;
                }

                meshNodes.Add(nodes[i]);

                // Positions and normals are 12-byte strides and each view
                // starts 4-aligned, so every accessor's offset is a multiple of
                // its component size, as glTF requires.
                AddView(mesh.VertexCount * 3 * sizeof(float));
                AddView(mesh.VertexCount * 3 * sizeof(float));
                AddView(mesh.IndexCount * sizeof(ushort));
            }

            void AddView(int length)
            {
                bufferViewOffsets.Add(binLength);
                bufferViewLengths.Add(length);
                binLength += length + Padding(length);
            }

            string json = BuildJson(nodes, sceneRoots, meshNodes, bufferViewOffsets, bufferViewLengths, binLength);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            // The JSON chunk pads with spaces and the binary chunk with zeros,
            // both to a 4-byte boundary. Required, not cosmetic: a reader finds
            // the next chunk from these lengths.
            int jsonPadding = Padding(jsonBytes.Length);
            int jsonChunkLength = jsonBytes.Length + jsonPadding;
            int binChunkLength = binLength; // every view is already padded

            int total = 12 + 8 + jsonChunkLength + 8 + binChunkLength;

            var buffer = new byte[total];
            int cursor = 0;

            WriteUInt(buffer, ref cursor, Magic);
            WriteUInt(buffer, ref cursor, GltfVersion);
            WriteUInt(buffer, ref cursor, (uint)total);

            WriteUInt(buffer, ref cursor, (uint)jsonChunkLength);
            WriteUInt(buffer, ref cursor, ChunkTypeJson);
            Buffer.BlockCopy(jsonBytes, 0, buffer, cursor, jsonBytes.Length);
            cursor += jsonBytes.Length;

            for (int i = 0; i < jsonPadding; i++)
            {
                buffer[cursor++] = 0x20;
            }

            WriteUInt(buffer, ref cursor, (uint)binChunkLength);
            WriteUInt(buffer, ref cursor, ChunkTypeBin);

            int binStart = cursor;
            int view = 0;

            for (int m = 0; m < meshNodes.Count; m++)
            {
                MeshBuffers mesh = meshNodes[m].Mesh;

                cursor = binStart + bufferViewOffsets[view++];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    WriteVector(buffer, ref cursor, mesh.Positions[i]);
                }

                cursor = binStart + bufferViewOffsets[view++];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    WriteVector(buffer, ref cursor, mesh.Normals[i]);
                }

                cursor = binStart + bufferViewOffsets[view++];
                for (int i = 0; i < mesh.IndexCount; i++)
                {
                    WriteUShort(buffer, ref cursor, mesh.Indices[i]);
                }
            }

            // Padding bytes are already zero from the array allocation.
            return buffer;
        }

        private static string BuildJson(
            List<NodeSpec> nodes,
            IReadOnlyList<int> sceneRoots,
            List<NodeSpec> meshNodes,
            List<int> viewOffsets,
            List<int> viewLengths,
            int binLength)
        {
            var builder = new StringBuilder(1536);

            builder.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":").Append(Quote(Generator)).Append('}');

            builder.Append(",\"scene\":0,\"scenes\":[{\"nodes\":[");
            for (int i = 0; i < sceneRoots.Count; i++)
            {
                builder.Append(i == 0 ? "" : ",").Append(sceneRoots[i]);
            }
            builder.Append("]}]");

            builder.Append(",\"nodes\":[");
            int meshIndex = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeSpec node = nodes[i];
                builder.Append(i == 0 ? "{" : ",{");

                if (node.Mesh != null)
                {
                    builder.Append("\"mesh\":").Append(meshIndex++).Append(',');
                }

                builder.Append("\"name\":").Append(Quote(node.Name));

                if (node.Children.Count > 0)
                {
                    builder.Append(",\"children\":[");
                    for (int c = 0; c < node.Children.Count; c++)
                    {
                        builder.Append(c == 0 ? "" : ",").Append(node.Children[c]);
                    }
                    builder.Append(']');
                }

                builder.Append('}');
            }
            builder.Append(']');

            builder.Append(",\"meshes\":[");
            for (int m = 0; m < meshNodes.Count; m++)
            {
                int a = m * 3;
                builder.Append(m == 0 ? "" : ",");
                builder.Append("{\"name\":").Append(Quote(meshNodes[m].Name));
                builder.Append(",\"primitives\":[{\"attributes\":{\"POSITION\":").Append(a)
                    .Append(",\"NORMAL\":").Append(a + 1).Append('}');
                builder.Append(",\"indices\":").Append(a + 2).Append(",\"material\":").Append(m).Append("}]}");
            }
            builder.Append(']');

            builder.Append(",\"materials\":[");
            for (int m = 0; m < meshNodes.Count; m++)
            {
                Color color = meshNodes[m].Color;
                builder.Append(m == 0 ? "" : ",");
                builder.Append("{\"name\":").Append(Quote(meshNodes[m].Name + "_material"));
                builder.Append(",\"pbrMetallicRoughness\":{\"baseColorFactor\":[");
                builder.Append(Num(color.r)).Append(',').Append(Num(color.g)).Append(',');
                builder.Append(Num(color.b)).Append(',').Append(Num(color.a));
                builder.Append("],\"metallicFactor\":0,\"roughnessFactor\":0.8}");
                if (meshNodes[m].DoubleSided)
                {
                    builder.Append(",\"doubleSided\":true");
                }
                builder.Append('}');
            }
            builder.Append(']');

            builder.Append(",\"accessors\":[");
            for (int m = 0; m < meshNodes.Count; m++)
            {
                MeshBuffers mesh = meshNodes[m].Mesh;
                int v = m * 3;
                builder.Append(m == 0 ? "" : ",");

                // POSITION. min/max are required by the spec for this accessor,
                // and are the geometry's real bounding box in metres.
                builder.Append("{\"bufferView\":").Append(v).Append(",\"componentType\":").Append(ComponentTypeFloat);
                builder.Append(",\"count\":").Append(mesh.VertexCount);
                builder.Append(",\"type\":\"VEC3\",\"min\":[");
                builder.Append(Exact(mesh.Min.x)).Append(',').Append(Exact(mesh.Min.y)).Append(',').Append(Exact(mesh.Min.z));
                builder.Append("],\"max\":[");
                builder.Append(Exact(mesh.Max.x)).Append(',').Append(Exact(mesh.Max.y)).Append(',').Append(Exact(mesh.Max.z));
                builder.Append("]}");

                builder.Append(",{\"bufferView\":").Append(v + 1).Append(",\"componentType\":").Append(ComponentTypeFloat);
                builder.Append(",\"count\":").Append(mesh.VertexCount).Append(",\"type\":\"VEC3\"}");

                builder.Append(",{\"bufferView\":").Append(v + 2).Append(",\"componentType\":").Append(ComponentTypeUnsignedShort);
                builder.Append(",\"count\":").Append(mesh.IndexCount).Append(",\"type\":\"SCALAR\"}");
            }
            builder.Append(']');

            builder.Append(",\"bufferViews\":[");
            for (int i = 0; i < viewOffsets.Count; i++)
            {
                builder.Append(i == 0 ? "" : ",");
                // Views come in threes per mesh: positions, normals, indices.
                int target = i % 3 == 2 ? TargetElementArrayBuffer : TargetArrayBuffer;
                builder.Append("{\"buffer\":0,\"byteOffset\":").Append(viewOffsets[i])
                    .Append(",\"byteLength\":").Append(viewLengths[i])
                    .Append(",\"target\":").Append(target).Append('}');
            }
            builder.Append(']');

            builder.Append(",\"buffers\":[{\"byteLength\":").Append(binLength).Append("}]");

            builder.Append('}');

            return builder.ToString();
        }

        // -------------------------------------------------------------------
        // Mesh building
        // -------------------------------------------------------------------

        /// <summary>
        /// Accumulates flat-shaded triangles in glTF coordinates, converting
        /// from Ghost space (Unity axes) as they are added.
        /// </summary>
        private sealed class MeshBuffers
        {
            public MeshBuffers(int vertexCapacity, int indexCapacity)
            {
                Positions = new List<Vector3>(vertexCapacity);
                Normals = new List<Vector3>(vertexCapacity);
                Indices = new List<ushort>(indexCapacity);
                Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            }

            public List<Vector3> Positions { get; }

            public List<Vector3> Normals { get; }

            public List<ushort> Indices { get; }

            public Vector3 Min { get; private set; }

            public Vector3 Max { get; private set; }

            public int VertexCount => Positions.Count;

            public int IndexCount => Indices.Count;

            /// <summary>An axis-aligned box given in Ghost space.</summary>
            public void AddBox(Vector3 unityCenter, Vector3 unitySize)
                => AddOrientedBox(unityCenter, Vector3.right, Vector3.up, Vector3.forward, unitySize);

            /// <summary>
            /// A box given in Ghost space by its centre, three orthonormal axes
            /// and its size along them.
            ///
            /// <para>Built directly in glTF space. For each face the two
            /// in-plane axes are ordered so that <c>u x v</c> equals the
            /// outward normal; a quad emitted as
            /// <c>(-u-v, +u-v, +u+v, -u+v)</c> is then counter-clockwise seen
            /// from outside, glTF's front face, whatever the axes were.</para>
            /// </summary>
            public void AddOrientedBox(Vector3 unityCenter, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 unitySize)
            {
                Vector3 center = Mirror(unityCenter);
                Vector3[] axes = { Mirror(axisX).normalized, Mirror(axisY).normalized, Mirror(axisZ).normalized };
                float[] half = { Mathf.Abs(unitySize.x) * 0.5f, Mathf.Abs(unitySize.y) * 0.5f, Mathf.Abs(unitySize.z) * 0.5f };

                for (int k = 0; k < 3; k++)
                {
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        Vector3 normal = axes[k] * sign;
                        int iu = (k + 1) % 3;
                        int iv = (k + 2) % 3;
                        Vector3 u = axes[iu] * half[iu];
                        Vector3 v = axes[iv] * half[iv];

                        if (Vector3.Dot(Vector3.Cross(u, v), normal) < 0f)
                        {
                            (u, v) = (v, u);
                        }

                        Vector3 faceCenter = center + normal * half[k];
                        var first = (ushort)Positions.Count;

                        Add(faceCenter - u - v, normal);
                        Add(faceCenter + u - v, normal);
                        Add(faceCenter + u + v, normal);
                        Add(faceCenter - u + v, normal);

                        Indices.Add(first);
                        Indices.Add((ushort)(first + 1));
                        Indices.Add((ushort)(first + 2));

                        Indices.Add(first);
                        Indices.Add((ushort)(first + 2));
                        Indices.Add((ushort)(first + 3));
                    }
                }
            }

            /// <summary>
            /// A Unity-wound triangle list (front face where
            /// <c>Cross(b - a, c - a)</c> points). Mirroring Z reverses that,
            /// so each triangle's winding is reversed to keep the same visible
            /// side. Vertices are duplicated per triangle for flat normals.
            /// </summary>
            public void AddTriangles(Vector3[] unityVertices, int[] unityTriangles)
            {
                for (int t = 0; t + 2 < unityTriangles.Length; t += 3)
                {
                    Vector3 a = Mirror(unityVertices[unityTriangles[t]]);
                    Vector3 b = Mirror(unityVertices[unityTriangles[t + 2]]);
                    Vector3 c = Mirror(unityVertices[unityTriangles[t + 1]]);
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;

                    var first = (ushort)Positions.Count;
                    Add(a, normal);
                    Add(b, normal);
                    Add(c, normal);
                    Indices.Add(first);
                    Indices.Add((ushort)(first + 1));
                    Indices.Add((ushort)(first + 2));
                }
            }

            private void Add(Vector3 position, Vector3 normal)
            {
                Positions.Add(position);
                Normals.Add(normal);

                Min = Vector3.Min(Min, position);
                Max = Vector3.Max(Max, position);
            }

            /// <summary>Ghost (Unity) space to glTF: negate Z.</summary>
            private static Vector3 Mirror(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z);
        }

        // -------------------------------------------------------------------
        // Primitives
        // -------------------------------------------------------------------

        private static int Padding(int length)
        {
            int remainder = length % 4;
            return remainder == 0 ? 0 : 4 - remainder;
        }

        private static void WriteUInt(byte[] buffer, ref int cursor, uint value)
        {
            buffer[cursor++] = (byte)(value & 0xFF);
            buffer[cursor++] = (byte)((value >> 8) & 0xFF);
            buffer[cursor++] = (byte)((value >> 16) & 0xFF);
            buffer[cursor++] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteUShort(byte[] buffer, ref int cursor, ushort value)
        {
            buffer[cursor++] = (byte)(value & 0xFF);
            buffer[cursor++] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteVector(byte[] buffer, ref int cursor, Vector3 value)
        {
            WriteFloat(buffer, ref cursor, value.x);
            WriteFloat(buffer, ref cursor, value.y);
            WriteFloat(buffer, ref cursor, value.z);
        }

        private static void WriteFloat(byte[] buffer, ref int cursor, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);

            // glTF is little-endian. Every platform GhostMap targets already is,
            // but assuming it silently would be a trap for anything else.
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            Buffer.BlockCopy(bytes, 0, buffer, cursor, 4);
            cursor += 4;
        }

        /// <summary>
        /// Invariant-culture number formatting. Not optional: on a locale that
        /// writes decimals with a comma, default formatting would emit
        /// <c>0,8</c> and a JSON chunk no glTF reader can parse.
        /// </summary>
        private static string Num(float value)
            => value.ToString("0.######", CultureInfo.InvariantCulture);

        /// <summary>
        /// Round-trip formatting for accessor <c>min</c>/<c>max</c>. glTF
        /// requires them to equal the data's real extremes exactly; rounding
        /// to six decimals turned float noise such as <c>5.96e-9</c> on a
        /// rotated wall into <c>0</c>, which the Khronos validator rejects.
        /// </summary>
        private static string Exact(float value)
            => value.ToString("R", CultureInfo.InvariantCulture);

        internal static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        /// <summary>
        /// First segment of a GUID, enough to tell two objects apart in a
        /// name without making it unreadable.
        /// </summary>
        public static string Shorten(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "unknown";
            }

            int dash = id.IndexOf('-');
            return dash > 0 ? id.Substring(0, dash) : id;
        }
    }
}
