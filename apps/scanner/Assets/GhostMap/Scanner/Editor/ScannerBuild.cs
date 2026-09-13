using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Documented, repeatable scanner build (implementation plan section 21).
    /// Configures the iOS player settings Task S1 requires and produces an
    /// Xcode project under the git-ignored apps/scanner/Builds/ directory.
    /// The generated project still needs a signing team selected in Xcode
    /// before it can be deployed to a physical iPhone.
    ///
    /// This is deliberately a two-step process:
    ///
    /// <list type="number">
    /// <item><description><see cref="ConfigureXr"/> switches the active build
    /// target to iOS and turns the ARKit loader on, which sets the
    /// UNITY_XR_ARKIT_LOADER_ENABLED scripting define.</description></item>
    /// <item><description><see cref="BuildScanner"/> builds, once scripts have
    /// been recompiled with that define.</description></item>
    /// </list>
    ///
    /// They cannot be one step. A scripting define only reaches compiled code
    /// on the next script compilation, so XR configuration performed inside
    /// BuildPlayer arrives too late for the assemblies that build is producing.
    /// See <see cref="ScannerXrSettings"/> for what goes wrong when it is.
    /// </summary>
    public static class ScannerBuild
    {
        internal const string ConfigureXrMenuPath = "GhostMap/Configure Scanner XR (iOS)";

        private const string BuildMenuPath = "GhostMap/Build Scanner (iOS)";
        private const string BundleIdentifier = "com.ghostmap.scanner";
        private const string OutputPath = "Builds/iOS";

        /// <summary>
        /// Step 1. Makes iOS the active build target, enables the ARKit XR
        /// loader for it, and turns on the Input System backend that AR
        /// Foundation's TrackedPoseDriver needs to receive the device pose. Run
        /// this before <see cref="BuildScanner"/>, in its own Unity invocation
        /// when building from the command line, so scripts are recompiled with
        /// UNITY_XR_ARKIT_LOADER_ENABLED and ENABLE_INPUT_SYSTEM before the
        /// build runs.
        /// </summary>
        [MenuItem(ConfigureXrMenuPath)]
        public static void ConfigureXr()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS
                && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                throw new BuildFailedException(
                    "Could not switch the active build target to iOS. The iOS Build Support module must be installed.");
            }

            bool xrDefinesChanged = ScannerXrSettings.EnableArKitLoaderForIos();
            bool inputBackendChanged = ScannerInputSettings.EnableInputSystemBackend();

            Debug.Log(xrDefinesChanged
                ? $"GhostMap: enabled the ARKit XR loader for iOS and added {ScannerXrSettings.ArKitLoaderDefine}."
                : $"GhostMap: ARKit XR loader for iOS already enabled with {ScannerXrSettings.ArKitLoaderDefine} set.");

            Debug.Log(inputBackendChanged
                ? "GhostMap: switched Active Input Handling to Both so the Input System backend is on."
                : $"GhostMap: Active Input Handling is already {ScannerInputSettings.ActiveInputHandler}.");

            if (xrDefinesChanged || inputBackendChanged)
            {
                Debug.Log(
                    "GhostMap: scripts must finish recompiling before running the build. In the Editor, restart it " +
                    "if Active Input Handling changed.");
            }
        }

        /// <summary>
        /// Step 2. Produces the Xcode project. Fails fast rather than shipping a
        /// player whose XR loader could never initialize on device.
        /// </summary>
        [MenuItem(BuildMenuPath)]
        public static void BuildScanner()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                throw new BuildFailedException(
                    $"The active build target is {EditorUserBuildSettings.activeBuildTarget}, not iOS. " +
                    $"iOS scripting defines only apply while iOS is the active build target, so run " +
                    $"{ConfigureXrMenuPath} first.");
            }

#if !UNITY_XR_ARKIT_LOADER_ENABLED || !ENABLE_INPUT_SYSTEM
            // This assembly is compiled with the iOS scripting defines and with
            // the built-in input-handling defines, so a branch below surviving
            // compilation proves the currently loaded domain was built without
            // that define.
            var missing = new List<string>();
#if !UNITY_XR_ARKIT_LOADER_ENABLED
            missing.Add(
                $"{ScannerXrSettings.ArKitLoaderDefine} — the Apple ARKit XR Plug-in would compile to stubs and " +
                "libUnityARKit.a would be left out, so the app would report no active XR loader on device");
#endif
#if !ENABLE_INPUT_SYSTEM
            missing.Add(
                "ENABLE_INPUT_SYSTEM — Active Input Handling excludes the Input System backend, so AR Foundation's " +
                "TrackedPoseDriver would resolve no controls and the AR camera transform would never be written, " +
                "leaving the camera pose frozen on device however well ARKit tracks");
#endif
            throw new BuildFailedException(
                "These defines were missing when these scripts were compiled:\n  - " +
                string.Join("\n  - ", missing) +
                $"\nRun {ConfigureXrMenuPath} (or -executeMethod " +
                "GhostMap.Scanner.Editor.ScannerBuild.ConfigureXr in its own Unity invocation), let scripts " +
                "recompile, then build again.");
#else
            ScannerXrSettings.VerifyIosArKitConfiguration();
            ScannerInputSettings.VerifyInputSystemBackendEnabled();
            ConfigurePlayerSettings();

            // Unity's incremental iOS export can leave a stale Data folder
            // (PlayerSettings changes made programmatically during this same
            // build are not always detected as reasons to regenerate it). Force
            // a full export every time so what ships always matches what was
            // just configured.
            if (Directory.Exists(OutputPath))
            {
                Directory.Delete(OutputPath, recursive: true);
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Scanner iOS build failed: {report.summary.result}, {report.summary.totalErrors} errors.");
            }

            VerifyArKitNativePluginWasCopied(report.summary.outputPath);

            Debug.Log($"GhostMap: scanner Xcode project written to {report.summary.outputPath}");
#endif
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "GhostMap";
            PlayerSettings.productName = "GhostMap Scanner";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleIdentifier);

            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.targetOSVersionString = "13.0";

            // The Apple ARKit XR Plug-in's own build processor fails the build
            // when this is empty, and Unity writes it into Info.plist for us.
            PlayerSettings.iOS.cameraUsageDescription = ScannerIosPostBuild.CameraUsageDescription;

            // ARKit is a required capability for this app, and the plug-in only
            // supports ARM64.
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
        }

        /// <summary>
        /// The ARKit package decides whether to copy libUnityARKit.a into the
        /// Xcode project from its own build-processor state. If that state is
        /// ever wrong again the managed code still builds cleanly and the
        /// failure only shows up on a physical device, so check the artifact.
        /// </summary>
        private static void VerifyArKitNativePluginWasCopied(string outputPath)
        {
            bool copied = Directory.Exists(outputPath)
                && Directory.EnumerateFiles(outputPath, "libUnityARKit.a", SearchOption.AllDirectories).Any();

            if (!copied)
            {
                throw new BuildFailedException(
                    $"The generated Xcode project at {outputPath} does not contain libUnityARKit.a. The Apple ARKit " +
                    "XR Plug-in excluded its native library, so the app would report no active XR loader on device.");
            }
        }
    }
}
