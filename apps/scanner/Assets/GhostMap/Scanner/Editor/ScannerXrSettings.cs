using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Enables the Apple ARKit XR loader for iOS through XR Plug-in Management,
    /// equivalent to checking "Apple ARKit" under Project Settings > XR
    /// Plug-in Management > iOS by hand. This is what actually marks the app
    /// as requiring an ARKit-capable device (implementation plan section 2.4)
    /// and lets ARSession find a provider at runtime — installing the ARKit
    /// package alone does not activate it.
    ///
    /// Assigning the loader is only half the job. Nearly every native entry
    /// point in com.unity.xr.arkit is wrapped in
    /// <c>#if UNITY_XR_ARKIT_LOADER_ENABLED</c>, and the package only includes
    /// libUnityARKit.a in an iOS build when its own build processor has
    /// observed that define. Without it the player still ships an ARKitLoader
    /// asset, but the loader is compiled down to stubs that register no
    /// subsystem descriptors, so ARKitLoader.Initialize() fails and XR
    /// Management reports no active loader on device.
    ///
    /// The package normally adds that define itself from an editor coroutine in
    /// UnityEditor.XR.ARKit.LoaderEnabledCheck, which returns immediately in
    /// batch mode and, in the Editor, only runs some time *after* the loader is
    /// assigned. A scripting define also only takes effect on the next script
    /// compilation. Configuring XR from inside BuildPlayer therefore can never
    /// affect the assemblies that same build is compiling. That is why this is
    /// a separate step from <see cref="ScannerBuild"/>: configure, let scripts
    /// recompile, then build.
    /// </summary>
    public static class ScannerXrSettings
    {
        /// <summary>
        /// Scripting define the Apple ARKit XR Plug-in gates its native
        /// implementation and its iOS plugin inclusion behind.
        /// </summary>
        public const string ArKitLoaderDefine = "UNITY_XR_ARKIT_LOADER_ENABLED";

        private const string SettingsAssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        private const string ArKitLoaderTypeName = "UnityEngine.XR.ARKit.ARKitLoader";

        /// <summary>
        /// Assigns the ARKit loader to the iOS XR configuration and makes sure
        /// <see cref="ArKitLoaderDefine"/> is set for iOS.
        /// </summary>
        /// <returns>
        /// True when the scripting defines changed, meaning scripts must be
        /// recompiled (and the Editor domain reloaded) before an iOS build can
        /// produce a working player.
        /// </returns>
        public static bool EnableArKitLoaderForIos()
        {
            XRGeneralSettingsPerBuildTarget settingsAsset = FindOrCreateSettingsAsset();

            if (!settingsAsset.HasSettingsForBuildTarget(BuildTargetGroup.iOS))
            {
                settingsAsset.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.iOS);
            }

            if (!settingsAsset.HasManagerSettingsForBuildTarget(BuildTargetGroup.iOS))
            {
                settingsAsset.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.iOS);
            }

            XRGeneralSettings generalSettings = settingsAsset.SettingsForBuildTarget(BuildTargetGroup.iOS);
            generalSettings.InitManagerOnStart = true;

            XRManagerSettings manager = settingsAsset.ManagerSettingsForBuildTarget(BuildTargetGroup.iOS);
            XRPackageMetadataStore.AssignLoader(manager, ArKitLoaderTypeName, BuildTargetGroup.iOS);

            // AssignLoader alone leaves automatic loading/running off, which
            // would leave the ARKit subsystem never started at runtime even
            // though the loader is assigned. This matches what checking
            // "Initialize XR on Startup" does in the Editor UI.
            manager.automaticLoading = true;
            manager.automaticRunning = true;

            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(settingsAsset);
            AssetDatabase.SaveAssets();

            return AddArKitLoaderDefine();
        }

        /// <summary>
        /// Throws when the iOS XR configuration is not in a state that can
        /// produce a working ARKit player, so a misconfiguration fails the
        /// build instead of shipping an app that silently has no XR loader.
        /// </summary>
        public static void VerifyIosArKitConfiguration()
        {
            var problems = new List<string>();

            XRGeneralSettings generalSettings = null;
            XRManagerSettings manager = null;

            if (!EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget settingsAsset)
                || settingsAsset == null)
            {
                problems.Add(
                    $"no XRGeneralSettingsPerBuildTarget asset is registered as the '{XRGeneralSettings.k_SettingsKey}' " +
                    "EditorBuildSettings config object, so XR Plug-in Management has no settings to ship");
            }
            else if (!settingsAsset.HasSettingsForBuildTarget(BuildTargetGroup.iOS))
            {
                problems.Add("XRGeneralSettings has no entry for the iOS build target group");
            }
            else
            {
                generalSettings = settingsAsset.SettingsForBuildTarget(BuildTargetGroup.iOS);
                manager = settingsAsset.ManagerSettingsForBuildTarget(BuildTargetGroup.iOS);

                if (generalSettings == null)
                {
                    problems.Add("the iOS XRGeneralSettings entry is null");
                }
                else if (!generalSettings.InitManagerOnStart)
                {
                    problems.Add("'Initialize XR on Startup' is off for iOS (XRGeneralSettings.InitManagerOnStart)");
                }

                if (manager == null)
                {
                    problems.Add("iOS has no XRManagerSettings instance");
                }
                else
                {
                    if (!manager.automaticLoading)
                    {
                        problems.Add("XRManagerSettings.automaticLoading is off for iOS");
                    }

                    IReadOnlyList<XRLoader> loaders = manager.activeLoaders;
                    if (loaders.Count == 0)
                    {
                        problems.Add("the iOS loader list is empty; ARKitLoader is not assigned");
                    }
                    else if (loaders.Any(loader => loader == null))
                    {
                        problems.Add(
                            "the iOS loader list contains a null entry, which means a loader asset reference is " +
                            "broken (missing or moved .asset)");
                    }
                    else if (!loaders.Any(loader => loader.GetType().FullName == ArKitLoaderTypeName))
                    {
                        string assigned = string.Join(", ", loaders.Select(loader => loader.GetType().FullName));
                        problems.Add($"the iOS loader list is [{assigned}] and does not contain {ArKitLoaderTypeName}");
                    }
                }
            }

            if (!HasArKitLoaderDefine())
            {
                problems.Add(
                    $"{ArKitLoaderDefine} is not in the iOS scripting define symbols, so the Apple ARKit XR Plug-in " +
                    "would compile to stubs and libUnityARKit.a would be left out of the Xcode project");
            }

            if (problems.Count > 0)
            {
                throw new BuildFailedException(
                    "Scanner iOS XR configuration is not usable:\n  - " + string.Join("\n  - ", problems) +
                    $"\nRun {ScannerBuild.ConfigureXrMenuPath} (or -executeMethod " +
                    "GhostMap.Scanner.Editor.ScannerBuild.ConfigureXr) and let scripts recompile, then build again.");
            }
        }

        /// <summary>
        /// True when <see cref="ArKitLoaderDefine"/> is present in the iOS
        /// scripting define symbols stored in PlayerSettings.
        /// </summary>
        public static bool HasArKitLoaderDefine()
        {
            return ReadIosDefines().Contains(ArKitLoaderDefine);
        }

        private static bool AddArKitLoaderDefine()
        {
            HashSet<string> defines = ReadIosDefines();
            if (!defines.Add(ArKitLoaderDefine))
            {
                return false;
            }

            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, string.Join(";", defines.OrderBy(d => d)));
            AssetDatabase.SaveAssets();
            return true;
        }

        private static HashSet<string> ReadIosDefines()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS);
            return new HashSet<string>(
                defines.Split(';').Select(define => define.Trim()).Where(define => define.Length > 0));
        }

        private static XRGeneralSettingsPerBuildTarget FindOrCreateSettingsAsset()
        {
            if (EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget settingsAsset)
                && settingsAsset != null)
            {
                return settingsAsset;
            }

            string[] found = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            if (found.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(found[0]);
                settingsAsset = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settingsAsset, true);
                return settingsAsset;
            }

            settingsAsset = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            Directory.CreateDirectory("Assets/XR");
            AssetDatabase.CreateAsset(settingsAsset, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settingsAsset, true);
            return settingsAsset;
        }
    }
}
