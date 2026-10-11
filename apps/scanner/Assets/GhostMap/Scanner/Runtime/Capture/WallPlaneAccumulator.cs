using System;
using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Shared.Geometry;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// One physical wall, as the union of every ARKit vertical plane that lies
    /// on the same infinite vertical plane, expressed in Ghost top-down
    /// X/Z coordinates.
    /// </summary>
    public sealed class WallCluster
    {
        /// <summary>Unit normal in Ghost XZ (x, z). Sign is arbitrary.</summary>
        public Vector2 Normal;

        /// <summary><c>dot(Normal, p)</c> for any point <c>p</c> on the wall line.</summary>
        public float Offset;

        /// <summary>Observed wall length along the line, with overlaps merged, in meters.</summary>
        public float ObservedWidthM;

        /// <summary>The tallest vertical extent seen among members, in meters.</summary>
        public float MaxHeightM;

        /// <summary>True when ARKit labelled at least one member as a wall.</summary>
        public bool HasWallLabel;

        /// <summary>Number of distinct ARKit planes merged into this wall.</summary>
        public int MemberCount;

        /// <summary>Total scan updates that fed this wall, a proxy for persistence.</summary>
        public int UpdateCount;

        /// <summary>
        /// Relative strength used to rank walls. Wall-labelled evidence counts
        /// more than an unlabelled large vertical plane. Never shown to the user.
        /// </summary>
        public float Score;

        /// <summary>Unit direction along the wall in Ghost XZ.</summary>
        public Vector2 Tangent => new Vector2(-Normal.y, Normal.x);

        /// <summary>The point of the line nearest the Ghost origin.</summary>
        public Vector2 NearestPointToOrigin => Normal * Offset;

        public float SignedDistance(Vector2 point) => Vector2.Dot(Normal, point) - Offset;

        public WallLine ToWallLine()
        {
            Vector2 p = NearestPointToOrigin;
            Vector2 t = Tangent;

            return new WallLine(
                new Vector3(p.x, 0f, p.y),
                new Vector3(t.x, 0f, t.y),
                0f,
                ObservedWidthM,
                Mathf.Max(UpdateCount, 1));
        }
    }

    /// <summary>
    /// Remembers every vertical ARKit plane seen during the room scan and
    /// merges the ones that are the same physical wall.
    ///
    /// <para>ARKit splits, grows, and subsumes planes while it scans, so a
    /// single wall is routinely reported as several planes over time. A
    /// plane's last known state is kept even after ARKit stops reporting it,
    /// because a plane disappears when a larger one swallows it, and the larger
    /// one covers it.</para>
    ///
    /// <para>Conceptually, two planes are the same wall when their normals
    /// agree, and their centers lie on the same line. Tolerances are fixed and
    /// deliberately simple; they are not tuned per room.</para>
    ///
    /// <para>Plain C#: no Unity scene access, so it is tested off-device.</para>
    /// </summary>
    public sealed class WallPlaneAccumulator
    {
        /// <summary>How far from vertical a plane's normal may tip, as the normal's Y component.</summary>
        public const float MaxNormalTilt = 0.30f;

        /// <summary>Two planes are the same wall only if their normals are within this angle.</summary>
        public const float SameWallAngleDeg = 12f;

        /// <summary>...and their centers are within this distance of the same line.</summary>
        public const float SameWallOffsetM = 0.30f;

        /// <summary>An unlabelled vertical plane must be at least this wide to count as wall evidence.</summary>
        public const float MinUnlabelledWidthM = 1.5f;

        /// <summary>...and at least this tall (a monitor or cabinet side is smaller).</summary>
        public const float MinUnlabelledHeightM = 1.2f;

        /// <summary>A wall-labelled plane must be at least this wide.</summary>
        public const float MinLabelledWidthM = 0.6f;

        /// <summary>A cluster with at least this much merged width, and a wall label, is trusted.</summary>
        public const float TrustedLabelledWidthM = 1.0f;

        /// <summary>A cluster with no wall label needs this much merged width to be trusted.</summary>
        public const float TrustedUnlabelledWidthM = 2.5f;

        private sealed class Observation
        {
            public TrackableId Id;
            public Vector2 Center;     // Ghost XZ
            public float CenterY;
            public Vector2 Normal;     // Ghost XZ, unit
            public float WidthM;
            public float HeightM;
            public bool WallLabel;
            public int Updates;
            public float Weight;
        }

        private readonly Dictionary<TrackableId, Observation> observations =
            new Dictionary<TrackableId, Observation>();

        private readonly List<WallCluster> clusters = new List<WallCluster>();

        /// <summary>Latest merged walls, strongest first.</summary>
        public IReadOnlyList<WallCluster> Clusters => clusters;

        public int PlaneCount => observations.Count;

        public void Clear()
        {
            observations.Clear();
            clusters.Clear();
        }

        /// <summary>
        /// Folds the currently detected planes into the accumulated walls.
        /// </summary>
        /// <returns>True when the set of walls may have changed.</returns>
        public bool Update(IReadOnlyList<DetectedSurface> surfaces, GhostCoordinateFrame frame)
        {
            if (surfaces == null || frame == null)
            {
                return false;
            }

            bool any = false;

            for (int i = 0; i < surfaces.Count; i++)
            {
                if (TryObserve(surfaces[i], frame))
                {
                    any = true;
                }
            }

            if (any)
            {
                Recluster();
            }

            return any;
        }

        /// <summary>A vertical plane measured in the Ghost frame.</summary>
        internal readonly struct PlaneMeasure
        {
            public PlaneMeasure(Vector2 center, float centerY, Vector2 normal, float widthM, float heightM)
            {
                Center = center;
                CenterY = centerY;
                Normal = normal;
                WidthM = widthM;
                HeightM = heightM;
            }

            public Vector2 Center { get; }
            public float CenterY { get; }
            public Vector2 Normal { get; }
            public float WidthM { get; }
            public float HeightM { get; }
        }

        /// <summary>
        /// Measures a vertical plane's horizontal width and vertical extent in
        /// the Ghost frame. A plane's local X and Z span the surface, and which
        /// one is horizontal is not guaranteed, so both are projected rather
        /// than assumed.
        /// </summary>
        internal static bool TryMeasureVertical(DetectedSurface surface, GhostCoordinateFrame frame, out PlaneMeasure measure)
        {
            measure = default;

            if (surface.Alignment != PlaneAlignment.Vertical)
            {
                return false;
            }

            Quaternion rotation = surface.WorldRotation;
            Vector3 normalGhost = frame.WorldDirectionToGhost(rotation * Vector3.up);

            if (Mathf.Abs(normalGhost.y) > MaxNormalTilt)
            {
                return false;
            }

            var normal = new Vector2(normalGhost.x, normalGhost.z);

            if (normal.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            normal.Normalize();
            var tangent = new Vector2(-normal.y, normal.x);

            Vector3 axisX = frame.WorldDirectionToGhost(rotation * Vector3.right);
            Vector3 axisZ = frame.WorldDirectionToGhost(rotation * Vector3.forward);
            float halfX = surface.ExtentsM.x * 0.5f;
            float halfZ = surface.ExtentsM.y * 0.5f;

            float widthM = 2f * (
                Mathf.Abs(Vector2.Dot(new Vector2(axisX.x, axisX.z), tangent)) * halfX +
                Mathf.Abs(Vector2.Dot(new Vector2(axisZ.x, axisZ.z), tangent)) * halfZ);

            float heightM = 2f * (Mathf.Abs(axisX.y) * halfX + Mathf.Abs(axisZ.y) * halfZ);

            Vector3 centerGhost = frame.WorldToGhost(surface.WorldCenter);
            measure = new PlaneMeasure(new Vector2(centerGhost.x, centerGhost.z), centerGhost.y, normal, widthM, heightM);
            return true;
        }

        private bool TryObserve(DetectedSurface surface, GhostCoordinateFrame frame)
        {
            // Doors and windows are cut into walls; they are not walls. Floors,
            // tables and seats are never vertical walls either.
            if ((surface.Classifications & (PlaneClassifications.DoorFrame | PlaneClassifications.WindowFrame
                                            | PlaneClassifications.Floor | PlaneClassifications.Ceiling
                                            | PlaneClassifications.Table | PlaneClassifications.SeatOfAnyType)) != 0)
            {
                return false;
            }

            if (!TryMeasureVertical(surface, frame, out PlaneMeasure m))
            {
                return false;
            }

            bool wallLabel = (surface.Classifications & PlaneClassifications.WallFace) != 0;

            if (wallLabel)
            {
                if (m.WidthM < MinLabelledWidthM)
                {
                    return false;
                }
            }
            else if (m.WidthM < MinUnlabelledWidthM || m.HeightM < MinUnlabelledHeightM)
            {
                return false;
            }

            if (!observations.TryGetValue(surface.Id, out Observation obs))
            {
                obs = new Observation { Id = surface.Id };
                observations[surface.Id] = obs;
            }

            obs.Center = m.Center;
            obs.CenterY = m.CenterY;
            obs.Normal = m.Normal;
            obs.WidthM = m.WidthM;
            obs.HeightM = m.HeightM;
            obs.WallLabel = obs.WallLabel || wallLabel;
            obs.Updates++;
            obs.Weight = m.WidthM * Mathf.Max(m.HeightM, 0.5f) * (obs.WallLabel ? 2f : 1f);

            return true;
        }

        private void Recluster()
        {
            var ordered = new List<Observation>(observations.Values);
            ordered.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            var members = new List<List<Observation>>();
            var lines = new List<(Vector2 normal, float offset)>();

            foreach (Observation obs in ordered)
            {
                int best = -1;
                float bestDelta = float.MaxValue;

                for (int k = 0; k < lines.Count; k++)
                {
                    if (!SameWall(obs, lines[k].normal, lines[k].offset, out float delta))
                    {
                        continue;
                    }

                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        best = k;
                    }
                }

                if (best < 0)
                {
                    members.Add(new List<Observation> { obs });
                    lines.Add(FitLine(members[members.Count - 1]));
                }
                else
                {
                    members[best].Add(obs);
                    lines[best] = FitLine(members[best]);
                }
            }

            clusters.Clear();

            for (int k = 0; k < members.Count; k++)
            {
                clusters.Add(BuildCluster(members[k], lines[k].normal, lines[k].offset));
            }

            clusters.Sort((a, b) => b.Score.CompareTo(a.Score));
        }

        private static bool SameWall(Observation obs, Vector2 clusterNormal, float clusterOffset, out float offsetDelta)
        {
            float cos = Mathf.Abs(Vector2.Dot(obs.Normal, clusterNormal));
            float angle = Mathf.Acos(Mathf.Clamp01(cos)) * Mathf.Rad2Deg;
            offsetDelta = Mathf.Abs(Vector2.Dot(clusterNormal, obs.Center) - clusterOffset);

            return angle <= SameWallAngleDeg && offsetDelta <= SameWallOffsetM;
        }

        /// <summary>
        /// Weighted line through the members: the normal is the weighted mean
        /// of the doubled angle (a line's normal has no sign, so plain
        /// averaging would cancel opposite normals), and the offset is the
        /// weighted mean center distance along that normal.
        /// </summary>
        private static (Vector2 normal, float offset) FitLine(List<Observation> group)
        {
            float sumCos = 0f;
            float sumSin = 0f;
            float sumW = 0f;

            foreach (Observation o in group)
            {
                float angle = Mathf.Atan2(o.Normal.y, o.Normal.x);
                sumCos += Mathf.Cos(2f * angle) * o.Weight;
                sumSin += Mathf.Sin(2f * angle) * o.Weight;
                sumW += o.Weight;
            }

            float mean = 0.5f * Mathf.Atan2(sumSin, sumCos);
            var normal = new Vector2(Mathf.Cos(mean), Mathf.Sin(mean));

            float offset = 0f;

            foreach (Observation o in group)
            {
                offset += Vector2.Dot(normal, o.Center) * o.Weight;
            }

            return (normal, sumW > 0f ? offset / sumW : 0f);
        }

        private static WallCluster BuildCluster(List<Observation> group, Vector2 normal, float offset)
        {
            var tangent = new Vector2(-normal.y, normal.x);
            var intervals = new List<(float lo, float hi)>();
            bool label = false;
            float maxHeight = 0f;
            int updates = 0;

            foreach (Observation o in group)
            {
                float s = Vector2.Dot(tangent, o.Center);
                intervals.Add((s - o.WidthM * 0.5f, s + o.WidthM * 0.5f));
                label |= o.WallLabel;
                maxHeight = Mathf.Max(maxHeight, o.HeightM);
                updates += o.Updates;
            }

            float width = UnionLength(intervals);
            bool trustedByLabel = label && width >= TrustedLabelledWidthM;

            return new WallCluster
            {
                Normal = normal,
                Offset = offset,
                ObservedWidthM = width,
                MaxHeightM = maxHeight,
                HasWallLabel = label,
                MemberCount = group.Count,
                UpdateCount = updates,
                Score = width * (label ? 1.5f : 1f) * (trustedByLabel ? 1.2f : 1f)
            };
        }

        public static bool IsTrusted(WallCluster cluster)
        {
            return cluster != null &&
                   (cluster.HasWallLabel
                       ? cluster.ObservedWidthM >= TrustedLabelledWidthM
                       : cluster.ObservedWidthM >= TrustedUnlabelledWidthM);
        }

        internal static float UnionLength(List<(float lo, float hi)> intervals)
        {
            if (intervals.Count == 0)
            {
                return 0f;
            }

            intervals.Sort((a, b) => a.lo.CompareTo(b.lo));

            float total = 0f;
            float lo = intervals[0].lo;
            float hi = intervals[0].hi;

            for (int i = 1; i < intervals.Count; i++)
            {
                if (intervals[i].lo > hi)
                {
                    total += hi - lo;
                    lo = intervals[i].lo;
                    hi = intervals[i].hi;
                }
                else
                {
                    hi = Mathf.Max(hi, intervals[i].hi);
                }
            }

            return total + (hi - lo);
        }
    }
}
