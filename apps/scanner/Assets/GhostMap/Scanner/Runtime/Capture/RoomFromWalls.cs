using System;
using System.Collections.Generic;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;

namespace GhostMap.Scanner.Capture
{
    /// <summary>The outcome of trying to find a room among accumulated walls.</summary>
    public sealed class RoomFromWallsResult
    {
        /// <summary>Four walls, in order around the room, or null.</summary>
        public WallCluster[] Walls;

        /// <summary>Four corners, <c>corner[i]</c> = wall[i-1] ∩ wall[i], or null.</summary>
        public Vector3[] Corners;

        /// <summary>Number of trusted walls found, whether or not they made a room.</summary>
        public int TrustedWallCount;

        /// <summary>
        /// When walls were found but did not close into a room: the Ghost-frame
        /// bearing (degrees, 0 = +Z, 90 = +X) from the scan center of the
        /// largest unseen stretch. NaN when not applicable.
        /// </summary>
        public float MissingBearingDeg = float.NaN;

        /// <summary>Why no room was built. Empty on success.</summary>
        public string Reason = string.Empty;

        public bool Success => Corners != null;
    }

    /// <summary>
    /// Chooses the best four walls among the accumulated wall clusters and
    /// intersects adjacent ones into the four corners the existing room store
    /// expects.
    ///
    /// <para>It never fabricates a wall: with three trustworthy walls it
    /// returns no room and the bearing of the gap, so the user can be told
    /// where to look.</para>
    ///
    /// <para>Corner derivation and validation are the existing shared code
    /// (<see cref="WallFitting.TryDeriveCorners"/> and
    /// <see cref="RoomValidator.ValidateRoom"/>), unchanged.</para>
    /// </summary>
    public static class RoomFromWalls
    {
        public const int MaxCandidates = 7;

        /// <summary>The scan center must have walls on all sides: no gap wider than this.</summary>
        public const float MaxAngularGapDeg = 170f;

        /// <summary>The scan center must not sit on a wall.</summary>
        public const float MinCenterToWallM = 0.3f;

        /// <summary>Every chosen wall must have at least this much observed length.</summary>
        public const float MinObservedPerWallM = 0.8f;

        /// <summary>Observed wall length must cover at least this fraction of the room's perimeter.</summary>
        public const float MinCoverageFraction = 0.25f;

        public static RoomFromWallsResult Select(IReadOnlyList<WallCluster> clusters, Vector2 center)
        {
            var result = new RoomFromWallsResult();
            var trusted = new List<WallCluster>();

            if (clusters != null)
            {
                foreach (WallCluster c in clusters)
                {
                    if (WallPlaneAccumulator.IsTrusted(c))
                    {
                        trusted.Add(c);
                    }
                }
            }

            trusted.Sort((a, b) => b.Score.CompareTo(a.Score));

            if (trusted.Count > MaxCandidates)
            {
                trusted.RemoveRange(MaxCandidates, trusted.Count - MaxCandidates);
            }

            result.TrustedWallCount = trusted.Count;

            if (trusted.Count < 3)
            {
                result.Reason = "Fewer than three trustworthy walls.";
                return result;
            }

            if (trusted.Count >= 4)
            {
                float bestScore = float.NegativeInfinity;
                WallCluster[] bestWalls = null;
                Vector3[] bestCorners = null;
                var pick = new int[4];

                for (pick[0] = 0; pick[0] < trusted.Count - 3; pick[0]++)
                for (pick[1] = pick[0] + 1; pick[1] < trusted.Count - 2; pick[1]++)
                for (pick[2] = pick[1] + 1; pick[2] < trusted.Count - 1; pick[2]++)
                for (pick[3] = pick[2] + 1; pick[3] < trusted.Count; pick[3]++)
                {
                    var subset = new[]
                    {
                        trusted[pick[0]], trusted[pick[1]], trusted[pick[2]], trusted[pick[3]]
                    };

                    if (TryBuild(subset, center, out WallCluster[] ordered, out Vector3[] corners, out float score)
                        && score > bestScore)
                    {
                        bestScore = score;
                        bestWalls = ordered;
                        bestCorners = corners;
                    }
                }

                if (bestCorners != null)
                {
                    result.Walls = bestWalls;
                    result.Corners = bestCorners;
                    return result;
                }

                result.Reason = "No four walls close into a valid room yet.";
            }
            else
            {
                result.Reason = "Only three trustworthy walls.";
            }

            result.MissingBearingDeg = LargestGapBearing(trusted, center);
            return result;
        }

