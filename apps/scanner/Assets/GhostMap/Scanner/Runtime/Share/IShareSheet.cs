using System;
using System.Globalization;

namespace GhostMap.Scanner.Share
{
    /// <summary>
    /// Task R7 (ADR-0013 section 2): hands one file to the operating system's
    /// share sheet. On the iPhone that is <c>UIActivityViewController</c>
    /// (AirDrop, Save to Files, Mail, ...); in the Editor the file is revealed
    /// in Finder; in tests it is a fake.
    /// </summary>
    public interface IShareSheet
    {
        /// <summary>False where no share sheet exists (for example a standalone desktop build).</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Presents the sheet for <paramref name="filePath"/>. <paramref name="onFinished"/>
        /// is called once, on the main thread, when the user finishes or
        /// dismisses it.
        /// </summary>
        void Share(string filePath, Action<ShareResult> onFinished);
    }

    /// <summary>How a share sheet ended.</summary>
    public readonly struct ShareResult
    {
        public ShareResult(bool completed, string activityType, string error)
        {
            Completed = completed;
            ActivityType = activityType ?? string.Empty;
            Error = error ?? string.Empty;
        }

        /// <summary>True when the user picked a destination and iOS reported success.</summary>
        public bool Completed { get; }

        /// <summary>iOS activity type, e.g. <c>com.apple.UIKit.activity.AirDrop</c>; empty when cancelled.</summary>
        public string ActivityType { get; }

        /// <summary>Why it failed; empty when it did not.</summary>
        public string Error { get; }

        /// <summary>Dismissed without choosing anything, and without an error.</summary>
        public bool Cancelled => !Completed && Error.Length == 0;

        /// <summary>
        /// Parses the native plugin's callback, <c>&lt;0|1&gt;|&lt;activityType&gt;|&lt;error&gt;</c>
        /// (<c>GhostMapShare.mm</c>). The error is last, so it may itself contain "|".
        /// </summary>
        public static bool TryParse(string message, out ShareResult result)
        {
            result = default;

            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            string[] parts = message.Split(new[] { '|' }, 3);

            if (parts.Length != 3 || (parts[0] != "0" && parts[0] != "1"))
            {
                return false;
            }

            result = new ShareResult(parts[0] == "1", parts[1], parts[2]);
            return true;
        }

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "completed={0} activity={1} error={2}", Completed, ActivityType, Error);
    }
}
