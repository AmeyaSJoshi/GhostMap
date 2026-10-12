namespace GhostMap.Scanner.AR
{
    /// <summary>
    /// Task R9: after the app has been in the background, tracking is not
    /// trusted again until ARKit has reported good tracking continuously for
    /// <see cref="SettleSeconds"/>. ARKit can report its last state for a
    /// moment after resuming, before it has relocalized; a corner captured in
    /// that moment would be wrong. Plain C#, so the rule is EditMode-tested.
    /// </summary>
    public sealed class TrackingResumeGate
    {
        public const float SettleSeconds = 1f;

        private bool paused;
        private bool settling;
        private float goodSince = float.NaN;

        /// <summary>True from backgrounding until tracking has settled again.</summary>
        public bool IsRecovering => paused || settling;

        /// <summary>The app went to the background.</summary>
        public void Pause()
        {
            paused = true;
            settling = true;
            goodSince = float.NaN;
        }

        /// <summary>The app came back; tracking must now settle.</summary>
        public void Resume()
        {
            paused = false;
        }

        /// <summary>Whether to trust ARKit's tracking state right now.</summary>
        /// <param name="rawTrackingGood">What ARKit reports.</param>
        /// <param name="now">A monotonic clock, in seconds.</param>
        public bool Allows(bool rawTrackingGood, float now)
        {
            if (paused)
            {
                return false;
            }

            if (!settling)
            {
                return rawTrackingGood;
            }

            if (!rawTrackingGood)
            {
                goodSince = float.NaN;
                return false;
            }

            if (float.IsNaN(goodSince))
            {
                goodSince = now;
            }

            if (now - goodSince < SettleSeconds)
            {
                return false;
            }

            settling = false;
            return true;
        }
    }
}
