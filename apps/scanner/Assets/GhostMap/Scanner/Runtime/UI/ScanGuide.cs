using GhostMap.Scanner.Capture;
using GhostMap.Scanner.Workflow;
using GhostMap.Shared.Validation;

namespace GhostMap.Scanner.UI
{
    /// <summary>How a guide message should be colored on screen.</summary>
    public enum GuideMessageKind
    {
        None = 0,
        Tip,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Facts the guide needs that live outside <see cref="ScanWorkflowController"/>.
    /// </summary>
    public readonly struct ScanGuideContext
    {
        public ScanGuideContext(
            bool trackingGood,
            FloorLockRejection lastFloorRejection,
            bool connectedToComputer)
        {
            TrackingGood = trackingGood;
            LastFloorRejection = lastFloorRejection;
            ConnectedToComputer = connectedToComputer;
        }

        public bool TrackingGood { get; }

        /// <summary>The most recent Lock Floor refusal, or None when there was none.</summary>
        public FloorLockRejection LastFloorRejection { get; }

        public bool ConnectedToComputer { get; }
    }

    /// <summary>What the screen should say right now. See <see cref="ScanGuide"/>.</summary>
    public readonly struct ScanGuideStep
    {
        public ScanGuideStep(
            int number,
            string title,
            string instruction,
            string aimHint,
            string message,
            GuideMessageKind messageKind)
        {
            Number = number;
            Title = title;
            Instruction = instruction;
            AimHint = aimHint;
            Message = message;
            MessageKind = messageKind;
        }

        /// <summary>1-based step number, out of <see cref="ScanGuide.StepCount"/>.</summary>
        public int Number { get; }

        /// <summary>Short step name for the header, such as "Measure the ceiling".</summary>
        public string Title { get; }

        /// <summary>One or two plain sentences: what to do next.</summary>
        public string Instruction { get; }

        /// <summary>
        /// What the crosshair should be resting on, shown in a bubble right
        /// next to it. Null when this step does not involve aiming.
        /// </summary>
        public string AimHint { get; }

        /// <summary>An error, warning, tip or success line. Empty when there is none.</summary>
        public string Message { get; }

        public GuideMessageKind MessageKind { get; }
    }

    /// <summary>
    /// Turns the scan's current state into plain-language guidance: which
    /// step this is, what to do, where to point the dot, and what went wrong.
    ///
    /// <para>The per-phase HUDs still own their buttons and AR markers. This
    /// class owns only the words, so that every screen tells a first-time user
    /// exactly one next action. It reads the controllers and changes nothing.
    /// It is plain C# so every message can be tested without a device.</para>
    /// </summary>
    public static class ScanGuide
    {
        public const int StepCount = 7;

        public static ScanGuideStep Describe(ScanWorkflowController workflow, ScanGuideContext context)
        {
            switch (workflow.Phase)
            {
                case ScanPhase.Boot:
                case ScanPhase.WaitingForTracking:
                case ScanPhase.FindFloor:
                    return DescribeFindFloor(workflow, context);

                case ScanPhase.FloorLocked:
                case ScanPhase.SweepWalls:
                    return DescribeSweep(workflow);

                case ScanPhase.CaptureCorners:
                    return DescribeWalkedCorners(workflow);

                case ScanPhase.VerifyClosure:
                    return DescribeVerifyClosure(workflow);

                case ScanPhase.CaptureHeight:
                    return DescribeHeight(workflow);

                case ScanPhase.AddOpenings:
                    return DescribeOpenings(workflow);

                case ScanPhase.AddObjects:
                    return DescribeObjects(workflow);

                case ScanPhase.ReadyToFinalize:
                    return new ScanGuideStep(
                        7,
                        "Send to computer",
                        context.ConnectedToComputer
                            ? "Your room is ready. Tap Finish & Send to hand it to your computer."
                            : "Your room is ready. Connect to your computer above, then tap Finish & Send.",
                        null,
                        context.ConnectedToComputer ? "Connected to your computer." : string.Empty,
                        context.ConnectedToComputer ? GuideMessageKind.Success : GuideMessageKind.None);

                case ScanPhase.Finalized:
                    return new ScanGuideStep(
                        7,
                        "All done",
                        "Your room is saved. Open GhostMap on your computer to look around and edit it.",
                        null,
                        context.ConnectedToComputer
                            ? "Sent to your computer."
                            : "Not connected yet. Connect above and the room will be sent.",
                        context.ConnectedToComputer ? GuideMessageKind.Success : GuideMessageKind.Warning);

                default:
                    return new ScanGuideStep(1, "Getting ready", string.Empty, null, string.Empty, GuideMessageKind.None);
            }
        }