        /// <summary>
        /// Orders four walls by the bearing from the scan center to each, builds
        /// the corners, and applies every gate. Returns false for a subset that
        /// is not a legal, enclosing room.
        /// </summary>
        public static bool TryBuild(
            WallCluster[] subset,
            Vector2 center,
            out WallCluster[] ordered,
            out Vector3[] corners,
            out float score)
        {
            ordered = null;
            corners = null;
            score = 0f;

            var bearings = new float[4];

            for (int i = 0; i < 4; i++)
            {
                float distance = subset[i].SignedDistance(center);

                if (Mathf.Abs(distance) < MinCenterToWallM)
                {
                    return false;
                }

                Vector2 toWall = distance > 0f ? -subset[i].Normal : subset[i].Normal;
                bearings[i] = Mathf.Atan2(toWall.x, toWall.y) * Mathf.Rad2Deg;
            }

            var order = new[] { 0, 1, 2, 3 };
            Array.Sort(order, (a, b) => bearings[a].CompareTo(bearings[b]));

            for (int i = 0; i < 4; i++)
            {
                float gap = bearings[order[(i + 1) % 4]] - bearings[order[i]];

                if (gap <= 0f)
                {
                    gap += 360f;
                }

                if (gap > MaxAngularGapDeg)
                {
                    return false;
                }
            }

            var walls = new WallCluster[4];
            var lines = new WallLine[4];

            for (int i = 0; i < 4; i++)
            {
                walls[i] = subset[order[i]];
                lines[i] = walls[i].ToWallLine();
            }

            if (!WallFitting.TryDeriveCorners(lines, out Vector3[] derived, out _))
            {
                return false;
            }

            var candidates = new CornerModel[4];

            for (int i = 0; i < 4; i++)
            {
                candidates[i] = new CornerModel
                {
                    id = "candidate" + i,
                    position = new Vec3Dto(derived[i].x, 0f, derived[i].z)
                };
            }

            var room = new RoomModel
            {
                id = "candidate",
                name = "Room",
                heightM = 0f,
                corners = candidates,
                openings = Array.Empty<OpeningModel>(),
                objects = Array.Empty<SceneObjectModel>()
            };

            if (!RoomValidator.ValidateRoom(room).IsValid)
            {
                return false;
            }

            if (!RoomGeometry.ContainsPointXZ(derived, new Vector3(center.x, 0f, center.y)))
            {
                return false;
            }

            // Support: how much of each room edge lies on observed wall.
            float observed = 0f;
            float perimeter = 0f;

            for (int i = 0; i < 4; i++)
            {
                Vector3 a = derived[i];
                Vector3 b = derived[(i + 1) % 4];
                float length = Vector3.Distance(a, b);

                // Wall i spans corner[i] -> corner[i + 1].
                if (walls[i].ObservedWidthM < MinObservedPerWallM)
                {
                    return false;
                }

                observed += Mathf.Min(walls[i].ObservedWidthM, length);
                perimeter += length;
            }

            float coverage = observed / Mathf.Max(perimeter, 0.01f);

            if (coverage < MinCoverageFraction)
            {
                return false;
            }

            // Reward observed length, and reward it more when the wall is
            // actually seen along its whole edge, so a far, barely-seen
            // plane cannot beat a smaller room that is well observed.
            score = observed * coverage;
            ordered = walls;
            corners = derived;
            return true;
        }

        private static float LargestGapBearing(List<WallCluster> walls, Vector2 center)
        {
            var bearings = new List<float>();

            foreach (WallCluster w in walls)
            {
                float distance = w.SignedDistance(center);
                Vector2 toWall = distance > 0f ? -w.Normal : w.Normal;
                float bearing = Mathf.Atan2(toWall.x, toWall.y) * Mathf.Rad2Deg;
                bearings.Add((bearing + 360f) % 360f);
            }

            bearings.Sort();

            float bestGap = -1f;
            float bestCenter = float.NaN;

            for (int i = 0; i < bearings.Count; i++)
            {
                float from = bearings[i];
                float to = bearings[(i + 1) % bearings.Count];
                float gap = to - from;

                if (gap <= 0f)
                {
                    gap += 360f;
                }

                if (gap > bestGap)
                {
                    bestGap = gap;
                    bestCenter = (from + gap * 0.5f) % 360f;
                }
            }

            return bestCenter;
        }
    }
}
