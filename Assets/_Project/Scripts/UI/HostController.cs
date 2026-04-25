using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Tigerverse.Core;

namespace Tigerverse.UI
{
    /// <summary>
    /// Wires the HOST button to GameStateManager.StartHosting and updates the
    /// status label with the generated 4-letter code.
    /// </summary>
    public class HostController : MonoBehaviour
    {
        [SerializeField] private Button hostButton;
        [SerializeField] private GameStateManager gsm;
        [SerializeField] private TMP_Text statusLabel;

        private void Awake()
        {
            if (gsm == null)
            {
                gsm = FindFirstObjectByType<GameStateManager>();
            }
            if (hostButton != null)
            {
                hostButton.onClick.RemoveAllListeners();
                hostButton.onClick.AddListener(OnHostClicked);
            }
        }

        public void OnHostClicked()
        {
            if (gsm == null) { Debug.LogError("[HostController] GameStateManager missing."); return; }
            if (statusLabel != null) statusLabel.text = "Connecting...";
            gsm.StartHosting();
            // Code is generated synchronously inside StartHosting; show it immediately.
            if (statusLabel != null && !string.IsNullOrEmpty(gsm.sessionCode))
            {
                statusLabel.text = $"Code: {gsm.sessionCode}\n(give this to your friend)";
            }
        }
    }
}