        // -------------------------------------------------------------------
        // Step 1 — floor
        // -------------------------------------------------------------------

        private static ScanGuideStep DescribeFindFloor(ScanWorkflowController workflow, ScanGuideContext context)
        {
            const string title = "Find the floor";

            if (workflow.Phase != ScanPhase.FindFloor || !context.TrackingGood)
            {
                return new ScanGuideStep(
                    1,
                    title,
                    "Slowly move your phone side to side so it can see the room.",
                    null,
                    "Good light and a floor with some texture help.",
                    GuideMessageKind.Tip);
            }

            string message = string.Empty;
            GuideMessageKind kind = GuideMessageKind.None;

            switch (context.LastFloorRejection)
            {
                case FloorLockRejection.NoFloorHit:
                    message = "No floor under the dot yet. Aim lower and move the phone a little.";
                    kind = GuideMessageKind.Warning;
                    break;

                case FloorLockRejection.PlaneNotHorizontalUp:
                    message = "That looks like a wall or table, not the floor. Aim at the floor.";
                    kind = GuideMessageKind.Warning;
                    break;

                case FloorLockRejection.DegenerateForward:
                    message = "Don't point straight down. Aim at the floor a few steps ahead.";
                    kind = GuideMessageKind.Warning;
                    break;

                case FloorLockRejection.TrackingNotGood:
                case FloorLockRejection.NoCameraPose:
                    message = "Lost tracking. Move the phone slowly and try again.";
                    kind = GuideMessageKind.Warning;
                    break;
            }

            return new ScanGuideStep(
                1,
                title,
                "Point the dot at the floor a few steps in front of you, then tap Lock Floor.",
                "Aim at the floor",
                message,
                kind);
        }

        // -------------------------------------------------------------------
        // Step 2 — walls (swept, ADR-0005)
        // -------------------------------------------------------------------

        private static ScanGuideStep DescribeSweep(ScanWorkflowController workflow)
        {
            const string title = "Trace the walls";
            WallSweepController sweep = workflow.WallSweep;
            int total = WallSweepController.RequiredWallCount;

            if (workflow.Phase == ScanPhase.FloorLocked)
            {
                return new ScanGuideStep(
                    2,
                    title,
                    "Stand near the middle of the room. You'll trace where each wall meets the floor, without walking.",
                    null,
                    "Floor locked.",
                    GuideMessageKind.Success);
            }

            // Room-level refusal (the four walls do not make a legal room)
            // is the most actionable thing on screen when present.
            string error = FirstNonEmpty(sweep.LastError, workflow.Corners.LastError);

            if (sweep.IsSweeping)
            {
                int wall = sweep.WallCount + 1;

                return sweep.ActiveSweepLooksUsable
                    ? new ScanGuideStep(
                        2,
                        title,
                        $"Good. Keep sliding along wall {wall} to its end, then tap Done.",
                        "Follow the bottom of the wall",
                        $"Traced {sweep.ActiveSpanM:F1} m",
                        GuideMessageKind.Success)
                    : new ScanGuideStep(
                        2,
                        title,
                        $"Slowly slide the dot along the line where the floor meets wall {wall}.",
                        "Follow the bottom of the wall",
                        string.Empty,
                        GuideMessageKind.None);
            }

            if (sweep.IsComplete)
            {
                return new ScanGuideStep(
                    2,
                    title,
                    "All 4 walls traced. Tap Build Room.",
                    null,
                    error,
                    string.IsNullOrEmpty(error) ? GuideMessageKind.None : GuideMessageKind.Error);
            }

            string next = sweep.WallCount == 0
                ? "Point the dot where the floor meets any wall, then tap Start."
                : $"Turn to the next wall (going around the room in order). Point at its bottom edge and tap Start. {sweep.WallCount} of {total} done.";

            string tip = sweep.WallCount == 0
                ? "Furniture in the way is fine. Trace any part of the wall you can see."
                : string.Empty;

            return new ScanGuideStep(
                2,
                title,
                next,
                "Where floor meets wall",
                string.IsNullOrEmpty(error) ? tip : error,
                !string.IsNullOrEmpty(error)
                    ? GuideMessageKind.Error
                    : string.IsNullOrEmpty(tip) ? GuideMessageKind.None : GuideMessageKind.Tip);
        }

