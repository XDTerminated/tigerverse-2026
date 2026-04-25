using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Tigerverse.Core;

namespace Tigerverse.UI
{
    /// <summary>
    /// Wires the JOIN button + CodeInput field to GameStateManager.JoinByCode.
    /// Reads the input text at click/end-edit time, normalises (upper-case + trim).
    /// Also displays the host code on the title canvas after HOST is pressed.
    /// </summary>
    public class JoinController : MonoBehaviour
    {
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private Button joinButton;
        [SerializeField] private GameStateManager gsm;
        [SerializeField] private TMP_Text statusLabel; // optional, shows "Hosting code: ABCD" or "Joining ABCD..."

        private void Awake()
        {
            if (gsm == null)
            {
                gsm = FindFirstObjectByType<GameStateManager>();
            }

            if (joinButton != null)
            {
                joinButton.onClick.RemoveAllListeners();
                joinButton.onClick.AddListener(OnJoinClicked);
            }
            if (codeInput != null)
            {
                codeInput.onEndEdit.RemoveAllListeners();
                codeInput.onEndEdit.AddListener(_ => OnJoinClicked());
                codeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            }
        }

        public void OnJoinClicked()
        {
            if (gsm == null) { Debug.LogError("[JoinController] GameStateManager missing."); return; }
            string raw = codeInput != null ? codeInput.text : string.Empty;
            string code = (raw ?? "").Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(code) || code.Length < 4)
            {
                Debug.LogWarning($"[JoinController] Code too short: '{raw}'");
                if (statusLabel != null) statusLabel.text = "Type a 4-letter code";
                return;
            }

            if (statusLabel != null) statusLabel.text = $"Joining {code}...";
            gsm.JoinByCode(code);
        }

        // Called externally by HostController when StartHosting fires, to display the code.
        public void ShowHostCode(string code)
        {
            if (statusLabel != null) statusLabel.text = $"Code: {code}";
        }
    }
}
