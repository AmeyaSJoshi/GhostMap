using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Export;
using GhostMap.Shared.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>
    /// Task R6: the whole-room <c>room.glb</c>. Each test reads the file back
    /// (JSON and binary chunk) instead of trusting the writer's own
    /// bookkeeping. Contract: docs/contracts/export-bundle-v1.md, section 3.
    /// </summary>
    public sealed class GlbWriterRoomTests
    {
        private const float Tolerance = 1e-4f;

        // ---------------------------------------------------------------
        // Structure
        // ---------------------------------------------------------------

        [Test]
        public void Room_HasFloorCeilingAndOneNodePerWall()
        {
            Gltf gltf = Export(Room());

            string[] names = gltf.nodes.Select(n => n.name).ToArray();
            CollectionAssert.AreEqual(
                new[] { "floor", "ceiling", "walls", "wall_0", "wall_1", "wall_2", "wall_3" }, names);

            Node walls = gltf.nodes[2];
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 6 }, walls.children);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, gltf.scenes[0].nodes);
        }

        [Test]
        public void Room_HasExactlyOneNodeNamedCeiling()
        {
            // export-bundle-v1 section 3.1: the browser viewer's dollhouse hides it.
            Gltf gltf = Export(Room());
            Assert.AreEqual(1, gltf.nodes.Count(n => n.name == GlbWriter.CeilingNodeName));
            Assert.AreEqual("ceiling", GlbWriter.CeilingNodeName);
        }

        [Test]
        public void Room_FloorAndCeilingAreDoubleSided_WallsAreNot()
        {
            string json = Json(ExportBytes(Room()));
            Gltf gltf = JsonUtility.FromJson<Gltf>(json);

            Assert.AreEqual(6, gltf.materials.Length, "floor, ceiling and four walls");
            Assert.IsTrue(gltf.materials[0].doubleSided, "floor");
            Assert.IsTrue(gltf.materials[1].doubleSided, "ceiling");
            for (int i = 2; i < gltf.materials.Length; i++)
            {
                Assert.IsFalse(gltf.materials[i].doubleSided, gltf.materials[i].name);
            }
        }

        [Test]
        public void Room_UsesTheSharedRoomPalette()
        {
            Gltf gltf = Export(Room());
            AssertColor(RoomPalette.Floor, gltf.materials[0]);
            AssertColor(RoomPalette.Ceiling, gltf.materials[1]);
            AssertColor(RoomPalette.Wall, gltf.materials[2]);
        }

        // ---------------------------------------------------------------
        // Geometry
        // ---------------------------------------------------------------

        [Test]
        public void Room_FloorAndCeilingSpanTheFootprintWithZNegated()
        {
            // Ghost corners x in [0, 4], z in [0, 3]; glTF z = -Ghost z.
            byte[] glb = ExportBytes(Room());
            Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));

            Bounds floor = BoundsOf(glb, gltf, 0);
            AssertVector(new Vector3(0f, 0f, -3f), floor.min, "floor min");
            AssertVector(new Vector3(4f, 0f, 0f), floor.max, "floor max");

            Bounds ceiling = BoundsOf(glb, gltf, 1);
            Assert.AreEqual(2.5f, ceiling.min.y, Tolerance);
            Assert.AreEqual(2.5f, ceiling.max.y, Tolerance);
        }

        [Test]
        public void Room_EachWallHasExactlyTheLayoutsSegments()
        {
            RoomModel room = Room();
            IReadOnlyList<WallRenderSpec> layout = WallLayout.BuildWalls(room);
            Gltf gltf = Export(room);

            for (int w = 0; w < 4; w++)
            {
                int expected = layout[w].Segments.Count * 24;
                Assert.AreEqual(expected, gltf.accessors[(2 + w) * 3].count, $"wall_{w} vertices");
            }

            // The door wall is cut into five pieces (left, right, above; and
            // the grid's two cells either side above), the window wall into
            // eight around one hole; plain walls are one piece each.
            Assert.AreEqual(5, layout[0].Segments.Count, "door wall");
            Assert.AreEqual(8, layout[1].Segments.Count, "window wall");
            Assert.AreEqual(1, layout[2].Segments.Count);
            Assert.AreEqual(1, layout[3].Segments.Count);
        }

        [Test]
        public void Room_DoorIsARealHole()
        {
            // Door on wall c0 -> c1 (Ghost z = 0): offset 1.2, width 0.9,
            // height 2.05. No wall_0 triangle may have its centroid inside it.
            byte[] glb = ExportBytes(Room());
            Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));

            foreach (Vector3[] triangle in Triangles(glb, gltf, 2)) // wall_0 is mesh 2
            {
                Vector3 c = (triangle[0] + triangle[1] + triangle[2]) / 3f;
                bool insideDoor = c.x > 1.2f + 1e-3f && c.x < 2.1f - 1e-3f && c.y < 2.05f - 1e-3f;
                Assert.IsFalse(insideDoor, $"wall geometry inside the doorway at {c}");
            }
        }

        [Test]
        public void Room_WallsSitOnTheirCentrelineAtTheWallThickness()
        {
            // A room rotated 30 degrees, so wall boxes are not axis-aligned.
            RoomModel room = Rotated(Room(), 30f);
            byte[] glb = ExportBytes(room);
            Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));
            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(room);

            for (int w = 0; w < 4; w++)
            {
                Vector3 start = new Vector3(walls[w].Start.x, 0f, -walls[w].Start.z);
                Vector3 end = new Vector3(walls[w].End.x, 0f, -walls[w].End.z);
                Vector3 along = (end - start).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, along).normalized;

                float maxAcross = 0f;
                foreach (Vector3 p in Positions(glb, gltf, (2 + w) * 3))
                {
                    float d = Mathf.Abs(Vector3.Dot(new Vector3(p.x, 0f, p.z) - start, across));
                    maxAcross = Mathf.Max(maxAcross, d);
                    float u = Vector3.Dot(new Vector3(p.x, 0f, p.z) - start, along);
                    Assert.That(u, Is.InRange(-1e-3f, walls[w].LengthM + 1e-3f), $"wall_{w} along");
                    Assert.That(p.y, Is.InRange(-1e-4f, 2.5f + 1e-4f), $"wall_{w} height");
                }

                Assert.AreEqual(WallLayout.WallThicknessM * 0.5f, maxAcross, 1e-3f, $"wall_{w} half thickness");
            }
        }

        [Test]
        public void Room_EveryTriangleIsWoundToItsNormal()
        {
            // glTF front faces are counter-clockwise: the right-handed cross
            // product of each triangle must agree with its stored normal. A
            // mirrored (Z-negated) mesh fails this unless the writer re-winds.
            foreach (RoomModel room in new[] { Room(), Rotated(Room(), 30f), Rotated(Room(), 180f), Rotated(Room(), 200f) })
            {
                byte[] glb = ExportBytes(room);
                Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));

                for (int mesh = 0; mesh < gltf.meshes.Length; mesh++)
                {
                    List<Vector3> normals = ReadVec3(glb, gltf, mesh * 3 + 1);
                    ushort[] indices = ReadIndices(glb, gltf, mesh * 3 + 2);
                    List<Vector3> positions = ReadVec3(glb, gltf, mesh * 3);

                    for (int t = 0; t < indices.Length; t += 3)
                    {
                        Vector3 a = positions[indices[t]];
                        Vector3 b = positions[indices[t + 1]];
                        Vector3 c = positions[indices[t + 2]];
                        Vector3 face = Cross(b - a, c - a);
                        Assert.Greater(Vector3.Dot(face, normals[indices[t]]), 0f,
                            $"mesh {gltf.meshes[mesh].name} triangle {t / 3}");
                    }
                }
            }
        }

        [Test]
        public void Room_FloorFacesUpAndCeilingFacesDown()
        {
            byte[] glb = ExportBytes(Room());
            Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));

            foreach (Vector3 n in ReadVec3(glb, gltf, 1))
            {
                AssertVector(Vector3.up, n, "floor normal");
            }

            foreach (Vector3 n in ReadVec3(glb, gltf, 4))
            {
                AssertVector(Vector3.down, n, "ceiling normal");
            }
        }

        [Test]
        public void Object_EveryTriangleIsWoundToItsNormal()
        {
            foreach (string type in new[] { "bed", "desk", "chair", "couch", "table", "dresser", "tv", "generic" })
            {
                var model = new SceneObjectModel
                {
                    id = type + "-1", type = type, center = new Vec3Dto(1f, 0f, 1f),
                    yawDeg = 30f, widthM = 1f, depthM = 0.8f, heightM = 0.9f
                };
                Assert.IsTrue(GlbWriter.TryExportObject(model, out byte[] glb, out string error), error);
                Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));

                List<Vector3> positions = ReadVec3(glb, gltf, 0);
                List<Vector3> normals = ReadVec3(glb, gltf, 1);
                ushort[] indices = ReadIndices(glb, gltf, 2);

                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 face = Cross(positions[indices[t + 1]] - positions[indices[t]],
                                         positions[indices[t + 2]] - positions[indices[t]]);
                    Assert.Greater(Vector3.Dot(face, normals[indices[t]]), 0f, $"{type} triangle {t / 3}");
                }
            }
        }

        [Test]
        public void Room_ContainerDeclaresItsRealLengthAndPadsChunks()
        {
            byte[] glb = ExportBytes(Room());
            Assert.AreEqual("glTF", Encoding.ASCII.GetString(glb, 0, 4));
            Assert.AreEqual((uint)glb.Length, BitConverter.ToUInt32(glb, 8));
            Assert.AreEqual(0, glb.Length % 4);

            uint jsonLength = BitConverter.ToUInt32(glb, 12);
            uint binLength = BitConverter.ToUInt32(glb, 20 + (int)jsonLength);
            Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));
            Assert.AreEqual(binLength, (uint)gltf.buffers[0].byteLength, "buffer length equals BIN chunk");

            foreach (BufferView view in gltf.bufferViews)
            {
                Assert.AreEqual(0, view.byteOffset % 4, "every view starts 4-aligned");
                Assert.LessOrEqual(view.byteOffset + view.byteLength, (int)binLength);
            }
        }

        [Test]
        public void DeclaredBounds_EqualTheDataExactly()
        {
            // Regression: min/max were written to six decimals, so float noise
            // on rotated walls (5.96e-9 declared as 0) broke the glTF rule that
            // declared bounds equal the data. Found by the Khronos validator.
            foreach (RoomModel room in new[] { Room(), Rotated(Room(), 30f), Rotated(Room(), 180f) })
            {
                byte[] glb = ExportBytes(room);
                Gltf gltf = JsonUtility.FromJson<Gltf>(Json(glb));
                for (int mesh = 0; mesh < gltf.meshes.Length; mesh++)
                {
                    AssertDeclaredBoundsAreExact(glb, gltf, mesh * 3);
                }
            }

            var desk = new SceneObjectModel
            {
                id = "d", type = "desk", center = new Vec3Dto(0f, 0f, 0f), widthM = 1.37f, depthM = 0.71f, heightM = 0.733f
            };
            Assert.IsTrue(GlbWriter.TryExportObject(desk, out byte[] deskGlb, out _));
            AssertDeclaredBoundsAreExact(deskGlb, JsonUtility.FromJson<Gltf>(Json(deskGlb)), 0);
        }

        [Test]
        public void BufferViews_DeclareVertexOrIndexTargets()
        {
            Gltf gltf = Export(Room());
            for (int i = 0; i < gltf.bufferViews.Length; i++)
            {
                Assert.AreEqual(i % 3 == 2 ? 34963 : 34962, gltf.bufferViews[i].target, $"bufferView {i}");
            }
        }

        [Test]
        public void Room_IsDeterministic()
        {
            CollectionAssert.AreEqual(ExportBytes(Room()), ExportBytes(Room()));
        }

        // ---------------------------------------------------------------
        // Refusals
        // ---------------------------------------------------------------

        [Test]
        public void Room_RefusesNullZeroHeightAndTooFewCorners()
        {
            Assert.IsFalse(GlbWriter.TryExportRoom(null, out byte[] glb, out string error));
            Assert.IsNull(glb);
            Assert.IsNotEmpty(error);

            RoomModel flat = Room();
            flat.heightM = 0f;
            Assert.IsFalse(GlbWriter.TryExportRoom(flat, out _, out error));
            StringAssert.Contains("height", error);

            RoomModel line = Room();
            line.corners = line.corners.Take(2).ToArray();
            Assert.IsFalse(GlbWriter.TryExportRoom(line, out _, out error));
            Assert.IsNotEmpty(error);
        }

        // ---------------------------------------------------------------
        // Fixtures and readers
        // ---------------------------------------------------------------

        /// <summary>The fixtures/room-with-door-window-v1.json room.</summary>
        private static RoomModel Room()
        {
            return new RoomModel
            {
                id = "room-1",
                name = "Bedroom",
                heightM = 2.5f,
                corners = new[]
                {
                    new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                    new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                    new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                    new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
                },
                openings = new[]
                {
                    new OpeningModel
                    {
                        id = "door-1", type = "door", wallStartCornerId = "c0", wallEndCornerId = "c1",
                        offsetM = 1.2f, widthM = 0.9f, sillHeightM = 0f, heightM = 2.05f
                    },
                    new OpeningModel
                    {
                        id = "window-1", type = "window", wallStartCornerId = "c1", wallEndCornerId = "c2",
                        offsetM = 0.9f, widthM = 1.2f, sillHeightM = 0.9f, heightM = 1.1f
                    }
                },
                objects = new SceneObjectModel[0]
            };
        }

        private static RoomModel Rotated(RoomModel room, float degrees)
        {
            Quaternion q = Quaternion.Euler(0f, degrees, 0f);
            foreach (CornerModel corner in room.corners)
            {
                corner.position = Vec3Dto.FromVector3(q * corner.position.ToVector3());
            }
            return room;
        }

        private static byte[] ExportBytes(RoomModel room)
        {
            Assert.IsTrue(GlbWriter.TryExportRoom(room, out byte[] glb, out string error), error);
            return glb;
        }

        private static Gltf Export(RoomModel room) => JsonUtility.FromJson<Gltf>(Json(ExportBytes(room)));

        private static string Json(byte[] glb)
        {
            int length = (int)BitConverter.ToUInt32(glb, 12);
            return Encoding.UTF8.GetString(glb, 20, length).TrimEnd(' ');
        }

        private static int BinStart(byte[] glb) => 20 + (int)BitConverter.ToUInt32(glb, 12) + 8;

        private static List<Vector3> ReadVec3(byte[] glb, Gltf gltf, int accessor)
        {
            Accessor a = gltf.accessors[accessor];
            BufferView view = gltf.bufferViews[a.bufferView];
            int start = BinStart(glb) + view.byteOffset;
            var result = new List<Vector3>(a.count);
            for (int i = 0; i < a.count; i++)
            {
                int o = start + i * 12;
                result.Add(new Vector3(
                    BitConverter.ToSingle(glb, o), BitConverter.ToSingle(glb, o + 4), BitConverter.ToSingle(glb, o + 8)));
            }
            return result;
        }

        private static ushort[] ReadIndices(byte[] glb, Gltf gltf, int accessor)
        {
            Accessor a = gltf.accessors[accessor];
            Assert.AreEqual(5123, a.componentType, "UNSIGNED_SHORT");
            BufferView view = gltf.bufferViews[a.bufferView];
            int start = BinStart(glb) + view.byteOffset;
            var result = new ushort[a.count];
            for (int i = 0; i < a.count; i++)
            {
                result[i] = BitConverter.ToUInt16(glb, start + i * 2);
            }
            return result;
        }

        private static IEnumerable<Vector3> Positions(byte[] glb, Gltf gltf, int accessor) => ReadVec3(glb, gltf, accessor);

        private static IEnumerable<Vector3[]> Triangles(byte[] glb, Gltf gltf, int mesh)
        {
            List<Vector3> positions = ReadVec3(glb, gltf, mesh * 3);
            ushort[] indices = ReadIndices(glb, gltf, mesh * 3 + 2);
            for (int t = 0; t < indices.Length; t += 3)
            {
                yield return new[] { positions[indices[t]], positions[indices[t + 1]], positions[indices[t + 2]] };
            }
        }

        private static Bounds BoundsOf(byte[] glb, Gltf gltf, int mesh)
        {
            List<Vector3> positions = ReadVec3(glb, gltf, mesh * 3);
            var bounds = new Bounds(positions[0], Vector3.zero);
            foreach (Vector3 p in positions)
            {
                bounds.Encapsulate(p);
            }

            AssertDeclaredBoundsAreExact(glb, gltf, mesh * 3);
            return bounds;
        }

        private static void AssertDeclaredBoundsAreExact(byte[] glb, Gltf gltf, int accessor)
        {
            List<Vector3> positions = ReadVec3(glb, gltf, accessor);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Vector3 p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            Accessor a = gltf.accessors[accessor];
            for (int k = 0; k < 3; k++)
            {
                Assert.AreEqual(min[k], a.min[k], 0f, $"accessor {accessor} min[{k}]");
                Assert.AreEqual(max[k], a.max[k], 0f, $"accessor {accessor} max[{k}]");
            }
        }

        /// <summary>Right-handed cross product, as glTF defines front faces.</summary>
        private static Vector3 Cross(Vector3 a, Vector3 b)
            => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        private static void AssertVector(Vector3 expected, Vector3 actual, string label)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-3f, label + " x");
            Assert.AreEqual(expected.y, actual.y, 1e-3f, label + " y");
            Assert.AreEqual(expected.z, actual.z, 1e-3f, label + " z");
        }

        private static void AssertColor(Color expected, Material material)
        {
            float[] c = material.pbrMetallicRoughness.baseColorFactor;
            Assert.AreEqual(expected.r, c[0], 1e-3f, material.name);
            Assert.AreEqual(expected.g, c[1], 1e-3f, material.name);
            Assert.AreEqual(expected.b, c[2], 1e-3f, material.name);
        }

#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable] private sealed class Gltf
        {
            public Scene[] scenes; public Node[] nodes; public Mesh[] meshes; public Material[] materials;
            public Accessor[] accessors; public BufferView[] bufferViews; public GltfBuffer[] buffers;
        }
        [Serializable] private sealed class Scene { public int[] nodes; }
        [Serializable] private sealed class Node { public string name; public int[] children; }
        [Serializable] private sealed class Mesh { public string name; }
        [Serializable] private sealed class Material { public string name; public bool doubleSided; public Pbr pbrMetallicRoughness; }
        [Serializable] private sealed class Pbr { public float[] baseColorFactor; }
        [Serializable] private sealed class Accessor { public int bufferView; public int componentType; public int count; public float[] min; public float[] max; }
        [Serializable] private sealed class BufferView { public int byteOffset; public int byteLength; public int target; }
        [Serializable] private sealed class GltfBuffer { public int byteLength; }
#pragma warning restore 0649
    }
}
