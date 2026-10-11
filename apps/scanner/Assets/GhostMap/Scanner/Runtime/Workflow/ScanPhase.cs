namespace GhostMap.Scanner.Workflow
{
    /// <summary>
    /// The scanner's one explicit state enum, from implementation plan
    /// section 10. Only <see cref="ScanWorkflowController"/> changes it; no UI
    /// control may set a phase directly.
    ///
    /// The value is carried on the wire as a string in
    /// <c>SceneSnapshot.scanPhase</c>, so these names are observable by the
    /// viewer and must not be renamed casually.
    /// </summary>
    public enum ScanPhase
    {
        Boot,
        WaitingForTracking,
        FindFloor,
        FloorLocked,

        /// <summary>
        /// The automatic room scan: the user stands, turns once, and GhostMap
        /// derives the four walls from ARKit's planes. The default path. On
        /// success it hands four corners to the same corner store the sweep and
        /// walked paths use and moves on to height; "Help GhostMap" falls back
        /// to <see cref="SweepWalls"/>.
        /// </summary>
        AutoScanRoom,

        /// <summary>
        /// ADR-0005: the user stands, turns, and sweeps each wall's floor
        /// junction. Corners are derived from the four fitted walls. This is
        /// the default capture path.
        /// </summary>
        SweepWalls,

        /// <summary>
        /// Task S3's walked capture: the user walks to each corner and taps.
        /// Retained as the fallback from <see cref="SweepWalls"/> until the
        /// swept path has accuracy numbers from Task I2. Both paths produce the
        /// same four corners and converge on <see cref="VerifyClosure"/>.
        /// </summary>
        CaptureCorners,

        VerifyClosure,
        CaptureHeight,
        AddOpenings,
        AddObjects,
        ReadyToFinalize,
        Finalized
    }
}