        // -------------------------------------------------------------------
        // Step 2 (fallback) — walked corners
        // -------------------------------------------------------------------

        private static ScanGuideStep DescribeWalkedCorners(ScanWorkflowController workflow)
        {
            CornerCaptureController corners = workflow.Corners;
            int next = corners.CornerCount + 1;
            string error = corners.LastError;

            return new ScanGuideStep(
                2,
                "Tap the corners",
                $"Walk to corner {next} of {CornerCaptureController.RequiredCornerCount}. Put the dot right on the corner at floor level, then tap Capture. Go around the room in order.",
                "Room corner, at the floor",
                error,
                string.IsNullOrEmpty(error) ? GuideMessageKind.None : GuideMessageKind.Error);
        }

        // -------------------------------------------------------------------
        // Step 3 — closure check
        // -------------------------------------------------------------------

        private static ScanGuideStep DescribeVerifyClosure(ScanWorkflowController workflow)
        {
            const string title = "Check a corner";
            CornerCaptureController corners = workflow.Corners;
            string redo = workflow.RoomWasSwept ? "Redo Walls" : "Redo Corners";

            if (corners.HasClosureMeasurement && corners.LastClosureQuality == ClosureQuality.Rejected)
            {
                return new ScanGuideStep(
                    3,
                    title,
                    $"That corner was too far off. Tap {redo} and try again a little more carefully.",
                    null,
                    $"Off by {Centimeters(corners.ClosureErrorM)}.",
                    GuideMessageKind.Error);
            }

            string error = corners.LastError;

            return new ScanGuideStep(
                3,
                title,
                "Point the dot at the real corner where the blue marker is, then tap Check Corner. This tells you how accurate the room is.",
                "That corner, at the floor",
                error,
                string.IsNullOrEmpty(error) ? GuideMessageKind.None : GuideMessageKind.Error);
        }

        // -------------------------------------------------------------------
        // Step 4 — height
        // -------------------------------------------------------------------

        private static ScanGuideStep DescribeHeight(ScanWorkflowController workflow)
        {
            HeightCaptureController height = workflow.Height;
            CornerCaptureController corners = workflow.Corners;

            string message = height.LastError;
            GuideMessageKind kind = GuideMessageKind.Error;

            if (string.IsNullOrEmpty(message) && corners.HasClosureMeasurement)
            {
                message = $"Corner check passed, off by {Centimeters(corners.ClosureErrorM)}.";
                kind = GuideMessageKind.Success;
            }

            return new ScanGuideStep(
                4,
                "Measure the ceiling",
                "Point the dot where the yellow-lined wall meets the ceiling, then tap Measure. Or type the height below.",
                "Where wall meets ceiling",
                message,
                string.IsNullOrEmpty(message) ? GuideMessageKind.None : kind);
        }

        // -------------------------------------------------------------------
        // Step 5 — doors and windows
        // -------------------------------------------------------------------

