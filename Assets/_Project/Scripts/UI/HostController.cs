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
            var dots = statusLabel != null ? statusLabel.GetComponent<AnimatedDotsLabel>() : null;
            if (dots == null && statusLabel != null) dots = statusLabel.gameObject.AddComponent<AnimatedDotsLabel>();

            if (dots != null) dots.SetMessage("Hosting", animated: true);
            else if (statusLabel != null) statusLabel.text = "Hosting...";

            gsm.StartHosting();

            // Code is generated synchronously inside StartHosting; once we
            // have it, swap the dot-animated "Hosting…" copy for a static
            // "Code: ABCD" line so the player can read it out loud.
            if (statusLabel != null && !string.IsNullOrEmpty(gsm.sessionCode))
            {
                if (dots != null) dots.SetMessage($"Code: {gsm.sessionCode}\n(give this to your friend)", animated: false);
                else statusLabel.text = $"Code: {gsm.sessionCode}\n(give this to your friend)";
            }
        }
    }
}
