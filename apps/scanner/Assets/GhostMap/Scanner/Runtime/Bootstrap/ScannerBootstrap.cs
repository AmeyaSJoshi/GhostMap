using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace GhostMap.Scanner.Bootstrap
{
    /// <summary>
    /// Task S1 physical-device smoke test readout. Shows AR session state, the
    /// not-tracking reason, live camera pose and whether a horizontal (floor
    /// candidate) plane has been detected, so the smoke test in
    /// docs/plans/ghostmap-implementation-plan.md can be verified by looking at
    /// the phone screen alone.
    ///
    /// The XR block reports the configured loader list separately from the
    /// active loader, and whether the Apple ARKit XR Plug-in's native code was
    /// actually compiled into this player. Those three facts distinguish the
    /// failure modes that all otherwise read as "XR loader: none" on device:
    /// no XR settings shipped at all, settings shipped with an empty loader
    /// list, and ARKitLoader configured but unable to initialize because the
    /// plug-in was compiled to stubs without UNITY_XR_ARKIT_LOADER_ENABLED.
    /// </summary>
    public sealed class ScannerBootstrap : MonoBehaviour
    {
        [SerializeField] private ARSession arSession;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Text diagnosticsText;

        private readonly StringBuilder builder = new StringBuilder();

        private void Reset()
        {
            arSession = FindFirstObjectByType<ARSession>();
            planeManager = FindFirstObjectByType<ARPlaneManager>();
            arCamera = Camera.main;
        }

        private void Update()
        {
            if (diagnosticsText == null)
            {
                return;
            }

            ARSessionState state = ARSession.state;
            NotTrackingReason reason = ARSession.notTrackingReason;

            int planeCount = 0;
            bool hasFloorCandidate = false;
            if (planeManager != null)
            {
                foreach (ARPlane plane in planeManager.trackables)
                {
                    planeCount++;
                    if (plane.alignment == PlaneAlignment.HorizontalUp)
                    {
                        hasFloorCandidate = true;
                    }
                }
            }

            Vector3 position = arCamera != null ? arCamera.transform.position : Vector3.zero;
            Vector3 eulerAngles = arCamera != null ? arCamera.transform.eulerAngles : Vector3.zero;

            builder.Clear();
            AppendXrDiagnostics(builder);
            builder.Append("Session: ").Append(state).Append('\n');
            builder.Append("Not tracking reason: ").Append(reason).Append('\n');
            builder.AppendFormat("Camera pos: ({0:F2}, {1:F2}, {2:F2})\n", position.x, position.y, position.z);
            builder.AppendFormat("Camera rot: ({0:F1}, {1:F1}, {2:F1})\n", eulerAngles.x, eulerAngles.y, eulerAngles.z);
            builder.Append("Planes: ").Append(planeCount)
                .Append(" (floor candidate: ").Append(hasFloorCandidate ? "yes" : "no").Append(")");

            diagnosticsText.text = builder.ToString();
        }

        private static void AppendXrDiagnostics(StringBuilder target)
        {
#if UNITY_XR_ARKIT_LOADER_ENABLED
            const string arkitNative = "compiled in";
#else
            const string arkitNative = "MISSING (UNITY_XR_ARKIT_LOADER_ENABLED not defined)";
#endif

            XRGeneralSettings settings = XRGeneralSettings.Instance;
            if (settings == null)
            {
                target.Append("XR settings: not in build\n");
                target.Append("Configured loaders: unknown\n");
                target.Append("Active loader: none\n");
                target.Append("ARKit native: ").Append(arkitNative).Append('\n');
                return;
            }

            XRManagerSettings manager = settings.Manager;
            if (manager == null)
            {
                target.Append("XR settings: present, no manager\n");
                target.Append("Configured loaders: unknown\n");
                target.Append("Active loader: none\n");
                target.Append("ARKit native: ").Append(arkitNative).Append('\n');
                return;
            }

            target.Append("XR settings: present (init on start: ")
                .Append(settings.InitManagerOnStart ? "yes" : "no")
                .Append(", init complete: ")
                .Append(manager.isInitializationComplete ? "yes" : "no")
                .Append(")\n");

            target.Append("Configured loaders: [");
            IReadOnlyList<XRLoader> loaders = manager.activeLoaders;
            for (int i = 0; i < loaders.Count; i++)
            {
                if (i > 0)
                {
                    target.Append(", ");
                }

                target.Append(loaders[i] == null ? "<null>" : loaders[i].GetType().Name);
            }

            target.Append("]\n");

            target.Append("Active loader: ")
                .Append(manager.activeLoader == null ? "none" : manager.activeLoader.GetType().Name)
                .Append('\n');

            target.Append("ARKit native: ").Append(arkitNative).Append('\n');
        }
    }
}