        private static ScanGuideStep DescribeOpenings(ScanWorkflowController workflow)
        {
            OpeningCaptureController openings = workflow.Openings;
            string type = OpeningName(openings.SelectedType);
            string error = openings.LastError;

            if (openings.HasPendingStartPoint)
            {
                return new ScanGuideStep(
                    5,
                    "Doors & windows",
                    $"Now point at the TOP-RIGHT corner of the {type} and tap again.",
                    $"Top-right of the {type}",
                    error,
                    string.IsNullOrEmpty(error) ? GuideMessageKind.None : GuideMessageKind.Error);
            }

            string tip = openings.OpeningCount == 0
                ? "No doors or windows to add? Just tap Next."
                : $"{openings.OpeningCount} added. Add more, or tap Next.";

            return new ScanGuideStep(
                5,
                "Doors & windows",
                $"Pick the wall with a {type} (yellow line). Point at the {type}'s BOTTOM-LEFT corner and tap.",
                $"Bottom-left of the {type}",
                string.IsNullOrEmpty(error) ? tip : error,
                string.IsNullOrEmpty(error) ? GuideMessageKind.Tip : GuideMessageKind.Error);
        }

        // -------------------------------------------------------------------
        // Step 6 — furniture
        // -------------------------------------------------------------------

        private static ScanGuideStep DescribeObjects(ScanWorkflowController workflow)
        {
            ObjectPlacementController objects = workflow.Objects;
            FurnitureDetectionController detection = workflow.FurnitureDetection;
            string type = Capitalize(objects.SelectedType);
            string error = objects.LastError;

            // ADR-0006: a detected surface is offered first. GhostMap measured
            // it; the user still says what it is.
            if (detection.HasSelection)
            {
                FurnitureCandidate candidate = detection.Candidates[detection.SelectedIndex];
                string found = detection.CandidateCount > 1
                    ? $"Surface {detection.SelectedIndex + 1} of {detection.CandidateCount}: "
                    : "Found a surface: ";

                // ADR-0007: when GhostMap has a specific guess, the user's
                // usual job is a single tap.
                string suggested = detection.SelectedType;
                string instruction = detection.TypeIsSuggested && suggested != FurnitureTypeSuggester.Fallback
                    ? $"Looks like a {suggested} at the yellow dot. Tap Add. Wrong type? Tap Is: to change it, or Skip."
                    : "Found something at the yellow dot. Tap Is: to say what it is, then Add. Or Skip it.";

                return new ScanGuideStep(
                    6,
                    "Add furniture",
                    instruction,
                    null,
                    string.IsNullOrEmpty(error)
                        ? $"{found}{candidate.WidthM:F2} × {candidate.DepthM:F2} m, top {candidate.HeightM:F2} m high."
                        : error,
                    string.IsNullOrEmpty(error) ? GuideMessageKind.Tip : GuideMessageKind.Error);
            }

            string tip = objects.ObjectCount == 0
                ? "Look at desk and table tops to detect them. No furniture? Just tap Next."
                : "Use the size buttons to match the last thing you placed.";

            return new ScanGuideStep(
                6,
                "Add furniture",
                $"Point the dot at the middle of the {type.ToLowerInvariant()} on the floor, then tap Place.",
                $"Middle of the {type.ToLowerInvariant()}",
                string.IsNullOrEmpty(error) ? tip : error,
                string.IsNullOrEmpty(error) ? GuideMessageKind.Tip : GuideMessageKind.Error);
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        /// <summary>"door" or "window", never a raw type id the user did not choose.</summary>
        public static string OpeningName(string type)
        {
            return type == OpeningValidator.TypeWindow ? "window" : "door";
        }

        public static string Capitalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static string Centimeters(float meters)
        {
            return $"{meters * 100f:F0} cm";
        }

        private static string FirstNonEmpty(string a, string b)
        {
            return !string.IsNullOrEmpty(a) ? a : b ?? string.Empty;
        }
    }
}
