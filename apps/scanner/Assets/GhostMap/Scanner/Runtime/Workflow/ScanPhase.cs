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
        CaptureCorners,
        VerifyClosure,
        CaptureHeight,
        AddOpenings,
        AddObjects,
        ReadyToFinalize,
        Finalized
    }
}
