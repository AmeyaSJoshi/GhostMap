using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Viewer.Rendering;
using UnityEngine;

namespace GhostMap.Viewer.Export
{
    /// <summary>
    /// ADR-0006: turns one piece of measured furniture into a binary glTF
    /// (<c>.glb</c>) asset.
    ///
    /// <para><b>The asset is generated from the measurements, never authored or
    /// imported.</b> GhostMap holds furniture as seven numbers; this builds the
    /// boxes those numbers describe and writes them as a model file that
    /// Blender, Unreal, three.js or any glTF viewer can open. It is the first
    /// point at which GhostMap's structured data leaves as geometry.</para>
    ///
    /// <para><b>Geometry comes from <see cref="FurnitureFactory.BuildParts"/>,
    /// the same pure function the renderer uses.</b> This class does not know
    /// what a bed looks like and must never learn: if it had its own part table
    /// the exported file and the screen could disagree, and a change to one
    /// would silently not reach the other.</para>
    ///
    /// <para>Written by hand rather than with a glTF package. The payload is
    /// axis-aligned boxes, the format's binary container is a 12-byte header
    /// and two chunks, and adding a dependency to the Viewer project would be a
    /// bigger change than the writer itself.</para>
    ///
    /// <para>Pure C#: it returns bytes and touches no filesystem, so every byte
    /// of the container can be asserted in an EditMode test.
    /// <see cref="FurnitureAssetExporter"/> is the file-writing wrapper.</para>
    /// </summary>
    public static class GlbExporter
    {
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

        private const int VerticesPerBox = 24;
        private const int IndicesPerBox = 36;

        /// <summary>
        /// A box has 24 vertices, so this is the most boxes that can be indexed
        /// by unsigned shorts. Furniture uses at most a handful.
        /// </summary>
        public const int MaxBoxes = 65535 / VerticesPerBox;

        /// <summary>
        /// Exports one furniture object as <c>.glb</c> bytes.
        ///
        /// <para>The model is emitted in its own local space, sitting on
        /// <c>y = 0</c> and centred on the origin, with its yaw applied by the
        /// node transform rather than baked into the vertices. That is what
        /// makes the asset reusable: a bed exported from one room drops into any
        /// scene without carrying that room's coordinates.</para>
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

            IReadOnlyList<FurniturePartSpec> parts = FurnitureFactory.BuildParts(model);

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

            var mesh = new MeshBuffers(parts.Count);

            for (int i = 0; i < parts.Count; i++)
            {
                mesh.AddBox(parts[i].LocalCenter, parts[i].LocalSize);
            }

            string name = string.IsNullOrEmpty(model.type) ? "object" : model.type;

            glb = Build(mesh, $"{name}_{Shorten(model.id)}", FurnitureFactory.ColorFor(model.type));
            error = string.Empty;
            return true;
        }

        // -------------------------------------------------------------------
        // Container
        // -------------------------------------------------------------------

