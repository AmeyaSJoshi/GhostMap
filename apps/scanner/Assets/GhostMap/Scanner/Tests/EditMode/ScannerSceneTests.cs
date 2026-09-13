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
    /// Covers Task S2's floor-lock wiring and Task S3's corner-capture wiring;
    /// <see cref="ScannerSceneBuilder.VerifyScene"/> throws on the first broken
    /// link in either.
    /// </summary>
    public sealed class ScannerSceneTests
    {
        [Test]
        public void SceneIsWiredForFloorLockAndCornerCapture()
        {
            Assert.DoesNotThrow(ScannerSceneBuilder.VerifyScene);
        }
    }
}
