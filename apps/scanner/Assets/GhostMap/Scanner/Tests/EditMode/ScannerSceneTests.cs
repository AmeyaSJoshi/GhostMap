using GhostMap.Scanner.Editor;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Guards the scene wiring the capture tasks depend on.
    ///
    /// Both S1 device failures presented as "nothing happens on the phone", and
    /// a null serialized reference or a missing EventSystem would present
    /// exactly the same way: the crosshair would sit there and the buttons
    /// would do nothing, with no error to read. Asserting the committed scene
    /// here means that class of failure is caught on a laptop.
    ///
    /// Covers Task S2's floor-lock wiring, ADR-0005's wall-sweep wiring, Task
    /// S3's corner-capture wiring, Task S4's height-capture wiring and Task
    /// S5's opening/object wiring; <see cref="ScannerSceneBuilder.VerifyScene"/>
    /// throws on the first broken link in any of them.
    ///
    /// <para><b>This asserts the committed <c>Scanner.unity</c>, so it fails
    /// until the scene is regenerated after any wiring change.</b> ADR-0005
    /// added a <c>WallSweepHud</c> requirement, and the scene was authored on a
    /// machine without Unity, so <c>GhostMap/Build Scanner Scene</c> (or
    /// <c>ScannerSceneBuilder.BuildScene</c>) must be run before this passes.
    /// The requirement is deliberately hard rather than conditional: a null
    /// serialized reference presents on the phone as "nothing happens", which
    /// is exactly what both S1 device failures looked like, and softening the
    /// check would move that failure back onto the device.</para>
    /// </summary>
    public sealed class ScannerSceneTests
    {
        [Test]
        public void SceneIsWiredForFloorLockWallSweepCornerCaptureHeightCaptureAndOpeningsAndObjects()
        {
            Assert.DoesNotThrow(ScannerSceneBuilder.VerifyScene);
        }
    }
}
