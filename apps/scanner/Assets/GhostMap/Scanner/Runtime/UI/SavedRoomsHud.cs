using System.Collections.Generic;
using GhostMap.Scanner.Export;
using GhostMap.Scanner.Share;
using UnityEngine;
using UnityEngine.UI;

namespace GhostMap.Scanner.UI
{
    /// <summary>
    /// Task R7: "Your rooms" (plan section 19). Lists the rooms saved on this
    /// phone, newest first, each with its own Send to Computer button, so a
    /// room can be sent again at any time, including after Restart.
    ///
    /// A fixed number of row slots built by <c>ScannerSceneBuilder</c>; older
    /// rooms are counted, not shown.
    /// </summary>
    public sealed class SavedRoomsHud : MonoBehaviour
    {
        /// <summary>Rows the scene builder creates.</summary>
        public const int Slots = 5;

        private const float RefreshSeconds = 1f;

        [SerializeField] private ScannerHudController scannerHud;
        [SerializeField] private Button toggleButton;
        [SerializeField] private GameObject panel;
        [SerializeField] private Text emptyText;
        [SerializeField] private Text moreText;
        [SerializeField] private Text statusText;
        [SerializeField] private GameObject[] rows;
        [SerializeField] private Text[] rowLabels;
        [SerializeField] private Button[] rowSendButtons;

        private IReadOnlyList<SavedScan> listed = new List<SavedScan>();
        private float nextRefresh;

        private void Awake()
        {
            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(Toggle);
            }

            for (int i = 0; rowSendButtons != null && i < rowSendButtons.Length; i++)
            {
                int index = i;
                rowSendButtons[i]?.onClick.AddListener(() => Send(index));
            }

            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void Update()
        {
            if (panel == null || !panel.activeSelf || scannerHud == null)
            {
                return;
            }

            if (Time.unscaledTime >= nextRefresh)
            {
                Refresh();
                nextRefresh = Time.unscaledTime + RefreshSeconds;
            }

            ShareController sharing = scannerHud.Sharing;
            bool busy = sharing != null && sharing.State == ShareState.Sharing;

            for (int i = 0; rowSendButtons != null && i < rowSendButtons.Length; i++)
            {
                if (rowSendButtons[i] != null)
                {
                    rowSendButtons[i].interactable = !busy;
                }
            }

            if (statusText != null)
            {
                statusText.text = sharing != null ? sharing.Message : string.Empty;
            }
        }

        private void Toggle()
        {
            if (panel == null)
            {
                return;
            }

            panel.SetActive(!panel.activeSelf);
            nextRefresh = 0f;
        }

        private void Refresh()
        {
            listed = scannerHud.SavedScans != null ? scannerHud.SavedScans.List() : new List<SavedScan>();
            IReadOnlyList<string> labels = Labels(listed, Slots, out int older);

            for (int i = 0; rows != null && i < rows.Length; i++)
            {
                bool shown = i < labels.Count;
                if (rows[i] != null)
                {
                    rows[i].SetActive(shown);
                }

                if (shown && rowLabels != null && i < rowLabels.Length && rowLabels[i] != null)
                {
                    rowLabels[i].text = labels[i];
                }
            }

            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(listed.Count == 0);
            }

            if (moreText != null)
            {
                moreText.gameObject.SetActive(older > 0);
                moreText.text = older == 1 ? "1 older room" : $"{older} older rooms";
            }
        }

        private void Send(int index)
        {
            if (scannerHud != null && index < listed.Count)
            {
                scannerHud.ShareSaved(listed[index].Path);
            }
        }

        /// <summary>The labels for the first <paramref name="slots"/> rooms, and how many more there are.</summary>
        public static IReadOnlyList<string> Labels(IReadOnlyList<SavedScan> scans, int slots, out int older)
        {
            var labels = new List<string>();
            int count = scans?.Count ?? 0;

            for (int i = 0; i < count && i < slots; i++)
            {
                labels.Add(SavedRoomLabel.Describe(scans[i].FileName));
            }

            older = count - labels.Count;
            return labels;
        }
    }
}
