using UnityEngine;

namespace GhostMap.Viewer.Interaction
{
    /// <summary>
    /// Task V4: the orbit camera's state and maths — a spherical camera around
    /// a target point, with every clamp implementation plan section 13.1 asks
    /// for ("do not let camera go below floor") enforced here rather than at
    /// the input layer.
    ///
    /// Deliberately a plain C# class: no <c>MonoBehaviour</c>, no
    /// <c>UnityEngine.Input</c>, no <c>Transform</c>. EditMode tests can drive
    /// every command exhaustively, and
    /// <see cref="OrbitCameraController"/> is left as a thin layer that reads
    /// the mouse and copies <see cref="Position"/>/<see cref="Rotation"/> onto
    /// a camera each frame.
    /// </summary>
    public sealed class OrbitCameraRig
    {
        /// <summary>
        /// Never flat-on or below: a strictly positive pitch is what keeps the
        /// camera above the floor for any target at or above it.
        /// </summary>
        public const float MinPitchDeg = 5f;

        /// <summary>Just short of straight down, which would make yaw meaningless.</summary>
        public const float MaxPitchDeg = 89f;

        public const float MinDistanceM = 0.5f;
        public const float MaxDistanceM = 60f;

        /// <summary>The reset/home viewing angles: from -Z, looking down.</summary>
        public const float HomeYawDeg = 0f;
        public const float HomePitchDeg = 35f;

        /// <summary>The dollhouse preset's angled overhead view.</summary>
        public const float DollhousePitchDeg = 55f;

        private const float ZoomStep = 1.15f;
        private const float FitMargin = 1.10f;
        private const float MinFitRadiusM = 0.5f;
        private const float DefaultFovDeg = 60f;
        private const float DefaultAspect = 16f / 9f;

        public Vector3 Target { get; set; } = Vector3.zero;
        public float Distance { get; set; } = 8f;
        public float YawDeg { get; set; } = HomeYawDeg;
        public float PitchDeg { get; set; } = HomePitchDeg;

        /// <summary>World height of the floor plane the camera may not go below.</summary>
        public float FloorY { get; set; }

        public Quaternion Rotation => Quaternion.Euler(PitchDeg, YawDeg, 0f);

        public Vector3 Position => Target - Rotation * Vector3.forward * Distance;

        public bool IsValid =>
            IsFinite(Target.x) && IsFinite(Target.y) && IsFinite(Target.z) &&
            IsFinite(Distance) && Distance > 0f &&
            IsFinite(YawDeg) && IsFinite(PitchDeg);

        /// <summary>Left-drag: swing around the target.</summary>
        public void Orbit(float yawDeltaDeg, float pitchDeltaDeg)
        {
            if (!IsFinite(yawDeltaDeg) || !IsFinite(pitchDeltaDeg))
            {
                return;
            }

            YawDeg = Mathf.Repeat(YawDeg + yawDeltaDeg, 360f);
            PitchDeg = Mathf.Clamp(PitchDeg + pitchDeltaDeg, MinPitchDeg, MaxPitchDeg);
        }

        /// <summary>Scroll wheel: positive zooms in. Multiplicative, so it feels
        /// even at every distance, and hard-clamped so it can never reach or
        /// pass through the target.</summary>
        public void Zoom(float scrollDelta)
        {
            if (!IsFinite(scrollDelta))
            {
                return;
            }

            Distance = Mathf.Clamp(
                Distance * Mathf.Pow(ZoomStep, -scrollDelta),
                MinDistanceM,
                MaxDistanceM);
        }

        /// <summary>Right/middle-drag: slide the orbit target in the camera's
        /// own screen plane, never below the floor.</summary>
        public void Pan(float rightDelta, float upDelta)
        {
            if (!IsFinite(rightDelta) || !IsFinite(upDelta))
            {
                return;
            }

            Quaternion rotation = Rotation;
            Vector3 moved = Target
                          + rotation * Vector3.right * rightDelta
                          + rotation * Vector3.up * upDelta;

            Target = new Vector3(moved.x, Mathf.Max(moved.y, FloorY), moved.z);
        }

        /// <summary>
        /// Frames the whole room: centres on it, backs off far enough for its
        /// bounding sphere to fit the frustum, and restores the home viewing
        /// angles. This is both section 13.1's `F` (frame whole room) and the
        /// reset/home view, which keeps "reset" completely deterministic.
        /// </summary>
        public void Frame(Bounds bounds, float verticalFovDeg, float aspect)
        {
            Frame(bounds, verticalFovDeg, aspect, HomePitchDeg);
        }

        /// <summary>Section 13.1's `D`: the same framing from an angled
        /// overhead view. Hiding the ceiling is the renderer's half of the
        /// preset and is driven by <see cref="OrbitCameraController"/>.</summary>
        public void Dollhouse(Bounds bounds, float verticalFovDeg, float aspect)
        {
            Frame(bounds, verticalFovDeg, aspect, DollhousePitchDeg);
        }

        private void Frame(Bounds bounds, float verticalFovDeg, float aspect, float pitchDeg)
        {
            if (!IsFinite(bounds.center.x) || !IsFinite(bounds.center.y) || !IsFinite(bounds.center.z) ||
                !IsFinite(bounds.size.x) || !IsFinite(bounds.size.y) || !IsFinite(bounds.size.z))
            {
                // A bad bounds must never move the camera somewhere unrecoverable.
                return;
            }

            Target = new Vector3(bounds.center.x, Mathf.Max(bounds.center.y, FloorY), bounds.center.z);
            YawDeg = HomeYawDeg;
            PitchDeg = Mathf.Clamp(pitchDeg, MinPitchDeg, MaxPitchDeg);
            Distance = FitDistance(bounds, verticalFovDeg, aspect);
        }

        /// <summary>
        /// Distance at which the room's bounding sphere fits both the vertical
        /// and the horizontal field of view. Using the bounding sphere rather
        /// than the box means the framing holds at every orbit angle, so
        /// spinning the camera can never push a corner off-screen.
        /// </summary>
        private static float FitDistance(Bounds bounds, float verticalFovDeg, float aspect)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, MinFitRadiusM);

            float fov = IsFinite(verticalFovDeg) && verticalFovDeg > 1f && verticalFovDeg < 179f
                ? verticalFovDeg
                : DefaultFovDeg;
            float safeAspect = IsFinite(aspect) && aspect > 0.01f ? aspect : DefaultAspect;

            float halfVertical = fov * 0.5f * Mathf.Deg2Rad;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * safeAspect);

            float verticalFit = radius / Mathf.Sin(halfVertical);
            float horizontalFit = radius / Mathf.Sin(halfHorizontal);

            return Mathf.Clamp(
                Mathf.Max(verticalFit, horizontalFit) * FitMargin,
                MinDistanceM,
                MaxDistanceM);
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
