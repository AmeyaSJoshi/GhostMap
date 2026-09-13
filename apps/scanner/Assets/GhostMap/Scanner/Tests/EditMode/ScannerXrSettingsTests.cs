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

        /// <summary>
        /// Regression test for the second Task S1 device failure: ARKit tracked
        /// correctly but the AR camera Transform never moved, because Active
        /// Input Handling was "Input Manager (Old)". Without the Input System
        /// backend there is no &lt;HandheldARInputDevice&gt;, so AR Foundation's
        /// TrackedPoseDriver resolves no controls, reports TrackingStates.None,
        /// and writes neither position nor rotation.
        /// </summary>
        [Test]
        public void InputSystemBackendIsEnabledForTheProject()
        {
            Assert.IsTrue(
                ScannerInputSettings.IsInputSystemBackendEnabled,
                "Active Input Handling is " + ScannerInputSettings.ActiveInputHandler +
                "; AR Foundation's TrackedPoseDriver needs the Input System backend to drive the AR camera.");
        }

        [Test]
        public void InputSystemBackendIsCompiledIn()
        {
#if ENABLE_INPUT_SYSTEM
            Assert.Pass();
#else
            Assert.Fail(
                "ENABLE_INPUT_SYSTEM was not defined when these scripts were compiled, so no XR input device can " +
                "exist and the AR camera pose would stay frozen on device.");
#endif
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
