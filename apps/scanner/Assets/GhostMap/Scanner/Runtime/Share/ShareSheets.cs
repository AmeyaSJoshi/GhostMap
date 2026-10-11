using System;
using UnityEngine;

namespace GhostMap.Scanner.Share
{
    /// <summary>Picks the share sheet for the platform the scanner runs on.</summary>
    public static class ShareSheets
    {
        public static IShareSheet CreateDefault()
        {
#if UNITY_EDITOR
            return new EditorShareSheet();
#elif UNITY_IOS
            return IosShareSheet.Create();
#else
            return new UnavailableShareSheet();
#endif
        }
    }

    /// <summary>
    /// The Editor's stand-in: reveals the saved bundle in Finder and reports a
    /// completed share, so the whole Finish, Send flow can be tried without a
    /// phone (including demo mode).
    /// </summary>
    public sealed class EditorShareSheet : IShareSheet
    {
        public const string ActivityType = "ghostmap.editor.reveal";

        public bool IsAvailable => true;

        public void Share(string filePath, Action<ShareResult> onFinished)
        {
            Debug.Log($"GhostMap share (Editor): {filePath}");
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(filePath);
#endif
            onFinished?.Invoke(new ShareResult(true, ActivityType, string.Empty));
        }
    }

    /// <summary>Where there is no share sheet at all.</summary>
    public sealed class UnavailableShareSheet : IShareSheet
    {
        public bool IsAvailable => false;

        public void Share(string filePath, Action<ShareResult> onFinished)
            => onFinished?.Invoke(new ShareResult(false, string.Empty, "Sending is not available on this device."));
    }
}
