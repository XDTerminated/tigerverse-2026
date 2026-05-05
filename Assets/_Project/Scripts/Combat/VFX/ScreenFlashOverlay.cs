using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class ScreenFlashOverlay
    {
        private static GameObject _activeOverlay;
        private static Coroutine _activeFade;

        public static void Flash(ElementType element, float intensity, float duration, MonoBehaviour coroutineRunner)
        {
            if (coroutineRunner == null) return;
            if (Camera.main == null) return;

            if (_activeOverlay != null)
            {
                if (_activeFade != null) coroutineRunner.StopCoroutine(_activeFade);
                Object.Destroy(_activeOverlay);
                _activeOverlay = null;
                _activeFade = null;
            }

            GameObject root = new GameObject("BattleScreenFlash");
            Object.DontDestroyOnLoad(root);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            GameObject tintGO = new GameObject("Tint");
            tintGO.transform.SetParent(root.transform, false);

            RectTransform rt = tintGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = tintGO.AddComponent<Image>();
            img.raycastTarget = false;
            Color tint = ElementTint(element);
            tint.a = 0f;
            img.color = tint;

            _activeOverlay = root;
            _activeFade = coroutineRunner.StartCoroutine(FadeRoutine(img, ElementTint(element), Mathf.Clamp01(intensity), Mathf.Max(0.01f, duration)));
        }

        private static IEnumerator FadeRoutine(Image img, Color baseColor, float peak, float duration)
        {
            float inDur = duration * 0.15f;
            float outDur = duration * 0.85f;

            float t = 0f;
            while (t < inDur)
            {
                if (_activeOverlay == null || img == null) yield break;
                t += Time.unscaledDeltaTime;
                float a = Mathf.Lerp(0f, peak, inDur > 0f ? t / inDur : 1f);
                Color c = baseColor; c.a = a;
                img.color = c;
                yield return null;
            }

            t = 0f;
            while (t < outDur)
            {
                if (_activeOverlay == null || img == null) yield break;
                t += Time.unscaledDeltaTime;
                float a = Mathf.Lerp(peak, 0f, outDur > 0f ? t / outDur : 1f);
                Color c = baseColor; c.a = a;
                img.color = c;
                yield return null;
            }

            if (_activeOverlay != null)
            {
                Object.Destroy(_activeOverlay);
                _activeOverlay = null;
            }
            _activeFade = null;
        }

        private static Color ElementTint(ElementType element)
        {
            switch (element)
            {
                case ElementType.Fire:     return new Color(1.00f, 0.45f, 0.20f, 1f);
                case ElementType.Water:    return new Color(0.30f, 0.60f, 1.00f, 1f);
                case ElementType.Electric: return new Color(1.00f, 0.95f, 0.30f, 1f);
                case ElementType.Earth:    return new Color(0.55f, 0.40f, 0.25f, 1f);
                case ElementType.Grass:    return new Color(0.40f, 0.85f, 0.40f, 1f);
                case ElementType.Ice:      return new Color(0.70f, 0.95f, 1.00f, 1f);
                case ElementType.Dark:     return new Color(0.45f, 0.25f, 0.55f, 1f);
                default:                   return new Color(1.00f, 0.95f, 0.85f, 1f);
            }
        }
    }
}
