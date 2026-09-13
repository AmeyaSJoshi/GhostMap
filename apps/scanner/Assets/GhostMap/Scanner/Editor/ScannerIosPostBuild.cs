using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Adds the Info.plist keys AR Foundation and the local-network protocol
    /// connection require (implementation plan section 2.4), and repairs the
    /// Swift runtime library search paths the Apple ARKit XR Plug-in adds, so a
    /// regenerated Xcode project never needs either fixed up by hand.
    /// </summary>
    public sealed class ScannerIosPostBuild : IPostprocessBuildWithReport
    {
        internal const string CameraUsageDescription =
            "GhostMap Scanner uses the camera to scan your room in AR.";

        private const string LocalNetworkUsageDescription =
            "GhostMap Scanner sends the scanned room to the Viewer app over your local network.";

        /// <summary>
        /// Linker flags pointing at where the Swift compatibility shims
        /// libUnityARKit.a pulls in actually live. See
        /// <see cref="AddSwiftRuntimeSearchPaths"/>.
        /// </summary>
        private static readonly string[] SwiftRuntimeLinkerFlags =
        {
            "-L$(DT_TOOLCHAIN_DIR)/usr/lib/swift/$(PLATFORM_NAME)",
            "-L$(DT_TOOLCHAIN_DIR)/usr/lib/swift-5.0/$(PLATFORM_NAME)",
        };

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            PlistElementDict root = plist.root;
            root.SetString("NSCameraUsageDescription", CameraUsageDescription);
            root.SetString("NSLocalNetworkUsageDescription", LocalNetworkUsageDescription);

            // The ARKit package's own post-processor also manages this key
            // (it runs after this one, at callbackOrder 1, and rewrites the
            // entry rather than duplicating it). Setting it here as well keeps
            // the requirement explicit and correct even if that processor is
            // compiled out, which is exactly the state that produced an app
            // with no active XR loader before.
            PlistElementArray capabilities = root.values.TryGetValue("UIRequiredDeviceCapabilities", out PlistElement existing)
                ? existing.AsArray()
                : root.CreateArray("UIRequiredDeviceCapabilities");
            if (capabilities.values.TrueForAll(value => value.AsString() != "arkit"))
            {
                capabilities.AddString("arkit");
            }

            plist.WriteToFile(plistPath);

            AddSwiftRuntimeSearchPaths(report.summary.outputPath);
        }

        /// <summary>
        /// libUnityARKit.a contains Swift (RoomCaptureSessionWrapper), so linking
        /// it pulls in libswiftCompatibility51/56/Concurrency. The ARKit package
        /// points the linker at those with $(TOOLCHAIN_DIR), but under Xcode 26
        /// the Metal toolchain is a separately delivered toolchain that Xcode
        /// prepends to TOOLCHAINS, so $(TOOLCHAIN_DIR) resolves to the Metal
        /// toolchain, which ships no Swift runtime. The link then fails with
        /// undefined __swift_FORCE_LOAD_$_swiftCompatibility* symbols.
        ///
        /// $(DT_TOOLCHAIN_DIR) always points at XcodeDefault.xctoolchain, which
        /// is where those archives are. Xcode refuses $(DT_TOOLCHAIN_DIR) inside
        /// LIBRARY_SEARCH_PATHS specifically ("use TOOLCHAIN_DIR instead"), so
        /// the search paths go in as plain -L flags on OTHER_LDFLAGS instead.
        /// These are added alongside the package's own entries rather than
        /// replacing them, so nothing breaks if a later Xcode or ARKit release
        /// resolves $(TOOLCHAIN_DIR) correctly — a duplicate -L is a no-op.
        /// </summary>
        private static void AddSwiftRuntimeSearchPaths(string outputPath)
        {
            string projectPath = PBXProject.GetPBXProjectPath(outputPath);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string[] targetGuids =
            {
                project.GetUnityMainTargetGuid(),
                project.GetUnityFrameworkTargetGuid()
            };

            foreach (string targetGuid in targetGuids)
            {
                foreach (string linkerFlag in SwiftRuntimeLinkerFlags)
                {
                    project.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", linkerFlag);
                }
            }

            project.WriteToFile(projectPath);
        }
    }
}
