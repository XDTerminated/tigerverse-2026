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

        // Pastel red for validation errors, soft enough to read as "warning"
        // not "fatal", matches the doodle/comic palette better than #DC2626.
        // (Tailwind red-400.)
        private static readonly Color ErrorColor = new Color(0xF8 / 255f, 0x71 / 255f, 0x71 / 255f, 1f);
        // Default subdued black for non-error status messages.
        private static readonly Color StatusColor = new Color(0f, 0f, 0f, 0.6f);

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
                SetStatus("Type a 4-letter code!", isError: true);
                return;
            }

            SetStatus($"Connecting to {code}", isError: false, animated: true);
            gsm.JoinByCode(code);
        }

        // Called externally by HostController when StartHosting fires, to display the code.
        public void ShowHostCode(string code)
        {
            SetStatus($"Code: {code}", isError: false, animated: false);
        }

        private void SetStatus(string text, bool isError, bool animated = false)
        {
            if (statusLabel == null) return;
            statusLabel.color = isError ? ErrorColor : StatusColor;
            var dots = statusLabel.GetComponent<AnimatedDotsLabel>();
            if (dots == null) dots = statusLabel.gameObject.AddComponent<AnimatedDotsLabel>();
            dots.SetMessage(text, animated);
        }
    }
}
