using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GhostMap.Scanner.Share
{
    /// <summary>
    /// Task R7: the iOS share sheet, through the native plugin
    /// <c>Assets/Plugins/iOS/GhostMapShare/GhostMapShare.mm</c>. The plugin
    /// reports back with <c>UnitySendMessage</c>, which needs a GameObject by
    /// name, so <see cref="Create"/> makes one at runtime; nothing in the scene
    /// depends on it.
    /// </summary>
    public sealed class IosShareSheet : MonoBehaviour, IShareSheet
    {
        public const string GameObjectName = "GhostMapShareSheet";
        private const string CallbackMethod = nameof(OnShareFinished);

        private Action<ShareResult> pending;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void GhostMapShare_ShareFile(string path, string callbackObject, string callbackMethod);

        public bool IsAvailable => true;
#else
        public bool IsAvailable => false;
#endif

        public static IosShareSheet Create()
        {
            var go = new GameObject(GameObjectName);
            DontDestroyOnLoad(go);
            return go.AddComponent<IosShareSheet>();
        }

        public void Share(string filePath, Action<ShareResult> onFinished)
        {
#if UNITY_IOS && !UNITY_EDITOR
            pending = onFinished;
            GhostMapShare_ShareFile(filePath, gameObject.name, CallbackMethod);
#else
            onFinished?.Invoke(new ShareResult(false, string.Empty, "The iOS share sheet exists only on an iPhone."));
#endif
        }

        /// <summary>Called by the native plugin through <c>UnitySendMessage</c>.</summary>
        public void OnShareFinished(string message)
        {
            Action<ShareResult> callback = pending;
            pending = null;

            if (!ShareResult.TryParse(message, out ShareResult result))
            {
                result = new ShareResult(false, string.Empty, "The share sheet sent an unreadable reply.");
            }

            Debug.Log($"GhostMap share: {result}");
            callback?.Invoke(result);
        }
    }
}
