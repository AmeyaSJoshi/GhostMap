using System;
using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Geometry;
using GhostMap.Shared.Validation;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GhostMap.Scanner.Capture
{
    /// <summary>What the automatic scan wants the user to do next.</summary>
    public enum AutoScanState
    {
        Idle = 0,
        MoveSlower,
        KeepTurning,
        PointAtWalls,
        MoreDetail,
        MissingWall,

        /// <summary>R2: the turn is done, some walls were found; tap the bottom of each missing one.</summary>
        TapMissingWalls,
        WeakWall,
        Ready,
        NeedHelp
    }

    /// <summary>
    /// The automatic room scan (the hackathon "Scan Room" path).
    ///
    /// <para>The user stands, locks the floor, and turns once. This
    /// controller watches ARKit's planes while they do. It produces
    /// <i>observations</i> only: walls, a ceiling height, door/window planes.
    /// Nothing here is a second room representation. When the scan finishes,
    /// the derived corners go through
    /// <see cref="CornerCaptureController.TryAdoptDerivedCorners"/>, the same
    /// store manual and swept capture use, and everything downstream is
    /// unchanged.</para>
    ///
    /// <para>Guidance is deliberately a handful of plain sentences. Plane ids,
    /// tracking enums and fit failures stay in <see cref="DebugSummary"/>.</para>
    ///
    /// <para>Plain C#. <see cref="Tick"/> takes the clock as a parameter so
    /// the whole thing is testable without a device.</para>
    /// </summary>
    public sealed class AutoRoomScanController
    {
        public const int SectorCount = 12;

        /// <summary>Fraction of the 360 degrees that must have been seen before the room may auto-finish.</summary>
        public const float ReadyCoverage = 0.85f;

        /// <summary>Turning faster than this is too fast for ARKit to find planes well.</summary>
        public const float MaxTurnRateDegPerS = 75f;

        /// <summary>How long the turn must stay too fast before the user is told to slow down.</summary>
        public const float TooFastSeconds = 0.4f;

        /// <summary>How often the wall selection is re-evaluated (it tries up to 35 wall subsets).</summary>
        public const float SelectIntervalS = 0.5f;

        /// <summary>After this long with full coverage and no room, offer help.</summary>
        public const float OfferHelpAfterS = 30f;

        /// <summary>
        /// Task R9: after a full turn that found fewer than three walls, offer
        /// the sweep this soon rather than after <see cref="OfferHelpAfterS"/>:
        /// a second turn rarely finds two missing walls.
        /// </summary>
        public const float FewWallsHelpAfterS = 6f;

        /// <summary>
        /// The observed width given to a wall placed by a tap (R2). Above the
        /// trust bar and the per-wall minimum in a room, so a tapped wall counts.
        /// </summary>
        public const float TappedWallWidthM = 1.0f;

        /// <summary>A tap nearer than this to where the user stands is not a wall.</summary>
        public const float MinTapDistanceM = 0.4f;

        /// <summary>Without any vertical plane for this long, suggest pointing at something detailed.</summary>
        public const float NoDetailAfterS = 8f;

        /// <summary>Largest camera pitch (sine of the angle from horizontal) for a heading to count as coverage.</summary>
        public const float MaxCoverageForwardY = 0.93f;

        public const float MinCeilingHeightM = 2.0f;
        public const float MaxCeilingHeightM = 4.0f;
        public const float MinCeilingAreaM2 = 1.5f;

        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;
        private readonly WallPlaneAccumulator accumulator = new WallPlaneAccumulator();
        private readonly List<DetectedSurface> surfaces = new List<DetectedSurface>();
        private readonly bool[] sectors = new bool[SectorCount];
        private readonly Dictionary<TrackableId, (float y, float area, bool labelled)> ceilingPlanes =
            new Dictionary<TrackableId, (float, float, bool)>();
        private readonly Dictionary<TrackableId, DetectedSurface> openingPlanes =
            new Dictionary<TrackableId, DetectedSurface>();

        private Quaternion previousRotation = Quaternion.identity;
        private float previousTime;
        private bool hasPrevious;
        private float tooFastFor;
        private float startedAt;
        private float lastSelectAt = float.NegativeInfinity;
        private float firstVerticalAt = float.NaN;
        private float coveredAt = float.NaN;
        private readonly List<WallCluster> tappedWalls = new List<WallCluster>();
        private readonly List<WallCluster> allWalls = new List<WallCluster>();
        private Vector2 centerSum;
        private int centerCount;
        private float headingDeg;

        public AutoRoomScanController(ISpatialProvider provider, FloorLockController floorLock)
        {
            this.provider = provider;
            this.floorLock = floorLock;
        }

        public GhostCoordinateFrame Frame => floorLock.Frame;

        public bool IsActive { get; private set; }

        public IReadOnlyList<WallCluster> Walls => accumulator.Clusters;

        public RoomFromWallsResult LastResult { get; private set; } = new RoomFromWallsResult();

        public AutoScanState State { get; private set; }

        public string Message { get; private set; } = string.Empty;

        /// <summary>True when <see cref="Message"/> is something the user should act on.</summary>
        public bool MessageIsWarning { get; private set; }

        public float CurrentTurnRateDegPerS { get; private set; }

        public float CoverageFraction
        {
            get
            {
                int n = 0;

                for (int i = 0; i < SectorCount; i++)
                {
                    if (sectors[i])
                    {
                        n++;
                    }
                }

                return n / (float)SectorCount;
            }
        }

        public int CoveragePercent => Mathf.RoundToInt(CoverageFraction * 100f);

        public bool IsSectorCovered(int sector) => sectors[((sector % SectorCount) + SectorCount) % SectorCount];

        /// <summary>The Ghost-frame heading the camera is facing, in degrees (0 = +Z, 90 = +X).</summary>
        public float HeadingDeg => headingDeg;

        /// <summary>A room is available and the whole turn has been seen.</summary>
        public bool IsReadyToFinish => LastResult.Success && CoverageFraction >= ReadyCoverage;

        /// <summary>The mean camera position in Ghost XZ, the point the walls must enclose.</summary>
        public Vector2 ScanCenter => centerCount == 0 ? Vector2.zero : centerSum / centerCount;

        public string DebugSummary { get; private set; } = string.Empty;

        public void Begin(float now)
        {
            accumulator.Clear();
            ceilingPlanes.Clear();
            RawVerticalCount = 0;
            RawWallLabelledCount = 0;
            LargestRawVerticalWidthM = 0f;
            LargestRawVerticalHeightM = 0f;
            openingPlanes.Clear();
            Array.Clear(sectors, 0, sectors.Length);
            LastResult = new RoomFromWallsResult();
            previousRotation = Quaternion.identity;
            previousTime = now;
            hasPrevious = false;
            tooFastFor = 0f;
            startedAt = now;
            lastSelectAt = float.NegativeInfinity;
            firstVerticalAt = float.NaN;
            coveredAt = float.NaN;
            tappedWalls.Clear();
            LastTapError = string.Empty;
            centerSum = Vector2.zero;
            centerCount = 0;
            CurrentTurnRateDegPerS = 0f;
            IsActive = floorLock.IsLocked;
            State = AutoScanState.PointAtWalls;
            Message = "Slowly turn all the way around.";
            MessageIsWarning = false;
        }

        public void End()
        {
            IsActive = false;
            State = AutoScanState.Idle;
        }

        /// <summary>Feed one frame. Cheap enough to call every Update.</summary>
        public void Tick(float now)
        {
            if (!IsActive || Frame == null)
            {
                return;
            }

            bool tracking = provider.IsTrackingGood;
            bool havePose = provider.TryGetCameraPose(out Pose pose);

            if (havePose)
            {
                TrackTurn(pose, now, tracking);
            }

            if (tracking && provider.TryGetDetectedSurfaces(surfaces))
            {
                accumulator.Update(surfaces, Frame);
                CountRawVerticals();
                ObserveOtherSurfaces();

                if (float.IsNaN(firstVerticalAt) && accumulator.PlaneCount > 0)
                {
                    firstVerticalAt = now;
                }
            }

            if (now - lastSelectAt >= SelectIntervalS)
            {
                lastSelectAt = now;
                LastResult = RoomFromWalls.Select(AllWalls(), ScanCenter);
            }

            UpdateGuidance(tracking, now);
            DebugSummary = BuildDebug();
        }

        private void TrackTurn(Pose pose, float now, bool tracking)
        {
            Vector3 forward = Frame.WorldDirectionToGhost(pose.rotation * Vector3.forward);
            Vector3 position = Frame.WorldToGhost(pose.position);

            if (hasPrevious)
            {
                float dt = now - previousTime;

                if (dt > 1e-4f)
                {
                    CurrentTurnRateDegPerS = Quaternion.Angle(previousRotation, pose.rotation) / dt;
                    tooFastFor = CurrentTurnRateDegPerS > MaxTurnRateDegPerS ? tooFastFor + dt : 0f;
                }
            }

            previousRotation = pose.rotation;
            previousTime = now;
            hasPrevious = true;

            Vector2 flat = new Vector2(forward.x, forward.z);

            if (flat.sqrMagnitude > 1e-4f)
            {
                headingDeg = (Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg + 360f) % 360f;
            }

            if (!tracking)
            {
                return;
            }

            centerSum += new Vector2(position.x, position.z);
            centerCount++;

            if (Mathf.Abs(forward.y) <= MaxCoverageForwardY &&
                CurrentTurnRateDegPerS <= MaxTurnRateDegPerS * 1.5f)
            {
                int sector = Mathf.Clamp(Mathf.FloorToInt(headingDeg / (360f / SectorCount)), 0, SectorCount - 1);
                sectors[sector] = true;
            }
        }

        private void ObserveOtherSurfaces()
        {
            foreach (DetectedSurface s in surfaces)
            {
                if (s.Alignment == PlaneAlignment.Vertical)
                {
                    if ((s.Classifications & (PlaneClassifications.DoorFrame | PlaneClassifications.WindowFrame)) != 0)
                    {
                        openingPlanes[s.Id] = s;
                    }

                    continue;
                }

                if (s.Alignment != PlaneAlignment.HorizontalUp && s.Alignment != PlaneAlignment.HorizontalDown)
                {
                    continue;
                }

                float area = s.ExtentsM.x * s.ExtentsM.y;
                float y = Frame.WorldToGhost(s.WorldCenter).y;
                bool labelled = (s.Classifications & PlaneClassifications.Ceiling) != 0;
                bool furnitureOrFloor = (s.Classifications &
                    (PlaneClassifications.Floor | PlaneClassifications.Table | PlaneClassifications.SeatOfAnyType)) != 0;

                if (furnitureOrFloor || area < MinCeilingAreaM2)
                {
                    continue;
                }

                if (labelled || (y >= MinCeilingHeightM && y <= MaxCeilingHeightM))
                {
                    ceilingPlanes[s.Id] = (y, area, labelled);
                }
            }
        }

        /// <summary>
        /// A ceiling height from ARKit's ceiling plane(s), or false when ARKit
        /// has not seen one. The existing height capture or typed height is
        /// the fallback.
        /// </summary>
        public bool TryGetCeilingHeight(out float heightM)
        {
            heightM = 0f;
            float sum = 0f;
            float weight = 0f;
            bool anyLabelled = false;

            foreach (var c in ceilingPlanes.Values)
            {
                anyLabelled |= c.labelled;
            }

            foreach (var c in ceilingPlanes.Values)
            {
                if (anyLabelled && !c.labelled)
                {
                    continue;
                }

                if (c.y < MinCeilingHeightM || c.y > MaxCeilingHeightM)
                {
                    continue;
                }

                sum += c.y * c.area;
                weight += c.area;
            }

            if (weight <= 0f)
            {
                return false;
            }

            heightM = sum / weight;
            return true;
        }

        /// <summary>Door/window planes ARKit reported, for the workflow to turn into openings.</summary>
        public IReadOnlyCollection<DetectedSurface> OpeningPlanes => openingPlanes.Values;

        /// <summary>
        /// Turns ARKit's door and window planes into openings on the nearest
        /// wall of the finished room. Proposals only: the caller validates and
        /// adopts them through the normal opening store, and drops any that do
        /// not fit.
        /// </summary>
        public List<OpeningModel> ProposeOpenings(IReadOnlyList<WallDefinition> walls, float roomHeightM)
        {
            var proposals = new List<OpeningModel>();

            if (Frame == null || walls == null)
            {
                return proposals;
            }

            foreach (DetectedSurface plane in openingPlanes.Values)
            {
                if (!WallPlaneAccumulator.TryMeasureVertical(plane, Frame, out WallPlaneAccumulator.PlaneMeasure m))
                {
                    continue;
                }

                bool isDoor = (plane.Classifications & PlaneClassifications.DoorFrame) != 0;
                Vector3 center = new Vector3(m.Center.x, 0f, m.Center.y);

                int best = -1;
                float bestDistance = 0.5f;

                for (int i = 0; i < walls.Count; i++)
                {
                    WallDefinition wall = walls[i];
                    WallGeometry.ToWallLocal(wall, center, out float u, out _);

                    if (u < -0.2f || u > wall.LengthM + 0.2f)
                    {
                        continue;
                    }

                    float distance = Mathf.Abs(Vector3.Dot(center - wall.Start, WallGeometry.Normal(wall)));

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                if (best < 0)
                {
                    continue;
                }

                WallDefinition target = walls[best];
                WallGeometry.ToWallLocal(target, center, out float centerU, out _);

                float width = Mathf.Max(m.WidthM, 0.3f);
                float sill = isDoor ? 0f : Mathf.Max(0f, m.CenterY - m.HeightM * 0.5f);
                float top = Mathf.Min(m.CenterY + m.HeightM * 0.5f, roomHeightM);
                float height = isDoor ? Mathf.Min(top, roomHeightM) : top - sill;
                float offset = Mathf.Clamp(centerU - width * 0.5f, 0f, Mathf.Max(0f, target.LengthM - width));

                proposals.Add(new OpeningModel
                {
                    id = Guid.NewGuid().ToString(),
                    type = isDoor ? OpeningValidator.TypeDoor : OpeningValidator.TypeWindow,
                    wallStartCornerId = target.StartCornerId,
                    wallEndCornerId = target.EndCornerId,
                    offsetM = offset,
                    widthM = width,
                    sillHeightM = sill,
                    heightM = height
                });
            }

            return proposals;
        }

        // -------------------------------------------------------------------
        // R2: walls ARKit cannot see, placed by a tap
        // -------------------------------------------------------------------

        /// <summary>Why the last tap was refused; empty after a tap that placed a wall.</summary>
        public string LastTapError { get; private set; } = string.Empty;

        /// <summary>Walls the user placed by tapping their bottom.</summary>
        public IReadOnlyList<WallCluster> TappedWalls => tappedWalls;

        /// <summary>
        /// True once the turn is done, the room is not found, and at least one
        /// wall is trusted to take the missing walls' directions from.
        /// </summary>
        public bool CanAddTappedWall =>
            IsActive && Frame != null && !LastResult.Success
            && CoverageFraction >= ReadyCoverage && ReferenceWall() != null;

        /// <summary>
        /// R2 (first iPhone session): ARKit on a phone without LiDAR found no
        /// plane at all on plain side walls, so no threshold could find them.
        /// The user points the crosshair at the bottom of a missing wall and
        /// taps. Assuming square corners (a four-wall room), the wall runs
        /// parallel or perpendicular to the strongest wall found, whichever
        /// faces the tap; it passes through the tapped floor point. The wall is
        /// the user's measurement, not an invented one (ADR-0012).
        /// </summary>
        public bool TryAddTappedWall(out string error)
        {
            bool added = TryAddTappedWallCore(out error);
            LastTapError = error;
            return added;
        }

        private bool TryAddTappedWallCore(out string error)
        {
            if (!CanAddTappedWall)
            {
                error = "Finish turning around first.";
                return false;
            }

            if (!provider.IsTrackingGood)
            {
                error = "Tracking lost. Move the phone slowly.";
                return false;
            }

            Ray ghostRay = Frame.WorldRayToGhost(provider.GetScreenRay(provider.CenterScreenPoint));
            if (!RayPlaneMath.TryIntersectHorizontalPlane(ghostRay, 0f, out Vector3 hit))
            {
                error = "Point the dot down at the bottom of the wall, where it meets the floor.";
                return false;
            }

            return TryAddTappedWallAt(new Vector2(hit.x, hit.z), out error);
        }

        /// <summary>The same, for a floor point already in Ghost XZ. Tested directly.</summary>
        public bool TryAddTappedWall(Vector2 floorPoint, out string error)
        {
            bool added = TryAddTappedWallAt(floorPoint, out error);
            LastTapError = error;
            return added;
        }

        private bool TryAddTappedWallAt(Vector2 floorPoint, out string error)
        {
            WallCluster reference = ReferenceWall();
            if (reference == null)
            {
                error = "No wall found yet to line the others up with. Tap Help GhostMap.";
                return false;
            }

            Vector2 fromCenter = floorPoint - ScanCenter;
            if (fromCenter.magnitude < MinTapDistanceM)
            {
                error = "That is where you are standing. Point at the bottom of a wall.";
                return false;
            }

            Vector2 n0 = reference.Normal.normalized;
            Vector2 n1 = new Vector2(-n0.y, n0.x);
            Vector2 normal = Mathf.Abs(Vector2.Dot(fromCenter, n0)) >= Mathf.Abs(Vector2.Dot(fromCenter, n1)) ? n0 : n1;
            float offset = Vector2.Dot(normal, floorPoint);

            foreach (WallCluster wall in AllWalls())
            {
                if (!WallPlaneAccumulator.IsTrusted(wall))
                {
                    continue;
                }

                float alignment = Vector2.Dot(normal, wall.Normal);
                if (Mathf.Abs(alignment) < Mathf.Cos(WallPlaneAccumulator.SameWallAngleDeg * Mathf.Deg2Rad))
                {
                    continue;
                }

                // Same side of the scan centre and close: the same wall.
                float wallOffset = wall.Offset * Mathf.Sign(alignment);
                float wallSide = wallOffset - Vector2.Dot(normal, ScanCenter);
                float tapSide = offset - Vector2.Dot(normal, ScanCenter);
                if (Mathf.Sign(wallSide) == Mathf.Sign(tapSide) && Mathf.Abs(wallOffset - offset) < 0.5f)
                {
                    error = "That wall is already found. Point at a missing one.";
                    return false;
                }
            }

            tappedWalls.Add(new WallCluster
            {
                Normal = normal,
                Offset = offset,
                ObservedWidthM = TappedWallWidthM,
                MaxHeightM = 0f,
                HasWallLabel = true,
                MemberCount = 0,
                UserPlaced = true,
                Score = TappedWallWidthM
            });

            Reselect();
            DebugSummary = BuildDebug();
            error = string.Empty;
            return true;
        }

        /// <summary>The highest-scoring trusted wall measured from planes, else a tapped one.</summary>
        private WallCluster ReferenceWall()
        {
            WallCluster best = null;
            foreach (WallCluster wall in accumulator.Clusters)
            {
                if (WallPlaneAccumulator.IsTrusted(wall) && (best == null || wall.Score > best.Score))
                {
                    best = wall;
                }
            }

            return best ?? (tappedWalls.Count > 0 ? tappedWalls[0] : null);
        }

        private List<WallCluster> AllWalls()
        {
            allWalls.Clear();
            allWalls.AddRange(accumulator.Clusters);
            allWalls.AddRange(tappedWalls);
            return allWalls;
        }

        /// <summary>
        /// Refreshes the wall selection immediately, so Finish uses the freshest walls
        /// rather than the last periodic result.
        /// </summary>
        public RoomFromWallsResult Reselect()
        {
            LastResult = RoomFromWalls.Select(AllWalls(), ScanCenter);
            return LastResult;
        }

        private void UpdateGuidance(bool tracking, float now)
        {
            MessageIsWarning = true;

            if (!tracking || tooFastFor >= TooFastSeconds)
            {
                State = AutoScanState.MoveSlower;
                Message = "Move slower.";
                return;
            }

            float coverage = CoverageFraction;

            if (LastResult.Success && coverage >= ReadyCoverage)
            {
                State = AutoScanState.Ready;
                Message = "Room found. Finishing…";
                MessageIsWarning = false;
                return;
            }

            if (coverage < ReadyCoverage)
            {
                State = AutoScanState.KeepTurning;
                Message = "Keep turning.";
                MessageIsWarning = false;
                return;
            }

            // The whole turn has been seen, but the walls do not close into a room.
            if (float.IsNaN(coveredAt))
            {
                coveredAt = now;
            }

            int trusted = LastResult.TrustedWallCount;

            // R2: walls ARKit could not see are placed by a tap at their
            // bottom. With three found, say which way the last one is.
            if (trusted >= 3 && !float.IsNaN(LastResult.MissingBearingDeg))
            {
                State = AutoScanState.MissingWall;
                Message = DescribeBearing(LastResult.MissingBearingDeg - headingDeg) + " Point at its bottom and tap Add Wall.";
                return;
            }

            if (trusted >= 1)
            {
                State = AutoScanState.TapMissingWalls;
                Message = DescribeFewWalls(trusted);
                return;
            }

            if (now - startedAt >= OfferHelpAfterS || now - coveredAt >= FewWallsHelpAfterS)
            {
                State = AutoScanState.NeedHelp;
                Message = DescribeFewWalls(0);
                return;
            }

            if (accumulator.Clusters.Count > 0)
            {
                State = AutoScanState.WeakWall;
                Message = "Hold still and keep this wall in view.";
                return;
            }

            if (float.IsNaN(firstVerticalAt) && now - startedAt >= NoDetailAfterS)
            {
                State = AutoScanState.MoreDetail;
                Message = "Point at an area with more detail.";
                return;
            }

            State = AutoScanState.PointAtWalls;
            Message = "Point the camera at the walls.";
        }

        /// <summary>
        /// Task R9: what to say when a full turn found too few walls to make a
        /// room, pointing to the sweep (Help GhostMap).
        /// </summary>
        public static string DescribeFewWalls(int trustedWalls)
        {
            return trustedWalls <= 0
                ? "No walls found yet. Tap Help GhostMap to trace them by hand."
                : $"Found {trustedWalls} of 4 walls. Point the dot at the bottom of a missing wall and tap Add Wall.";
        }

        /// <summary>Turns a bearing relative to where the camera faces into a short instruction.</summary>
        public static string DescribeBearing(float relativeDeg)
        {
            float r = Mathf.Repeat(relativeDeg + 180f, 360f) - 180f;
            float a = Mathf.Abs(r);

            if (a > 135f)
            {
                return "Look toward the wall behind you.";
            }

            if (a > 45f)
            {
                return r > 0f ? "Look toward the wall on your right." : "Look toward the wall on your left.";
            }

            return "Look at the wall ahead and hold steady.";
        }

        /// <summary>R2 diagnostics: what ARKit reported before any filter.</summary>
        public int RawVerticalCount { get; private set; }

        /// <summary>...how many of those ARKit labelled as a wall.</summary>
        public int RawWallLabelledCount { get; private set; }

        /// <summary>...and the widest one seen this scan, in metres.</summary>
        public float LargestRawVerticalWidthM { get; private set; }

        public float LargestRawVerticalHeightM { get; private set; }

        private void CountRawVerticals()
        {
            int vertical = 0;
            int labelled = 0;

            foreach (DetectedSurface surface in surfaces)
            {
                if (!WallPlaneAccumulator.TryMeasureVertical(surface, Frame, out WallPlaneAccumulator.PlaneMeasure m))
                {
                    continue;
                }

                vertical++;
                labelled += (surface.Classifications & PlaneClassifications.WallFace) != 0 ? 1 : 0;

                if (m.WidthM > LargestRawVerticalWidthM)
                {
                    LargestRawVerticalWidthM = m.WidthM;
                    LargestRawVerticalHeightM = m.HeightM;
                }
            }

            RawVerticalCount = vertical;
            RawWallLabelledCount = labelled;
        }

        /// <summary>
        /// R2 diagnostics: the direction from the scan centre to a wall's line
        /// (Ghost bearing in degrees, 0 = +Z, the camera's heading at floor
        /// lock, 90 = +X) and the perpendicular distance to it.
        /// </summary>
        public static void WallBearing(WallCluster cluster, Vector2 scanCenter, out float bearingDeg, out float distanceM)
        {
            // The wall's line is { p : dot(Normal, p) = Offset }.
            float signed = cluster.Offset - Vector2.Dot(cluster.Normal, scanCenter);
            Vector2 toWall = cluster.Normal * signed;
            distanceM = Mathf.Abs(signed);
            bearingDeg = toWall.sqrMagnitude < 1e-8f
                ? 0f
                : Mathf.Repeat(Mathf.Atan2(toWall.x, toWall.y) * Mathf.Rad2Deg, 360f);
        }

        private string BuildDebug()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(
                "auto: raw vertical {0} (wall-labelled {1}), largest {2:F2} x {3:F2} m\n",
                RawVerticalCount, RawWallLabelledCount, LargestRawVerticalWidthM, LargestRawVerticalHeightM);
            sb.AppendFormat(
                "auto: {0} cover {1}% turn {2:F0} deg/s planes {3} walls {4} trusted {5}\n",
                State, CoveragePercent, CurrentTurnRateDegPerS, accumulator.PlaneCount,
                accumulator.Clusters.Count, LastResult.TrustedWallCount);

            // R2: where each wall is, so a log shows which candidates are the
            // same physical wall, which are opposite, and which are furniture.
            for (int i = 0; i < accumulator.Clusters.Count && i < 8; i++)
            {
                WallCluster c = accumulator.Clusters[i];
                WallBearing(c, ScanCenter, out float bearingDeg, out float distanceM);
                sb.AppendFormat(
                    "  wall {0}: width {1:F1} m height {2:F1} m members {3} label {4} trusted {5} bearing {6:F0} deg dist {7:F2} m\n",
                    i, c.ObservedWidthM, c.MaxHeightM, c.MemberCount, c.HasWallLabel, WallPlaneAccumulator.IsTrusted(c),
                    bearingDeg, distanceM);
            }

            for (int i = 0; i < tappedWalls.Count; i++)
            {
                WallBearing(tappedWalls[i], ScanCenter, out float tapBearing, out float tapDistance);
                sb.AppendFormat("  tapped wall {0}: bearing {1:F0} deg dist {2:F2} m\n", i, tapBearing, tapDistance);
            }

            if (TryGetCeilingHeight(out float h))
            {
                sb.AppendFormat("  ceiling {0:F2} m\n", h);
            }

            if (openingPlanes.Count > 0)
            {
                sb.AppendFormat("  door/window planes {0}\n", openingPlanes.Count);
            }

            if (!LastResult.Success && !string.IsNullOrEmpty(LastResult.Reason))
            {
                sb.Append("  no room: ").Append(LastResult.Reason).Append('\n');
            }

            return sb.ToString();
        }
    }
}
