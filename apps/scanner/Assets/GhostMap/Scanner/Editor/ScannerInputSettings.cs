using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GhostMap.Scanner.Editor
{
    /// <summary>
    /// Turns on the Input System package backend (Project Settings > Player >
    /// Active Input Handling).
    ///
    /// AR Foundation drives the AR camera with
    /// UnityEngine.InputSystem.XR.TrackedPoseDriver, bound to
    /// &lt;HandheldARInputDevice&gt;/devicePosition and /deviceRotation. Those
    /// controls only exist if the Input System backend is enabled: with Active
    /// Input Handling left on "Input Manager (Old)", ENABLE_INPUT_SYSTEM is not
    /// defined, no XR input device is ever reported, the driver's position and
    /// rotation actions resolve to zero controls, and
    /// TrackedPoseDriver.ReadTrackingStateWithoutTrackingAction falls through to
    /// TrackingStates.None. SetLocalTransform then writes neither position nor
    /// rotation, so the AR camera sits at its authored local pose forever while
    /// ARKit itself tracks perfectly well.
    ///
    /// Like the ARKit loader define, this cannot be flipped during a build:
    /// ENABLE_INPUT_SYSTEM is a built-in define that only reaches compiled code
    /// on the next script compilation, which is why this runs from
    /// <see cref="ScannerBuild.ConfigureXr"/> and not from the build itself.
    /// </summary>
    public static class ScannerInputSettings
    {
        /// <summary>
        /// Active Input Handling = "Both". The new backend is what AR Foundation
        /// needs; the legacy one is kept so that later scanner tasks are free to
        /// use either, and so this changes as little as possible.
        /// </summary>
        public const int InputHandlingBoth = 2;

        /// <summary>Active Input Handling = "Input System Package (New)".</summary>
        public const int InputHandlingNewOnly = 1;

        private const string ProjectSettingsAssetPath = "ProjectSettings/ProjectSettings.asset";
        private const string ActiveInputHandlerProperty = "activeInputHandler";

        /// <summary>
        /// The project's current Active Input Handling value: 0 = Input Manager
        /// (Old), 1 = Input System Package (New), 2 = Both.
        /// </summary>
        public static int ActiveInputHandler
        {
            get
            {
                SerializedProperty property = FindActiveInputHandlerProperty(out _);
                return property.intValue;
            }
        }

        /// <summary>True when the Input System backend is on.</summary>
        public static bool IsInputSystemBackendEnabled =>
            ActiveInputHandler == InputHandlingNewOnly || ActiveInputHandler == InputHandlingBoth;

        /// <summary>
        /// Switches Active Input Handling to "Both" if the Input System backend
        /// is off.
        /// </summary>
        /// <returns>
        /// True when the setting changed, meaning scripts must be recompiled —
        /// and the Editor restarted, if this ran in the Editor rather than in a
        /// batch-mode invocation — before an iOS build can produce a player
        /// whose AR camera is driven by tracking.
        /// </returns>
        public static bool EnableInputSystemBackend()
        {
            SerializedProperty property = FindActiveInputHandlerProperty(out SerializedObject serialized);
            if (property.intValue == InputHandlingNewOnly || property.intValue == InputHandlingBoth)
            {
                return false;
            }

            property.intValue = InputHandlingBoth;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Throws when the Input System backend is off, so a build cannot
        /// silently ship an AR camera that tracking will never move.
        /// </summary>
        public static void VerifyInputSystemBackendEnabled()
        {
            if (!IsInputSystemBackendEnabled)
            {
                throw new BuildFailedException(
                    $"Active Input Handling is {ActiveInputHandler} (Input Manager (Old)). AR Foundation's " +
                    "TrackedPoseDriver needs the Input System backend to see <HandheldARInputDevice>, otherwise the " +
                    $"AR camera transform is never written. Run {ScannerBuild.ConfigureXrMenuPath} first.");
            }
        }

        private static SerializedProperty FindActiveInputHandlerProperty(out SerializedObject serialized)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsAssetPath);
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                throw new BuildFailedException($"Could not load {ProjectSettingsAssetPath}.");
            }

            serialized = new SerializedObject(assets[0]);
            serialized.Update();

            SerializedProperty property = serialized.FindProperty(ActiveInputHandlerProperty);
            if (property == null)
            {
                throw new BuildFailedException(
                    $"'{ActiveInputHandlerProperty}' was not found in {ProjectSettingsAssetPath}. Unity may have " +
                    "renamed it; set Active Input Handling by hand under Project Settings > Player > Other Settings.");
            }

            return property;
        }
    }
}
