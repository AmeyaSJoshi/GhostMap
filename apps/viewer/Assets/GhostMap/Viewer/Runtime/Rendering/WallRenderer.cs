using System;
using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using UnityEngine;

namespace GhostMap.Viewer.Rendering
{
    /// <summary>
    /// One solid piece of a wall, placed in world space. A wall with no
    /// openings has exactly one segment, spanning the whole wall — identical
    /// to the single cuboid V2 rendered.
    /// </summary>
    public readonly struct WallSegmentSpec
    {
        public WallSlice Slice { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }

        public WallSegmentSpec(WallSlice slice, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Slice = slice;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }

    /// <summary>
    /// One wall's render description: the full-wall cuboid transform that V2
    /// used, plus (V3) the solid segments left once its doors and windows are
    /// cut out.
    /// </summary>
    public readonly struct WallRenderSpec
    {
        public string StartCornerId { get; }
        public string EndCornerId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public float LengthM { get; }

        /// <summary>
        /// The solid regions of this wall, in wall order. Never overlapping,
        /// never covering an opening, and together covering the solid part of
        /// the wall exactly once.
        /// </summary>
        public IReadOnlyList<WallSegmentSpec> Segments { get; }

        public WallRenderSpec(
            string startCornerId,
            string endCornerId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            float lengthM,
            IReadOnlyList<WallSegmentSpec> segments)
        {
            StartCornerId = startCornerId;
            EndCornerId = endCornerId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            LengthM = lengthM;
            Segments = segments ?? Array.Empty<WallSegmentSpec>();
        }
    }

    /// <summary>
    /// Turns the shared package's derived <see cref="WallDefinition"/> list
    /// (never a viewer-side reimplementation of wall derivation) into world
    /// space cuboid transforms.
    ///
    /// V2 produced one solid cuboid per consecutive corner pair. V3 keeps that
    /// transform — a wall with no openings still renders exactly where V2 put
    /// it — but additionally cuts each wall into <see cref="WallSegmentSpec"/>
    /// pieces around its doors and windows, using
    /// <see cref="WallSliceGenerator"/>'s deterministic grid decomposition
    /// rather than runtime mesh booleans.
    ///
    /// An opening names its wall by an <em>ordered</em> corner pair, because
    /// its <c>offsetM</c> is measured from the start corner. The reversed pair
    /// deliberately does not match: accepting it would silently mirror the
    /// opening to the far end of the wall.
    /// </summary>
    public static class WallRenderer
    {
        /// <summary>Wall thickness per implementation plan section 12.3.</summary>
        public const float WallThicknessM = 0.10f;

        public static IReadOnlyList<WallRenderSpec> BuildWalls(RoomModel room)
        {
            return BuildWalls(room, null);
        }

        /// <param name="diagnostics">
        /// Optional sink for a human-readable line per opening that could not
        /// be rendered. Openings are validated by <c>OpeningValidator</c>
        /// before a snapshot is ever accepted, so anything reported here means
        /// bad data reached the renderer by another route; the affected wall
        /// is rendered solid rather than crashing or half-built.
        /// </param>
        public static IReadOnlyList<WallRenderSpec> BuildWalls(
            RoomModel room,
            IList<string> diagnostics)
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

                List<OpeningModel> wallOpenings = CollectOpeningsFor(room, wall);

                IReadOnlyList<WallSlice> slices = WallSliceGenerator.BuildSlices(
                    wall.LengthM, room.heightM, wallOpenings, diagnostics);

                specs.Add(new WallRenderSpec(
                    wall.StartCornerId,
                    wall.EndCornerId,
                    position,
                    rotation,
                    scale,
                    wall.LengthM,
                    BuildSegments(wall, rotation, slices)));
            }

            ReportUnmatchedOpenings(room, walls, diagnostics);

            return specs;
        }

        /// <summary>
        /// Places each wall-local slice in world space. The wall's local frame
        /// has its origin at the start corner on the floor, <c>u</c> along the
        /// wall tangent and <c>v</c> along world up; the corner's own <c>y</c>
        /// is ignored, exactly as V2's full-wall transform ignored it.
        /// </summary>
        private static IReadOnlyList<WallSegmentSpec> BuildSegments(
            WallDefinition wall,
            Quaternion rotation,
            IReadOnlyList<WallSlice> slices)
        {
            var segments = new List<WallSegmentSpec>(slices.Count);
            var origin = new Vector3(wall.Start.x, 0f, wall.Start.z);

            for (int i = 0; i < slices.Count; i++)
            {
                WallSlice slice = slices[i];

                Vector3 position = origin
                                 + wall.Tangent * slice.CenterU
                                 + Vector3.up * slice.CenterV;

                segments.Add(new WallSegmentSpec(
                    slice,
                    position,
                    rotation,
                    new Vector3(slice.WidthM, slice.HeightM, WallThicknessM)));
            }

            return segments;
        }

        private static List<OpeningModel> CollectOpeningsFor(RoomModel room, WallDefinition wall)
        {
            var matches = new List<OpeningModel>();

            if (room.openings == null)
            {
                return matches;
            }

            for (int i = 0; i < room.openings.Length; i++)
            {
                OpeningModel opening = room.openings[i];

                if (opening == null)
                {
                    continue;
                }

                if (opening.wallStartCornerId == wall.StartCornerId &&
                    opening.wallEndCornerId == wall.EndCornerId)
                {
                    matches.Add(opening);
                }
            }

            return matches;
        }

        private static void ReportUnmatchedOpenings(
            RoomModel room,
            IReadOnlyList<WallDefinition> walls,
            IList<string> diagnostics)
        {
            if (diagnostics == null || room.openings == null)
            {
                return;
            }

            for (int i = 0; i < room.openings.Length; i++)
            {
                OpeningModel opening = room.openings[i];

                if (opening == null)
                {
                    diagnostics.Add($"Opening at index {i} is null; ignored.");
                    continue;
                }

                bool matched = false;

                for (int w = 0; w < walls.Count; w++)
                {
                    if (walls[w].StartCornerId == opening.wallStartCornerId &&
                        walls[w].EndCornerId == opening.wallEndCornerId)
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    diagnostics.Add(
                        $"Opening '{opening.id}' names wall " +
                        $"'{opening.wallStartCornerId}'->'{opening.wallEndCornerId}', which is not a " +
                        "wall of this room (the pair is ordered, so the reverse does not match); ignored.");
                }
            }
        }
    }
}
