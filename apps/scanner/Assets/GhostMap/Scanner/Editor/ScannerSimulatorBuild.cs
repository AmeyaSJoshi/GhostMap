using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Builds the scanner for the iOS Simulator, where it runs in demo mode
    /// (<see cref="AR.SimulatedRoom"/>) so the scan UI can be seen and clicked
    /// through without a phone.
    ///
    /// <para><b>This rewrites the project's XR and player settings for the
    /// Simulator</b>: the ARKit loader comes out (its native library is
    /// device-only and will not link for the Simulator) and the SDK becomes
    /// the Simulator SDK. Run it in a scratch copy of the project, or run
    /// <c>GhostMap/Configure Scanner XR (iOS)</c> afterwards before building
    /// for a phone. Nothing here changes what a device build produces.</para>
    ///
    /// <para>Two steps in two Unity invocations, for the same reason as
    /// <see cref="ScannerBuild"/>: removing the ARKit define only reaches
    /// compiled code on the next script compilation.</para>
    /// <code>
    /// Unity -batchmode -quit -projectPath apps/scanner -executeMethod GhostMap.Scanner.Editor.ScannerSimulatorBuild.ConfigureForSimulator
    /// Unity -batchmode -quit -projectPath apps/scanner -executeMethod GhostMap.Scanner.Editor.ScannerSimulatorBuild.BuildForSimulator
    /// xcodebuild -project Builds/iOSSimulator/Unity-iPhone.xcodeproj -scheme Unity-iPhone -sdk iphonesimulator -configuration Debug build
    /// </code>
    /// </summary>
    public static class ScannerSimulatorBuild
    {
        public const string OutputPath = "Builds/iOSSimulator";

        private const string ArKitLoaderTypeName = "UnityEngine.XR.ARKit.ARKitLoader";

        public static void ConfigureForSimulator()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS
                && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                throw new BuildFailedException("Could not switch the active build target to iOS.");
            }

            ScannerInputSettings.EnableInputSystemBackend();

            if (EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget settingsAsset)
                && settingsAsset != null
                && settingsAsset.HasManagerSettingsForBuildTarget(BuildTargetGroup.iOS))
            {
                XRManagerSettings manager = settingsAsset.ManagerSettingsForBuildTarget(BuildTargetGroup.iOS);
                XRPackageMetadataStore.RemoveLoader(manager, ArKitLoaderTypeName, BuildTargetGroup.iOS);
                EditorUtility.SetDirty(manager);
            }

            var defines = new HashSet<string>(
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS)
                    .Split(';')
                    .Select(define => define.Trim())
                    .Where(define => define.Length > 0));
            defines.Remove(ScannerXrSettings.ArKitLoaderDefine);
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, string.Join(";", defines.OrderBy(d => d)));

            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            AssetDatabase.SaveAssets();

            Debug.Log("GhostMap: configured for the iOS Simulator (ARKit loader removed, Simulator SDK).");
        }

        public static void BuildForSimulator()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                throw new BuildFailedException("Run ConfigureForSimulator first.");
            }

            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
            PlayerSettings.productName = "GhostMap Scanner";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.ghostmap.scanner");
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.cameraUsageDescription = ScannerIosPostBuild.CameraUsageDescription;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            if (Directory.Exists(OutputPath))
            {
                Directory.Delete(OutputPath, recursive: true);
            }

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Scanner Simulator build failed: {report.summary.result}, {report.summary.totalErrors} errors.");
            }

            Debug.Log($"GhostMap: Simulator Xcode project written to {report.summary.outputPath}");
        }
    }
}
