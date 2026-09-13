using GhostMap.Scanner.Editor;
using NUnit.Framework;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>
    /// Regression tests for the Task S1 failure where the scanner shipped to a
    /// physical iPhone with no active XR loader. The iOS XR settings asset was
    /// correct and did reach the player, but UNITY_XR_ARKIT_LOADER_ENABLED was
    /// never defined, so the Apple ARKit XR Plug-in compiled to stubs that
    /// register no subsystem descriptors and its native library was left out of
    /// the Xcode project. On device that read as "XR loader: none" with no
    /// active XRRaycastSubsystem or XRInputSubsystem.
    ///
    /// These assert the project's committed configuration, so the regression
    /// cannot return silently and be discovered only on a phone.
    /// </summary>
    public sealed class ScannerXrSettingsTests
    {
        [Test]
        public void IosArKitConfigurationIsBuildable()
        {
            Assert.DoesNotThrow(ScannerXrSettings.VerifyIosArKitConfiguration);
        }

        [Test]
        public void ArKitLoaderDefineIsSetForIos()
        {
            Assert.IsTrue(
                ScannerXrSettings.HasArKitLoaderDefine(),
                $"{ScannerXrSettings.ArKitLoaderDefine} must be in the iOS scripting define symbols, otherwise the " +
                "Apple ARKit XR Plug-in compiles to stubs and libUnityARKit.a is excluded from the iOS build.");
        }
    }
}
