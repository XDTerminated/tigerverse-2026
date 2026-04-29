using TMPro;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Small helper that drives a TMP label with a base message + animated
    /// trailing dots ("Connecting" → "Connecting." → "Connecting.." →
    /// "Connecting..."). Set Message via SetMessage; pass null/empty to clear.
    /// Pass a no-dot message (e.g. "Code: ABCD") with animated=false to show
    /// static text. Lives on the same GameObject as the TMP label.
    /// </summary>
    [DisallowMultipleComponent]
    public class AnimatedDotsLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private float stepSeconds = 0.40f;
        [SerializeField] private int maxDots = 3;

        private string _baseText = string.Empty;
        private bool _animated;
        private float _phase;
        private int _lastDots = -1;

        private void Awake()
        {
            if (label == null) label = GetComponent<TMP_Text>();
        }

        public void SetMessage(string text, bool animated = true)
        {
            _baseText = text ?? string.Empty;
            _animated = animated;
            _phase = 0f;
            _lastDots = -1;
            Render();
        }

        public void Clear()
        {
            _baseText = string.Empty;
            _animated = false;
            if (label != null) label.text = string.Empty;
        }

        private void Update()
        {
            if (!_animated || string.IsNullOrEmpty(_baseText) || label == null) return;
            _phase += Time.unscaledDeltaTime;
            int dots = Mathf.FloorToInt(_phase / stepSeconds) % (maxDots + 1);
            if (dots == _lastDots) return;
            _lastDots = dots;
            Render();
        }

        private void Render()
        {
            if (label == null) return;
            if (string.IsNullOrEmpty(_baseText)) { label.text = string.Empty; return; }
            if (!_animated) { label.text = _baseText; return; }
            int dots = Mathf.Clamp(_lastDots, 0, maxDots);
            label.text = _baseText + new string('.', dots);
        }
    }
}
