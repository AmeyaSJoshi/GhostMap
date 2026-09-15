using System;
using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// One wall's render transform: a cuboid spanning the floor to the
    /// ceiling, centered on the wall's centerline.
    /// </summary>
    public readonly struct WallRenderSpec
    {
        public string StartCornerId { get; }
        public string EndCornerId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public float LengthM { get; }

        public WallRenderSpec(
            string startCornerId,
            string endCornerId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            float lengthM)
        {
            StartCornerId = startCornerId;
            EndCornerId = endCornerId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            LengthM = lengthM;
        }
    }

    /// <summary>
    /// Task V2: turns the shared package's derived <see cref="WallDefinition"/>
    /// list (never a viewer-side reimplementation of wall derivation) into
    /// solid cuboid transforms, one per consecutive corner pair. V3 cuts
    /// door/window openings into these; V2 renders them solid.
    /// </summary>
    public static class WallRenderer
    {
        /// <summary>Wall thickness per implementation plan section 12.3.</summary>
        public const float WallThicknessM = 0.10f;

        public static IReadOnlyList<WallRenderSpec> BuildWalls(RoomModel room)
        {
            if (room == null || room.heightM <= 0f)
            {
                return Array.Empty<WallRenderSpec>();
            }

            IReadOnlyList<WallDefinition> walls = RoomGeometry.BuildWalls(room);
            var specs = new List<WallRenderSpec>(walls.Count);

            for (int i = 0; i < walls.Count; i++)
            {
                WallDefinition wall = walls[i];

                if (wall.LengthM <= Mathf.Epsilon)
                {
                    continue;
                }

                Vector3 midpoint = (wall.Start + wall.End) * 0.5f;
                var position = new Vector3(midpoint.x, room.heightM * 0.5f, midpoint.z);
                Quaternion rotation = Quaternion.FromToRotation(Vector3.right, wall.Tangent);
                var scale = new Vector3(wall.LengthM, room.heightM, WallThicknessM);

                specs.Add(new WallRenderSpec(
                    wall.StartCornerId,
                    wall.EndCornerId,
                    position,
                    rotation,
                    scale,
                    wall.LengthM));
            }

            return specs;
        }
    }
}
