using System.Runtime.InteropServices;
using GhostMap.Scanner.Workflow;

namespace GhostMap.Scanner.UI
{
    /// <summary>Task R9: the seam onto the phone's haptics, so tests can count pulses.</summary>
    public interface IHaptics
    {
        void LightImpact();
    }

    /// <summary>
    /// Task R9: one light tap whenever the scan changes structurally: a floor
    /// lock, a corner, a height, a door, a piece of furniture, an undo. Several
    /// changes in one frame (Add All) are one tap. Plain C#, tested with a fake.
    /// </summary>
    public sealed class CaptureHaptics
    {
        private readonly IHaptics haptics;
        private ScanWorkflowController workflow;
        private int lastRevision;

        public CaptureHaptics(IHaptics haptics)
        {
            this.haptics = haptics;
        }

        /// <summary>Call once per frame.</summary>
        public void Observe(ScanWorkflowController current)
        {
            if (current == null)
            {
                return;
            }

            // A new workflow (Restart) is not a capture.
            if (current != workflow)
            {
                workflow = current;
                lastRevision = current.Revision;
                return;
            }

            if (current.Revision > lastRevision)
            {
                lastRevision = current.Revision;
                haptics?.LightImpact();
            }
        }
    }

    /// <summary>UIKit's light impact on the phone; nothing elsewhere.</summary>
    public sealed class IosHaptics : IHaptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void GhostMapHaptics_LightImpact();
#else
        private static void GhostMapHaptics_LightImpact() { }
#endif

        public void LightImpact() => GhostMapHaptics_LightImpact();
    }
}
