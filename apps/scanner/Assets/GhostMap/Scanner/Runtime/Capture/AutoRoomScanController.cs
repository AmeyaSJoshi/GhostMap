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
                ObserveOtherSurfaces();

                if (float.IsNaN(firstVerticalAt) && accumulator.PlaneCount > 0)
                {
                    firstVerticalAt = now;
                }
            }

            if (now - lastSelectAt >= SelectIntervalS)
            {
                lastSelectAt = now;
                LastResult = RoomFromWalls.Select(accumulator.Clusters, ScanCenter);
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

        /// <summary>
        /// Refreshes the wall selection immediately, so Finish uses the freshest walls
        /// rather than the last periodic result.
        /// </summary>
        public RoomFromWallsResult Reselect()
        {
            LastResult = RoomFromWalls.Select(accumulator.Clusters, ScanCenter);
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
            if (now - startedAt >= OfferHelpAfterS)
            {
                State = AutoScanState.NeedHelp;
                Message = "Having trouble? Tap Help GhostMap.";
                return;
            }

            if (LastResult.TrustedWallCount >= 3 && !float.IsNaN(LastResult.MissingBearingDeg))
            {
                State = AutoScanState.MissingWall;
                Message = DescribeBearing(LastResult.MissingBearingDeg - headingDeg);
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

        private string BuildDebug()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(
                "auto: {0} cover {1}% turn {2:F0} deg/s planes {3} walls {4} trusted {5}\n",
                State, CoveragePercent, CurrentTurnRateDegPerS, accumulator.PlaneCount,
                accumulator.Clusters.Count, LastResult.TrustedWallCount);

            for (int i = 0; i < accumulator.Clusters.Count && i < 6; i++)
            {
                WallCluster c = accumulator.Clusters[i];
                sb.AppendFormat(
                    "  wall {0}: width {1:F1} m height {2:F1} m members {3} label {4} trusted {5}\n",
                    i, c.ObservedWidthM, c.MaxHeightM, c.MemberCount, c.HasWallLabel, WallPlaneAccumulator.IsTrusted(c));
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
