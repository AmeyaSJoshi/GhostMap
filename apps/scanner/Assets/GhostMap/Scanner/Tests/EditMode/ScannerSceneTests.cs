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
    /// Covers Task S2's floor-lock wiring, Task S3's corner-capture wiring,
    /// Task S4's height-capture wiring and Task S5's opening/object wiring;
    /// <see cref="ScannerSceneBuilder.VerifyScene"/> throws on the first broken
    /// link in any of them.
    /// </summary>
    public sealed class ScannerSceneTests
    {
        [Test]
        public void SceneIsWiredForFloorLockCornerCaptureHeightCaptureAndOpeningsAndObjects()
        {
            Assert.DoesNotThrow(ScannerSceneBuilder.VerifyScene);
        }
    }
}
