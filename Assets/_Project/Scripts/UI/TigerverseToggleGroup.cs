using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// Mirrors the website's pencil/eraser toggle: a list of buttons where
    /// exactly one is "selected" at all times. The selected option renders
    /// inverted (filled black background, white content) and is slightly
    /// scaled up; the others render in the normal outline state and respond
    /// to hover via <see cref="TigerverseHoverFlip"/>.
    ///
    /// Wire this on the parent container of the toggle buttons. Drop each
    /// child Button into <see cref="options"/>, set <see cref="selectedIndex"/>
    /// for the default. Clicking any button selects it and deselects the
    /// previous one. <see cref="selectedScale"/> is multiplicative on the
    /// button's localScale so you can tweak the "pop" without touching the
    /// rect.
    /// </summary>
    public class TigerverseToggleGroup : MonoBehaviour
    {
        [Tooltip("Buttons in this toggle group. Selecting one deselects all others.")]
        public List<Button> options = new List<Button>();

        [Tooltip("Index of the option that is selected by default.")]
        public int selectedIndex = 0;

        [Tooltip("Scale applied to the selected button (relative to siblings).")]
        public float selectedScale = 1.04f;

        [Tooltip("Sprite used to draw the outer ring around the selected button.")]
        public Sprite ringSprite;

        [Tooltip("Distance in canvas units between the button border and the ring border (matches Tailwind ring-offset).")]
        public float ringOffset = 6f;

        [Tooltip("Color of the ring drawn around the selected option.")]
        public Color ringColor = Color.black;

        private Image _ring;

        private void Awake()
        {
            EnsureRing();
            for (int i = 0; i < options.Count; i++)
            {
                int captured = i;
                if (options[i] == null) continue;
                options[i].onClick.AddListener(() => Select(captured));
            }
            ApplyVisuals();
        }

        public void Select(int index)
        {
            if (index < 0 || index >= options.Count) return;
            if (selectedIndex == index) return;
            selectedIndex = index;
            ApplyVisuals();
        }

        public void ForceApply() => ApplyVisuals();

        private void EnsureRing()
        {
            if (_ring != null) return;
            var existing = transform.Find("SelectedRing");
            if (existing != null) { _ring = existing.GetComponent<Image>(); return; }

            var go = new GameObject("SelectedRing", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            // Sibling index 0 → renders BEFORE the buttons so it sits behind
            // them. Children render after their parent in Unity UI, so this
            // sibling-ordering trick is the only way to put art behind a peer.
            go.transform.SetSiblingIndex(0);
            _ring = go.GetComponent<Image>();
            _ring.raycastTarget = false;
            _ring.type = Image.Type.Sliced;
            _ring.color = ringColor;

            // Auto-load the project's outlined panel sprite if no sprite was
            // assigned. The outline-with-transparent-center geometry naturally
            // gives the "ring with hollow gap" look we want.
            if (ringSprite == null)
            {
#if UNITY_EDITOR
                ringSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(TigerverseTheme.PanelSpritePath);
#endif
            }
            if (ringSprite != null) _ring.sprite = ringSprite;
        }

        private void ApplyVisuals()
        {
            EnsureRing();

            for (int i = 0; i < options.Count; i++)
            {
                var btn = options[i];
                if (btn == null) continue;
                bool isSelected = i == selectedIndex;
                ApplyState(btn, isSelected);
            }

            // Position the ring around the selected button. When nothing is
            // selected, hide it.
            if (selectedIndex < 0 || selectedIndex >= options.Count || options[selectedIndex] == null)
            {
                _ring.enabled = false;
                return;
            }
            var selRT = (RectTransform)options[selectedIndex].transform;
            var ringRT = (RectTransform)_ring.transform;
            ringRT.anchorMin = selRT.anchorMin;
            ringRT.anchorMax = selRT.anchorMax;
            ringRT.pivot = selRT.pivot;
            ringRT.anchoredPosition = selRT.anchoredPosition;
            ringRT.localScale = selRT.localScale;
            ringRT.sizeDelta = selRT.sizeDelta + new Vector2(ringOffset * 2f, ringOffset * 2f);
            _ring.color = ringColor;
            if (ringSprite != null) _ring.sprite = ringSprite;
            _ring.enabled = true;
        }

        private void ApplyState(Button btn, bool selected)
        {
            // Background: selected = filled black, unselected = white outline.
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = selected ? Color.black : Color.white;

            // Content (icon + label): inverted from background.
            var contentColor = selected ? Color.white : Color.black;
            var ownBg = img;
            foreach (var inner in btn.GetComponentsInChildren<Image>(true))
            {
                if (inner == ownBg) continue;
                inner.color = contentColor;
            }
            foreach (var t in btn.GetComponentsInChildren<TMP_Text>(true))
                t.color = contentColor;

            // Slight scale-up on selected, mirroring the website's scale-110.
            btn.transform.localScale = selected ? Vector3.one * selectedScale : Vector3.one;

            // While selected, the button shouldn't respond to hover (it's
            // already in the inverted state). Disable HoverFlip so it doesn't
            // overwrite our colors.
            var flip = btn.GetComponent<TigerverseHoverFlip>();
            if (flip != null) flip.enabled = !selected;

            // Make the Selectable's normal/highlight tints match the new
            // resting background so Unity's built-in hover doesn't flip the
            // selected option back to white when the cursor enters it.
            var colors = btn.colors;
            if (selected)
            {
                colors.normalColor = Color.black;
                colors.highlightedColor = Color.black;
                colors.pressedColor = Color.black;
                colors.selectedColor = Color.black;
            }
            else
            {
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.black;
                colors.pressedColor = Color.black;
                colors.selectedColor = Color.black;
            }
            btn.colors = colors;
        }
    }
}
