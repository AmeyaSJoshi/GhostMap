using UnityEngine;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// Task R9: developer readouts (the S1 AR diagnostics) stay out of a
    /// normal scan. They show in the Editor and Development builds, or after
    /// the hidden toggle: tap Details <see cref="SecretTapCounter.Taps"/> times
    /// within <see cref="SecretTapCounter.WindowSeconds"/> seconds. The choice
    /// is remembered on the phone.
    /// </summary>
    public static class DeveloperMode
    {
        private const string PrefsKey = "GhostMap.DeveloperMode";

        public static bool Enabled => Debug.isDebugBuild || PlayerPrefs.GetInt(PrefsKey, 0) == 1;

        /// <summary>Flips the remembered setting and returns the new state.</summary>
        public static bool Toggle()
        {
            bool on = PlayerPrefs.GetInt(PrefsKey, 0) != 1;
            PlayerPrefs.SetInt(PrefsKey, on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log($"GhostMap: developer mode {(on ? "on" : "off")}.");
            return on;
        }
    }

    /// <summary>Counts quick taps for <see cref="DeveloperMode"/>'s hidden toggle. Plain C#, tested.</summary>
    public sealed class SecretTapCounter
    {
        public const int Taps = 5;
        public const float WindowSeconds = 3f;

        private int count;
        private float firstAt;

        /// <summary>Records one tap; true on the tap that completes the sequence (which then starts over).</summary>
        public bool Register(float now)
        {
            if (count == 0 || now - firstAt > WindowSeconds)
            {
                count = 0;
                firstAt = now;
            }

            count++;

            if (count < Taps)
            {
                return false;
            }

            count = 0;
            return true;
        }
    }
}
