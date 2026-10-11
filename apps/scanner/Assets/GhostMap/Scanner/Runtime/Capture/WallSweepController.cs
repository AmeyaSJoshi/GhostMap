using System.Collections.Generic;
using GhostMap.Scanner.AR;
using GhostMap.Shared.Geometry;
using UnityEngine;

namespace GhostMap.Scanner.Capture
{
    /// <summary>
    /// Why a sweep operation was refused. <see cref="None"/> means it succeeded.
    /// </summary>
    public enum WallSweepRejection
    {
        None = 0,

        /// <summary>There is no GhostMap frame yet. Lock the floor first.</summary>
        FloorNotLocked,

        /// <summary>The session is not tracking, or reports a not-tracking reason.</summary>
        TrackingNotGood,

        /// <summary>The scan phase does not permit this operation.</summary>
        WrongPhase,

        /// <summary>All four MVP walls are already swept.</summary>
        AllWallsCaptured,

        /// <summary>A sweep operation was asked for while no sweep is active.</summary>
        NoSweepActive,

        /// <summary>A sweep was started while one was already active.</summary>
        SweepAlreadyActive,

        /// <summary>The center-screen ray is parallel to the floor, or aimed away from it.</summary>
        RayMissedFloor,

        /// <summary>
        /// The shared fit refused these samples. See
        /// <see cref="WallSweepController.LastFitRejection"/> and
        /// <see cref="WallSweepController.LastError"/>.
        /// </summary>
        FitRejected,

        /// <summary>
        /// The fit succeeded but its RMS residual exceeds
        /// <see cref="WallSweepController.MaxRmsResidualM"/>: the aim wandered
        /// off the junction, or tracking drifted mid-sweep.
        /// </summary>
        ResidualTooHigh,

        /// <summary>Undo was asked for with no walls swept.</summary>
        NoWallsToUndo,

        /// <summary>
        /// The active sweep hit <see cref="WallSweepController.MaxSamplesPerWall"/>.
        /// Further samples are dropped; the sweep is still completable.
        /// </summary>
        SampleLimitReached,

        /// <summary>Corner derivation needs all four walls.</summary>
        NotFourWalls,

        /// <summary>
        /// Two consecutive walls were too close to parallel to form a corner.
        /// See <see cref="WallSweepController.LastError"/>.
        /// </summary>
        DerivationFailed
    }

    /// <summary>
    /// One accepted wall: its fitted line, plus the capture facts the line
    /// itself cannot know.
    /// </summary>
    public readonly struct SweptWall
    {
        public SweptWall(WallLine line, float maxSampleDistanceM)
        {
            Line = line;
            MaxSampleDistanceM = maxSampleDistanceM;
        }

        public WallLine Line { get; }

        /// <summary>
        /// Distance from the camera to the furthest sample in this sweep, in
        /// meters. This is the lever arm from <c>ADR-0005</c>: aim error at the
        /// floor grows with roughly the square of this value, so a wall swept
        /// from far away is less trustworthy than its residual suggests.
        /// </summary>
        public float MaxSampleDistanceM { get; }
    }

    /// <summary>
    /// ADR-0005: captures the room's four walls by sweeping the center-screen
    /// ray along each wall's floor junction, then derives the four corners by
    /// intersecting consecutive wall lines.
    ///
    /// <para><b>Why this exists.</b> Task S3 requires the user to walk to every
    /// corner and tap once. That is most of the scan time, it cannot capture a
    /// corner hidden behind furniture, and it stakes each corner on a single
    /// sample. Sweeping keeps the user standing in one place, needs only a
    /// visible segment of each junction, and gives every wall tens of samples
    /// to average.</para>
    ///
    /// <para><b>Same arithmetic as S3, different target.</b> Each frame of a
    /// sweep is the identical center-screen-ray-against-the-locked-floor-plane
    /// intersection that corner capture uses, and implementation plan section
    /// 8.4 already states its consequence: the user only needs to aim at the
    /// floor/wall boundary. Nothing here needs AR vertical-plane detection, so
    /// <c>ADR-0004</c> and plan section 1.2 are untouched.</para>
    ///
    /// <para><b>The frame is read, never written</b>, exactly as in S3-S5.</para>
    ///
    /// <para><b>No room rule is reimplemented here.</b> The fit lives in the
    /// shared <see cref="WallFitting"/>, and whether the derived footprint is a
    /// legal room is decided by <c>RoomValidator</c> via
    /// <see cref="CornerCaptureController.TryAdoptDerivedCorners"/>. This class
    /// decides only whether a *sweep* is good enough to become a wall.</para>
    ///
    /// <para>Plain C# rather than a MonoBehaviour, for the same reason as
    /// <see cref="CornerCaptureController"/>: every rule below is testable
    /// without a device.</para>
    /// </summary>
    public sealed class WallSweepController
    {
        /// <summary>One sweep per wall, four walls per room.</summary>
        public const int RequiredWallCount = WallFitting.RequiredWallCount;

