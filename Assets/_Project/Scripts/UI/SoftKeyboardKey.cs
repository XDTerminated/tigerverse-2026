using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// One key on the SoftKeyboard. Holds the character and auto-routes its Button click
    /// to SoftKeyboard.AppendChar at runtime (so the wiring survives scene save/reload).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SoftKeyboardKey : MonoBehaviour
    {
        public string character;
        public SoftKeyboard keyboard;
        public KeyAction action = KeyAction.Append;

        public enum KeyAction { Append, Backspace, Clear }

        private void Start()
        {
            var btn = GetComponent<Button>();
            // Re-wire at runtime so we don't depend on serialized listeners.
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnTapped);
        }

        public void OnTapped()
        {
            if (keyboard == null) { Debug.LogWarning("[SoftKeyboardKey] keyboard ref missing"); return; }
            switch (action)
            {
                case KeyAction.Append:    keyboard.AppendChar(character); break;
                case KeyAction.Backspace: keyboard.Backspace(); break;
                case KeyAction.Clear:     keyboard.Clear(); break;
            }
        }
    }
}
