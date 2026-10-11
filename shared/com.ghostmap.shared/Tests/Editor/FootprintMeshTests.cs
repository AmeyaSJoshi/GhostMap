using GhostMap.Shared.Domain;
using GhostMap.Shared.Export;
using NUnit.Framework;
using UnityEngine;

namespace GhostMap.Shared.Tests
{
    /// <summary>Task R6: the shared floor and ceiling triangulation.</summary>
    public sealed class FootprintMeshTests
    {
        private static RoomModel Room(bool clockwise)
        {
            var corners = new[]
            {
                new CornerModel { id = "c0", position = new Vec3Dto(0f, 0f, 0f) },
                new CornerModel { id = "c1", position = new Vec3Dto(4f, 0f, 0f) },
                new CornerModel { id = "c2", position = new Vec3Dto(4f, 0f, 3f) },
                new CornerModel { id = "c3", position = new Vec3Dto(0f, 0f, 3f) }
            };
            if (clockwise)
            {
                System.Array.Reverse(corners);
            }
            return new RoomModel { id = "r", heightM = 2.5f, corners = corners };
        }

        [Test]
        public void Floor_IsAFanOfTwoTrianglesAtYZero([Values(false, true)] bool clockwise)
        {
            Assert.IsTrue(FootprintMesh.TryBuildFloor(Room(clockwise), out Vector3[] vertices, out int[] triangles));
            Assert.AreEqual(4, vertices.Length);
            Assert.AreEqual(6, triangles.Length);
            foreach (Vector3 v in vertices)
            {
                Assert.AreEqual(0f, v.y);
            }
        }

        [Test]
        public void FloorFacesUp_CeilingFacesDown_WhateverTheCornerOrder([Values(false, true)] bool clockwise)
        {
            RoomModel room = Room(clockwise);

            Assert.IsTrue(FootprintMesh.TryBuildFloor(room, out Vector3[] fv, out int[] ft));
            for (int t = 0; t < ft.Length; t += 3)
            {
                Assert.Greater(Vector3.Dot(Vector3.Cross(fv[ft[t + 1]] - fv[ft[t]], fv[ft[t + 2]] - fv[ft[t]]), Vector3.up), 0f);
            }

            Assert.IsTrue(FootprintMesh.TryBuildCeiling(room, out Vector3[] cv, out int[] ct));
            for (int t = 0; t < ct.Length; t += 3)
            {
                Assert.AreEqual(2.5f, cv[ct[t]].y);
                Assert.Greater(Vector3.Dot(Vector3.Cross(cv[ct[t + 1]] - cv[ct[t]], cv[ct[t + 2]] - cv[ct[t]]), Vector3.down), 0f);
            }
        }

        [Test]
        public void RefusesTooFewCornersAndAMissingHeight()
        {
            Assert.IsFalse(FootprintMesh.TryBuildFloor(null, out _, out _));
            Assert.IsFalse(FootprintMesh.TryBuildFloor(new RoomModel { corners = new CornerModel[2] }, out _, out _));

            RoomModel flat = Room(false);
            flat.heightM = 0f;
            Assert.IsTrue(FootprintMesh.TryBuildFloor(flat, out _, out _), "a floor needs no height");
            Assert.IsFalse(FootprintMesh.TryBuildCeiling(flat, out _, out _));
        }
    }
}