        /// <summary>
        /// Maximum RMS perpendicular residual for an accepted wall, in meters.
        ///
        /// <para>Five centimeters is loose enough for a hand-held sweep along a
        /// skirting board and tight enough to catch an aim that drifted onto the
        /// open floor or up the wall. It is a capture-acceptance policy, not a
        /// room rule, which is why it lives here rather than in the shared
        /// package.</para>
        /// </summary>
        public const float MaxRmsResidualM = 0.05f;

        /// <summary>
        /// Minimum distance a sample must be from the previous one to be kept,
        /// in meters.
        ///
        /// <para>Without this, holding the phone still for a second banks tens of
        /// near-identical samples. They would satisfy
        /// <see cref="WallFitting.MinSampleCount"/> without adding any
        /// information, and would drag the fit toward wherever the user paused.
        /// Sample count is only meaningful if the samples are distinct.</para>
        /// </summary>
        public const float MinSampleSpacingM = 0.01f;

        /// <summary>
        /// Hard cap on samples per sweep, so a sweep left running cannot grow
        /// without bound. Far more than any real sweep needs.
        /// </summary>
        public const int MaxSamplesPerWall = 2048;

        /// <summary>
        /// Above this sweep distance the HUD warns, in meters. Not a rejection:
        /// a large room genuinely requires far sweeps, and refusing them would
        /// make it uncapturable. See <see cref="SweptWall.MaxSampleDistanceM"/>.
        /// </summary>
        public const float FarSweepWarningM = 4.0f;

        private readonly ISpatialProvider provider;
        private readonly FloorLockController floorLock;

        private readonly List<SweptWall> walls = new List<SweptWall>(RequiredWallCount);
        private readonly List<Vector3> activeSamples = new List<Vector3>(256);

        private float activeMaxDistanceM;

        /// <summary>
        /// The two active samples currently furthest apart. See
        /// <see cref="ExtendActiveSpan"/>.
        /// </summary>
        private Vector3 spanEndA;
        private Vector3 spanEndB;

        public WallSweepController(ISpatialProvider provider, FloorLockController floorLock)
        {
            this.provider = provider;
            this.floorLock = floorLock;
        }

        /// <summary>The frame Task S2 locked, or null before the floor is locked.</summary>
        public GhostCoordinateFrame Frame => floorLock.Frame;

        /// <summary>
        /// Accepted walls, in sweep order. Order is load bearing: corner
        /// derivation and the room's winding both come from it.
        /// </summary>
        public IReadOnlyList<SweptWall> Walls => walls;

        public int WallCount => walls.Count;

        public bool IsComplete => walls.Count == RequiredWallCount;

        /// <summary>Whether a sweep is currently accumulating samples.</summary>
        public bool IsSweeping { get; private set; }

        /// <summary>Samples accumulated by the active sweep.</summary>
        public int ActiveSampleCount => activeSamples.Count;

        /// <summary>
        /// Swept extent of the active sweep so far, in meters: the straight-line
        /// distance between its two most extreme samples.
        ///
        /// <para>This is a live HUD readout, not the fitted span. It is a chord
        /// rather than a projection onto a direction that does not exist yet,
        /// and for a sweep along a wall the two agree closely.</para>
        /// </summary>
        public float ActiveSpanM { get; private set; }

        /// <summary>Furthest active-sweep sample from the camera, in meters.</summary>
        public float ActiveMaxDistanceM => activeMaxDistanceM;

        /// <summary>Why the most recent user-initiated operation was refused.</summary>
        public WallSweepRejection LastRejection { get; private set; }

        /// <summary>
        /// The shared fit's own reason for the most recent
        /// <see cref="WallSweepRejection.FitRejected"/>.
        /// </summary>
        public WallFitRejection LastFitRejection { get; private set; }

