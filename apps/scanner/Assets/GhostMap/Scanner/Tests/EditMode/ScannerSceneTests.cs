using GhostMap.Scanner.Editor;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Guards the scene wiring Task S2 depends on.
    ///
    /// Both S1 device failures presented as "nothing happens on the phone", and
    /// a null serialized reference or a missing EventSystem would present
    /// exactly the same way: the crosshair would sit there and the Lock Floor
    /// button would do nothing, with no error to read. Asserting the committed
    /// scene here means that class of failure is caught on a laptop.
    /// </summary>
    public sealed class ScannerSceneTests
    {
        [Test]
        public void SceneIsWiredForFloorLock()
        {
            Assert.DoesNotThrow(ScannerSceneBuilder.VerifyScene);
        }
    }
}