        private static byte[] Build(MeshBuffers mesh, string name, Color color)
        {
            int positionBytes = mesh.VertexCount * 3 * sizeof(float);
            int normalBytes = positionBytes;
            int indexBytes = mesh.IndexCount * sizeof(ushort);

            // Every accessor's bufferView offset must be a multiple of its
            // component size. Positions and normals are 12-byte strides, so the
            // index view's offset is already 4-aligned.
            int binLength = positionBytes + normalBytes + indexBytes;

            string json = BuildJson(
                mesh, name, color, positionBytes, normalBytes, indexBytes, binLength);

            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            // The JSON chunk pads with spaces and the binary chunk with zeros,
            // both to a 4-byte boundary. This is required, not cosmetic: a
            // reader computes the next chunk's position from these lengths.
            int jsonPadding = Padding(jsonBytes.Length);
            int binPadding = Padding(binLength);

            int jsonChunkLength = jsonBytes.Length + jsonPadding;
            int binChunkLength = binLength + binPadding;

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

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vector3 position = mesh.Positions[i];
                WriteFloat(buffer, ref cursor, position.x);
                WriteFloat(buffer, ref cursor, position.y);
                WriteFloat(buffer, ref cursor, position.z);
            }

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vector3 normal = mesh.Normals[i];
                WriteFloat(buffer, ref cursor, normal.x);
                WriteFloat(buffer, ref cursor, normal.y);
                WriteFloat(buffer, ref cursor, normal.z);
            }

            for (int i = 0; i < mesh.IndexCount; i++)
            {
                WriteUShort(buffer, ref cursor, mesh.Indices[i]);
            }

            // Trailing zero padding is already zero from the array allocation.
            return buffer;
        }

        private static string BuildJson(
            MeshBuffers mesh,
            string name,
            Color color,
            int positionBytes,
            int normalBytes,
            int indexBytes,
            int binLength)
        {
            var builder = new StringBuilder(1536);

            builder.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"GhostMap glTF exporter (ADR-0006)\"}");
            builder.Append(",\"scene\":0,\"scenes\":[{\"nodes\":[0]}]");

            builder.Append(",\"nodes\":[{\"mesh\":0,\"name\":").Append(Quote(name)).Append("}]");

            builder.Append(",\"meshes\":[{\"name\":").Append(Quote(name));
            builder.Append(",\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1}");
            builder.Append(",\"indices\":2,\"material\":0}]}]");

            builder.Append(",\"materials\":[{\"name\":").Append(Quote(name + "_material"));
            builder.Append(",\"pbrMetallicRoughness\":{\"baseColorFactor\":[");
            builder.Append(Num(color.r)).Append(',').Append(Num(color.g)).Append(',');
            builder.Append(Num(color.b)).Append(',').Append(Num(color.a));
            builder.Append("],\"metallicFactor\":0,\"roughnessFactor\":0.8}}]");

            builder.Append(",\"accessors\":[");

            // POSITION. min/max are required by the spec for this accessor, and
            // are also the object's real bounding box in meters, which makes the
            // file self-describing to anything that reads bounds.
            builder.Append("{\"bufferView\":0,\"componentType\":").Append(ComponentTypeFloat);
            builder.Append(",\"count\":").Append(mesh.VertexCount);
            builder.Append(",\"type\":\"VEC3\",\"min\":[");
            builder.Append(Num(mesh.Min.x)).Append(',').Append(Num(mesh.Min.y)).Append(',')
                .Append(Num(mesh.Min.z));
            builder.Append("],\"max\":[");
            builder.Append(Num(mesh.Max.x)).Append(',').Append(Num(mesh.Max.y)).Append(',')
                .Append(Num(mesh.Max.z));
            builder.Append("]}");

            builder.Append(",{\"bufferView\":1,\"componentType\":").Append(ComponentTypeFloat);
            builder.Append(",\"count\":").Append(mesh.VertexCount).Append(",\"type\":\"VEC3\"}");

            builder.Append(",{\"bufferView\":2,\"componentType\":").Append(ComponentTypeUnsignedShort);
            builder.Append(",\"count\":").Append(mesh.IndexCount).Append(",\"type\":\"SCALAR\"}");

            builder.Append(']');

            builder.Append(",\"bufferViews\":[");
            builder.Append("{\"buffer\":0,\"byteOffset\":0,\"byteLength\":").Append(positionBytes).Append('}');
            builder.Append(",{\"buffer\":0,\"byteOffset\":").Append(positionBytes)
                .Append(",\"byteLength\":").Append(normalBytes).Append('}');
            builder.Append(",{\"buffer\":0,\"byteOffset\":").Append(positionBytes + normalBytes)
                .Append(",\"byteLength\":").Append(indexBytes).Append('}');
            builder.Append(']');

            builder.Append(",\"buffers\":[{\"byteLength\":").Append(binLength).Append("}]");

            builder.Append('}');

            return builder.ToString();
        }

        // -------------------------------------------------------------------
        // Mesh building
        // -------------------------------------------------------------------

        /// <summary>
        /// Accumulates boxes as flat-shaded triangles, in glTF coordinates.
        /// </summary>
        private sealed class MeshBuffers
        {
            /// <summary>
            /// The six faces of a box, as (outward normal, u, v) with
            /// <c>u x v = normal</c>.
            ///
            /// <para>That cross-product relationship is the whole trick: a quad
            /// emitted in the order
            /// <c>(-u-v, +u-v, +u+v, -u+v)</c> is then counter-clockwise seen
            /// from outside, which is glTF's front face. It removes any need to
            /// reason about winding per face.</para>
            /// </summary>
            private static readonly (Vector3 Normal, Vector3 U, Vector3 V)[] Faces =
            {
                (new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f)),
                (new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f)),
                (new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f)),
                (new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)),
                (new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)),
                (new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f))
            };

            public MeshBuffers(int boxCount)
            {
                Positions = new List<Vector3>(boxCount * VerticesPerBox);
                Normals = new List<Vector3>(boxCount * VerticesPerBox);
                Indices = new List<ushort>(boxCount * IndicesPerBox);
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

            /// <summary>
            /// Appends one axis-aligned box, converting Unity coordinates to
            /// glTF's.
            ///
            /// <para>Unity is left-handed with +Z forward; glTF is right-handed
            /// with +Z toward the viewer. Negating Z maps between them, and
            /// because that mirror would otherwise turn every face inside out,
            /// the faces are authored directly in glTF space with the winding
            /// rule above rather than being flipped afterwards.</para>
            /// </summary>
            public void AddBox(Vector3 unityCenter, Vector3 unitySize)
            {
                var center = new Vector3(unityCenter.x, unityCenter.y, -unityCenter.z);

                var half = new Vector3(
                    Mathf.Abs(unitySize.x) * 0.5f,
                    Mathf.Abs(unitySize.y) * 0.5f,
                    Mathf.Abs(unitySize.z) * 0.5f);

                for (int f = 0; f < Faces.Length; f++)
                {
                    (Vector3 normal, Vector3 u, Vector3 v) = Faces[f];

                    Vector3 faceCenter = center + Scale(normal, half);
                    Vector3 uHalf = Scale(u, half);
                    Vector3 vHalf = Scale(v, half);

                    var first = (ushort)Positions.Count;

                    Add(faceCenter - uHalf - vHalf, normal);
                    Add(faceCenter + uHalf - vHalf, normal);
                    Add(faceCenter + uHalf + vHalf, normal);
                    Add(faceCenter - uHalf + vHalf, normal);

                    Indices.Add(first);
                    Indices.Add((ushort)(first + 1));
                    Indices.Add((ushort)(first + 2));

                    Indices.Add(first);
                    Indices.Add((ushort)(first + 2));
                    Indices.Add((ushort)(first + 3));
                }
            }

            private void Add(Vector3 position, Vector3 normal)
            {
                Positions.Add(position);
                Normals.Add(normal);

                Min = Vector3.Min(Min, position);
                Max = Vector3.Max(Max, position);
            }

            /// <summary>Component-wise product; picks the half-extent along an axis.</summary>
            private static Vector3 Scale(Vector3 axis, Vector3 half)
                => new Vector3(axis.x * half.x, axis.y * half.y, axis.z * half.z);
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
        /// Invariant-culture number formatting.
        ///
        /// <para>Not optional. On a locale that writes decimals with a comma,
        /// default formatting would emit <c>0,8</c> and produce a JSON chunk
        /// that no glTF reader can parse — a bug that would never appear on a
        /// machine set to English.</para>
        /// </summary>
        private static string Num(float value)
            => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Quote(string value)
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
        /// filename without making it unreadable.
        /// </summary>
        internal static string Shorten(string id)
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
