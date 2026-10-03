using UnityEngine;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// Fits this RectTransform to <see cref="Screen.safeArea"/>, so the header
    /// clears the Dynamic Island and the bottom sheet clears the home
    /// indicator. Rechecked every frame because the safe area can change
    /// after launch.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;
        private Rect applied;
        private Vector2Int appliedScreen;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            Rect safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);

            if (safe == applied && screen == appliedScreen)
            {
                return;
            }

            applied = safe;
            appliedScreen = screen;

            if (screen.x <= 0 || screen.y <= 0)
            {
                return;
            }

            rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            rect.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
