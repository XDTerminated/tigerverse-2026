using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class MoveNameBanner
    {
        public static void Show(string moveName, ElementType element, Vector3 worldPos, float duration, MonoBehaviour coroutineRunner)
        {
            if (coroutineRunner == null) return;
            if (string.IsNullOrEmpty(moveName)) return;

            GameObject root = new GameObject("MoveNameBanner");
            root.transform.position = worldPos + Vector3.up * 0.9f;
            root.transform.localScale = Vector3.one * 0.01f;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>();

            RectTransform rootRT = root.GetComponent<RectTransform>();
            if (rootRT != null)
            {
                rootRT.sizeDelta = new Vector2(3.0f, 0.7f);
            }

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(root.transform, false);
            TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = moveName.ToUpperInvariant();
            tmp.fontSize = 56;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = ElementTint(element);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.outlineColor = Color.black;
            tmp.outlineWidth = 0.2f;

            RectTransform textRT = tmp.rectTransform;
            textRT.sizeDelta = new Vector2(3.0f, 0.7f);
            textRT.anchoredPosition = Vector2.zero;

            coroutineRunner.StartCoroutine(Animate(root, tmp, duration));
        }

        private static IEnumerator Animate(GameObject root, TextMeshProUGUI tmp, float duration)
        {
            Vector3 basePos = root.transform.position;
            float baseScale = 0.01f;
            float t = 0f;
            float popDuration = 0.18f;
            float holdEnd = Mathf.Max(popDuration, duration * 0.7f);

            Color baseColor = tmp.color;

            while (t < duration && root != null)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 fwd = root.transform.position - cam.transform.position;
                    if (fwd.sqrMagnitude > 0.0001f)
                    {
                        root.transform.rotation = Quaternion.LookRotation(fwd, cam.transform.up);
                    }
                }

                float scaleMul;
                if (t < popDuration)
                {
                    float half = popDuration * 0.5f;
                    if (t < half)
                    {
                        scaleMul = Mathf.Lerp(0f, 1.2f, t / half);
                    }
                    else
                    {
                        scaleMul = Mathf.Lerp(1.2f, 1.0f, (t - half) / half);
                    }
                }
                else
                {
                    scaleMul = 1.0f;
                }

                root.transform.localScale = Vector3.one * baseScale * scaleMul;

                float driftN;
                float alpha;
                if (t < popDuration)
                {
                    driftN = 0f;
                    alpha = 1f;
                }
                else if (t < holdEnd)
                {
                    driftN = (t - popDuration) / Mathf.Max(0.0001f, (holdEnd - popDuration));
                    alpha = 1f;
                }
                else
                {
                    driftN = 1f + (t - holdEnd) / Mathf.Max(0.0001f, (duration - holdEnd));
                    alpha = 1f - (t - holdEnd) / Mathf.Max(0.0001f, (duration - holdEnd));
                }

                root.transform.position = basePos + new Vector3(0f, 0.15f * driftN, 0f);
                tmp.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(alpha));

                t += Time.deltaTime;
                yield return null;
            }

            if (root != null)
            {
                Object.Destroy(root);
            }
        }

        private static Color ElementTint(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire: return new Color(1.00f, 0.45f, 0.20f);
                case ElementType.Water: return new Color(0.30f, 0.60f, 1.00f);
                case ElementType.Electric: return new Color(1.00f, 0.95f, 0.30f);
                case ElementType.Earth: return new Color(0.55f, 0.40f, 0.25f);
                case ElementType.Grass: return new Color(0.40f, 0.85f, 0.40f);
                case ElementType.Ice: return new Color(0.70f, 0.95f, 1.00f);
                case ElementType.Dark: return new Color(0.45f, 0.25f, 0.55f);
                default: return new Color(1.00f, 0.95f, 0.85f);
            }
        }
    }
}