        /// <summary>
        /// A user-facing explanation for the most recent failure. Empty when the
        /// last operation succeeded.
        /// </summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// Whether the sweep control should be enabled. Whether the samples
        /// actually yield a usable wall is only known once the sweep ends.
        /// </summary>
        public bool CanSweep =>
            Frame != null && provider.IsTrackingGood && walls.Count < RequiredWallCount;

        /// <summary>
        /// True when the active sweep already has enough distinct samples and
        /// enough span to stand a chance of fitting. A live "you can let go
        /// now" signal for the HUD, not a guarantee.
        /// </summary>
        public bool ActiveSweepLooksUsable =>
            IsSweeping
            && activeSamples.Count >= WallFitting.MinSampleCount
            && ActiveSpanM >= WallFitting.MinSpanM;

        /// <summary>
        /// True when any accepted wall was swept from further than
        /// <see cref="FarSweepWarningM"/>, so the HUD can say the scan is
        /// accuracy-limited by distance rather than by fit.
        /// </summary>
        public bool HasFarSweptWall
        {
            get
            {
                for (int i = 0; i < walls.Count; i++)
                {
                    if (walls[i].MaxSampleDistanceM > FarSweepWarningM)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // -------------------------------------------------------------------
        // The floor ray
        // -------------------------------------------------------------------

        /// <summary>
        /// Projects the center-screen ray onto the locked floor plane, in
        /// GhostMap coordinates with Y forced to exactly zero, and reports how
        /// far away the hit was.
        ///
        /// <para>Side-effect free, so the HUD can call it every frame to preview
        /// the aim point whether or not a sweep is running.</para>
        ///
        /// <para>Deliberately not delegated to
        /// <see cref="CornerCaptureController.TryProjectCrosshairToFloor"/>: that
        /// method discards the range, which is exactly the number this path
        /// needs for its lever-arm warning. Both are thin wrappers over the same
        /// shared <see cref="RayPlaneMath"/> and
        /// <see cref="GhostCoordinateFrame"/>, which remain the single source of
        /// truth for the arithmetic.</para>
        /// </summary>
        public bool TryProjectCrosshairToFloor(out Vector3 ghostPoint, out float distanceM)
        {
            ghostPoint = Vector3.zero;
            distanceM = 0f;

            GhostCoordinateFrame frame = Frame;

            if (frame == null)
            {
                return false;
            }

            Ray worldRay = provider.GetScreenRay(provider.CenterScreenPoint);

            if (!RayPlaneMath.TryIntersectHorizontalPlane(
                    worldRay, frame.FloorWorldY, out Vector3 worldPoint))
            {
                return false;
            }

            Vector3 ghost = frame.WorldToGhost(worldPoint);

            ghostPoint = new Vector3(ghost.x, 0f, ghost.z);
            distanceM = Vector3.Distance(worldRay.origin, worldPoint);
            return true;
        }

        // -------------------------------------------------------------------
        // Sweeping
        // -------------------------------------------------------------------

        /// <summary>
        /// Starts accumulating samples for the next wall.
        /// </summary>
        public bool TryBeginSweep(out WallSweepRejection rejection)
        {
            LastError = string.Empty;

            if (IsSweeping)
            {
                rejection = Fail(WallSweepRejection.SweepAlreadyActive);
                return false;
            }

            if (Frame == null)
            {
                rejection = Fail(WallSweepRejection.FloorNotLocked);
                return false;
            }

            if (!provider.IsTrackingGood)
            {
                rejection = Fail(WallSweepRejection.TrackingNotGood);
                return false;
            }

            if (walls.Count >= RequiredWallCount)
            {
                rejection = Fail(WallSweepRejection.AllWallsCaptured);
                return false;
            }

            ResetActiveSweep();
            IsSweeping = true;

            rejection = Succeed();
            return true;
        }

        /// <summary>
        /// Accumulates one sample from the current crosshair position. Called
        /// once per frame while the sweep control is held.
        ///
        /// <para><b>Deliberately does not touch <see cref="LastRejection"/> or
        /// <see cref="LastError"/>.</b> Those report user-initiated operations.
        /// A sweep that crosses a doorway or passes over the ceiling misses the
        /// floor for a run of frames, which is normal and must not leave a
        /// sticky error on screen.</para>
        ///
        /// <para>Also publishes nothing. Performance target section 24 forbids
        /// per-frame snapshot generation, and a sweep is not a structural
        /// mutation until it is accepted as a wall.</para>
        /// </summary>
        /// <returns>True when a sample was appended by this call.</returns>
        public bool TryAddSample(out WallSweepRejection rejection)
        {
            if (!IsSweeping)
            {
                rejection = WallSweepRejection.NoSweepActive;
                return false;
            }

            if (Frame == null)
            {
                rejection = WallSweepRejection.FloorNotLocked;
                return false;
            }

            // Tracking is checked per sample, not just at sweep start. A sweep
            // runs for seconds, and a relocalization partway through would
            // otherwise contribute samples in a frame that has silently moved.
            if (!provider.IsTrackingGood)
            {
                rejection = WallSweepRejection.TrackingNotGood;
                return false;
            }

            if (activeSamples.Count >= MaxSamplesPerWall)
            {
                rejection = WallSweepRejection.SampleLimitReached;
                return false;
            }

            if (!TryProjectCrosshairToFloor(out Vector3 ghost, out float distanceM))
            {
                rejection = WallSweepRejection.RayMissedFloor;
                return false;
            }

            // Drop a sample that has barely moved, so a stationary phone cannot
            // inflate the sample count without adding information. This is not
            // a failure, so the rejection stays None — the caller just learns
            // that nothing was appended.
            if (activeSamples.Count > 0)
            {
                Vector3 previous = activeSamples[activeSamples.Count - 1];

                if (Vector3.Distance(previous, ghost) < MinSampleSpacingM)
                {
                    rejection = WallSweepRejection.None;
                    return false;
                }
            }

            activeSamples.Add(ghost);

            if (distanceM > activeMaxDistanceM)
            {
                activeMaxDistanceM = distanceM;
            }

            ExtendActiveSpan(ghost);

            rejection = WallSweepRejection.None;
            return true;
        }

        /// <summary>
        /// Ends the active sweep and, if the samples fit a line well enough,
        /// accepts it as the next wall.
        ///
        /// <para>A refused sweep is discarded rather than kept half-accepted:
        /// the user re-sweeps the same wall. Nothing partial is ever stored,
        /// because a wall that is not trusted must not be able to reach corner
        /// derivation.</para>
        /// </summary>
        public bool TryCompleteSweep(out WallSweepRejection rejection)
        {
            LastError = string.Empty;

            if (!IsSweeping)
            {
                rejection = Fail(WallSweepRejection.NoSweepActive);
                return false;
            }

            if (walls.Count >= RequiredWallCount)
            {
                EndSweep();
                rejection = Fail(WallSweepRejection.AllWallsCaptured);
                return false;
            }

            if (!WallFitting.TryFitWallLine(
                    activeSamples, out WallLine line, out WallFitRejection fitRejection))
            {
                LastFitRejection = fitRejection;
                LastError = DescribeFitRejection(fitRejection);
                EndSweep();
                rejection = Fail(WallSweepRejection.FitRejected);
                return false;
            }

            LastFitRejection = WallFitRejection.None;

            if (line.RmsResidualM > MaxRmsResidualM)
            {
                LastError =
                    $"Sweep wandered {line.RmsResidualM:F3} m off a straight line " +
                    $"(limit {MaxRmsResidualM:F2} m). Follow the floor/wall join more closely.";
                EndSweep();
                rejection = Fail(WallSweepRejection.ResidualTooHigh);
                return false;
            }

            walls.Add(new SweptWall(line, activeMaxDistanceM));
            EndSweep();

            rejection = Succeed();
            return true;
        }

        /// <summary>
        /// Abandons the active sweep without accepting a wall. Samples are
        /// discarded.
        /// </summary>
        public bool CancelSweep()
        {
            if (!IsSweeping)
            {
                LastRejection = WallSweepRejection.NoSweepActive;
                return false;
            }

            EndSweep();
            LastRejection = WallSweepRejection.None;
            LastError = string.Empty;
            return true;
        }

        /// <summary>
        /// Removes the most recently accepted wall, so the user can re-sweep it.
        /// </summary>
        public bool TryUndoLastWall(out WallSweepRejection rejection)
        {
            LastError = string.Empty;

            if (walls.Count == 0)
            {
                rejection = Fail(WallSweepRejection.NoWallsToUndo);
                return false;
            }

            walls.RemoveAt(walls.Count - 1);

            rejection = Succeed();
            return true;
        }

        /// <summary>
        /// Discards every swept wall and any active sweep, so the user can
        /// restart. The locked frame is untouched — the room is being
        /// re-measured, not re-anchored.
        /// </summary>
        public void ClearWalls()
        {
            walls.Clear();
            EndSweep();
            LastRejection = WallSweepRejection.None;
            LastFitRejection = WallFitRejection.None;
            LastError = string.Empty;
        }

        // -------------------------------------------------------------------
        // Corner derivation
        // -------------------------------------------------------------------

        /// <summary>
        /// Derives the four room corners from the four swept walls.
        ///
        /// <para>Purely geometric. It does not decide whether the result is a
        /// legal room: <c>RoomValidator</c> does, through
        /// <see cref="CornerCaptureController.TryAdoptDerivedCorners"/>.</para>
        /// </summary>
        public bool TryDeriveCorners(out Vector3[] corners, out WallSweepRejection rejection)
        {
            corners = null;
            LastError = string.Empty;

            if (walls.Count != RequiredWallCount)
            {
                rejection = Fail(WallSweepRejection.NotFourWalls);
                LastError =
                    $"Sweep all {RequiredWallCount} walls first " +
                    $"({walls.Count} done).";
                return false;
            }

            var lines = new WallLine[RequiredWallCount];

            for (int i = 0; i < RequiredWallCount; i++)
            {
                lines[i] = walls[i].Line;
            }

            if (!WallFitting.TryDeriveCorners(lines, out corners, out string error))
            {
                LastError = error;
                rejection = Fail(WallSweepRejection.DerivationFailed);
                return false;
            }

            rejection = Succeed();
            return true;
        }

        // -------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------

        private void EndSweep()
        {
            IsSweeping = false;
            ResetActiveSweep();
        }

        private void ResetActiveSweep()
        {
            activeSamples.Clear();
            activeMaxDistanceM = 0f;
            ActiveSpanM = 0f;
            spanEndA = Vector3.zero;
            spanEndB = Vector3.zero;
        }

        /// <summary>
        /// Extends the running span with one new sample, in constant time, by
        /// keeping the two samples currently furthest apart and testing the new
        /// one against both.
        ///
        /// <para>Rescanning every pair would be quadratic per sample and so
        /// cubic across a sweep, which at <see cref="MaxSamplesPerWall"/> is
        /// not affordable in a per-frame path. For samples lying along a wall —
        /// which is the only case this runs on — the pair furthest apart is
        /// always a pair of endpoints, so maintaining it incrementally is exact
        /// rather than approximate.</para>
        /// </summary>
        private void ExtendActiveSpan(Vector3 sample)
        {
            if (activeSamples.Count < 2)
            {
                spanEndA = activeSamples[0];
                spanEndB = activeSamples[0];
                ActiveSpanM = 0f;
                return;
            }

            float toA = Vector3.Distance(sample, spanEndA);
            float toB = Vector3.Distance(sample, spanEndB);

            if (toA > ActiveSpanM && toA >= toB)
            {
                spanEndB = sample;
                ActiveSpanM = toA;
                return;
            }

            if (toB > ActiveSpanM)
            {
                spanEndA = sample;
                ActiveSpanM = toB;
            }
        }

        private WallSweepRejection Fail(WallSweepRejection rejection)
        {
            LastRejection = rejection;
            return rejection;
        }

        private WallSweepRejection Succeed()
        {
            LastRejection = WallSweepRejection.None;
            return WallSweepRejection.None;
        }

        private static string DescribeFitRejection(WallFitRejection rejection)
        {
            switch (rejection)
            {
                case WallFitRejection.TooFewSamples:
                    return
                        $"Not enough of the wall was swept " +
                        $"(need {WallFitting.MinSampleCount} distinct points). " +
                        "Sweep more slowly along the floor/wall join.";

                case WallFitRejection.SpanTooShort:
                    return
                        $"Swept less than {WallFitting.MinSpanM:F2} m of wall. " +
                        "A short sweep cannot pin down the wall's angle.";

                case WallFitRejection.Degenerate:
                    return "The sweep did not move along a wall. Pan across the floor/wall join.";

                case WallFitRejection.NonFiniteSample:
                    return "Tracking produced an invalid point mid-sweep. Re-sweep this wall.";

                default:
                    return "The sweep could not be fitted to a wall.";
            }
        }
    }
}
