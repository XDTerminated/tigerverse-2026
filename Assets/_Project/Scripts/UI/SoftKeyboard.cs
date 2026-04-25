using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Minimal in-VR soft keyboard. Displays a grid of character buttons; tapping appends
    /// to the target TMP_InputField. Use for typing the 4-letter session code.
    /// </summary>
    public class SoftKeyboard : MonoBehaviour
    {
        [SerializeField] private TMP_InputField target;
        [SerializeField] private int maxLength = 4;

        public void SetTarget(TMP_InputField field) { target = field; }

        public void AppendChar(string c)
        {
            if (target == null || string.IsNullOrEmpty(c)) return;
            string current = target.text ?? "";
            if (current.Length >= maxLength) return;
            target.text = (current + c).ToUpperInvariant();
        }

        public void Backspace()
        {
            if (target == null) return;
            string current = target.text ?? "";
            if (current.Length == 0) return;
            target.text = current.Substring(0, current.Length - 1);
        }

        public void Clear()
        {
            if (target != null) target.text = "";
        }
    }
}
