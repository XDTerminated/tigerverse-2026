using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Mirrors the website's button hover behavior: white background + black
    /// content (text + icons) in the default state, black background + white
    /// content on hover/press.
    ///
    /// Unity's built-in Button color tint only affects the background Image,
    /// not the child label or icon, so black-on-white content becomes invisible
    /// the moment the background goes black. This helper flips every child
    /// TMP_Text and Image (other than the button's own background) in step
    /// with the Selectable's state.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class TigerverseHoverFlip : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISubmitHandler
    {
        public Color normalColor = Color.black;
        public Color hoverColor = Color.white;

        [Tooltip("Scale applied while the button is pressed (matches the website's active:scale-90).")]
        public float pressedScale = 0.92f;

        [Tooltip("Smoothing time for scale tween in seconds.")]
        public float scaleTweenSeconds = 0.08f;

        private readonly List<TMP_Text> _labels = new List<TMP_Text>();
        private readonly List<Image> _icons = new List<Image>();
        private bool _hovered;
        private bool _pressed;
        private Vector3 _baseScale = Vector3.one;
        private float _scaleVel;
        private float _targetScale = 1f;
        private float _currentScale = 1f;

        private void Awake()
        {
            _baseScale = transform.localScale;
            // Treat any non-uniform/non-1 scale set externally (e.g. by a
            // toggle group) as the new "rest" scale.
            _currentScale = _targetScale = 1f;
            Refresh();
            Apply();
        }
        private void OnEnable() => Apply();

        public void OnPointerEnter(PointerEventData e) { _hovered = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { _hovered = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { _pressed = true; Apply(); }
        public void OnPointerUp(PointerEventData e)
        {
            _pressed = false; Apply();
            // Drop EventSystem focus immediately so the Selectable's "selected"
            // tint doesn't latch the button in the inverted state after a
            // click. Website buttons are momentary, not focusable.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }
        public void OnSubmit(BaseEventData e) { /* no-op: prevent persistent select tint */ }

        private void Update()
        {
            // Smooth scale tween toward target. SmoothDamp gives a snappy feel
            // matching the website's 100ms transition.
            if (!Mathf.Approximately(_currentScale, _targetScale))
            {
                _currentScale = Mathf.SmoothDamp(_currentScale, _targetScale, ref _scaleVel, scaleTweenSeconds);
                transform.localScale = _baseScale * _currentScale;
            }
        }

        /// <summary>
        /// Re-scans children. Called automatically in Awake; expose publicly so
        /// procedural builders can invoke it after attaching the icon child.
        /// </summary>
        public void Refresh()
        {
            _labels.Clear();
            _icons.Clear();
            GetComponentsInChildren(true, _labels);

            // Skip the button's own background Image — Unity's color-tint
            // already inverts that one. Anything else (Icon child Images,
            // dividers, badges) is treated as foreground content.
            var ownBg = GetComponent<Image>();
            var allImgs = GetComponentsInChildren<Image>(true);
            foreach (var img in allImgs)
            {
                if (img == ownBg) continue;
                _icons.Add(img);
            }
        }

        private void Apply()
        {
            var c = (_hovered || _pressed) ? hoverColor : normalColor;
            for (int i = 0; i < _labels.Count; i++)
                if (_labels[i] != null) _labels[i].color = c;
            for (int i = 0; i < _icons.Count; i++)
                if (_icons[i] != null) _icons[i].color = c;
            _targetScale = _pressed ? pressedScale : 1f;
        }
    }
}
