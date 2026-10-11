using System;
using System.IO;

namespace GhostMap.Scanner.Share
{
    /// <summary>Where Send to Computer is.</summary>
    public enum ShareState
    {
        /// <summary>Nothing sent yet for this room.</summary>
        Idle,

        /// <summary>The share sheet is open.</summary>
        Sharing,

        /// <summary>iOS reported the room was sent or saved.</summary>
        Sent,

        /// <summary>The user closed the sheet without choosing anything.</summary>
        Cancelled,

        /// <summary>The sheet could not open, or iOS reported an error.</summary>
        Failed
    }

    /// <summary>
    /// Task R7: <b>Send to Computer</b> as plain C#. Hands a saved bundle to an
    /// <see cref="IShareSheet"/> and turns the outcome into one line for the
    /// guide. A cancelled or failed send never touches the saved file, so the
    /// room can always be sent again.
    /// </summary>
    public sealed class ShareController
    {
        public const string AirDropActivity = "com.apple.UIKit.activity.AirDrop";
        public const string MailActivity = "com.apple.UIKit.activity.Mail";
        public const string MessageActivity = "com.apple.UIKit.activity.Message";

        private readonly IShareSheet sheet;
        private readonly Func<string, bool> fileExists;

        public ShareController(IShareSheet sheet, Func<string, bool> fileExists = null)
        {
            this.sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            this.fileExists = fileExists ?? File.Exists;
        }

        public ShareState State { get; private set; } = ShareState.Idle;

        /// <summary>One line for the guide; empty when there is nothing to say.</summary>
        public string Message { get; private set; } = string.Empty;

        /// <summary>The file last handed to the sheet.</summary>
        public string LastFilePath { get; private set; } = string.Empty;

        public bool IsAvailable => sheet.IsAvailable;

        /// <summary>
        /// Opens the share sheet for a saved bundle. Refused while a sheet is
        /// already open, when no share sheet exists, or when the file is gone.
        /// </summary>
        public bool TryShare(string filePath)
        {
            if (State == ShareState.Sharing)
            {
                return false;
            }

            if (!sheet.IsAvailable)
            {
                Fail("Sending is not available on this device.");
                return false;
            }

            if (string.IsNullOrEmpty(filePath) || !fileExists(filePath))
            {
                Fail("The saved room file is missing. Finish the scan again to rebuild it.");
                return false;
            }

            LastFilePath = filePath;
            State = ShareState.Sharing;
            Message = "Choose your Mac under AirDrop, or Save to Files.";
            sheet.Share(filePath, OnFinished);
            return true;
        }

        /// <summary>Forgets the last outcome, for a new scan.</summary>
        public void Reset()
        {
            if (State != ShareState.Sharing)
            {
                State = ShareState.Idle;
                Message = string.Empty;
                LastFilePath = string.Empty;
            }
        }

        private void OnFinished(ShareResult result)
        {
            if (result.Completed)
            {
                State = ShareState.Sent;
                Message = Describe(result.ActivityType);
            }
            else if (result.Cancelled)
            {
                State = ShareState.Cancelled;
                Message = string.Empty;
            }
            else
            {
                Fail($"Not sent: {result.Error}");
            }
        }

        private void Fail(string message)
        {
            State = ShareState.Failed;
            Message = message;
        }

        /// <summary>What to tell the user after a completed share.</summary>
        public static string Describe(string activityType)
        {
            switch (activityType)
            {
                case AirDropActivity:
                    return "Sent with AirDrop.";
                case MailActivity:
                    return "Sent with Mail.";
                case MessageActivity:
                    return "Sent with Messages.";
                case EditorShareSheet.ActivityType:
                    return "Shown in Finder (Editor).";
            }

            if (!string.IsNullOrEmpty(activityType) &&
                (activityType.IndexOf("SaveToFiles", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 activityType.IndexOf("DocumentManager", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "Saved to Files.";
            }

            return "Sent.";
        }
    }
}
