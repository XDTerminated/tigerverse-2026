using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class HitFlashController
    {
        private static readonly Dictionary<Transform, Coroutine> _activeFlashes = new Dictionary<Transform, Coroutine>();
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int _colorId = Shader.PropertyToID("_Color");

        public static void Flash(Transform defender, ElementType element, float duration, MonoBehaviour coroutineRunner)
        {
            if (defender == null || coroutineRunner == null) return;
            if (duration <= 0f) return;

            if (_activeFlashes.TryGetValue(defender, out Coroutine existing))
            {
                if (existing != null) coroutineRunner.StopCoroutine(existing);
                _activeFlashes.Remove(defender);
            }

            Coroutine co = coroutineRunner.StartCoroutine(FlashRoutine(defender, element, duration));
            _activeFlashes[defender] = co;
        }

        private static IEnumerator FlashRoutine(Transform defender, ElementType element, float duration)
        {
            Renderer[] renderers = defender.GetComponentsInChildren<Renderer>(false);
            if (renderers == null || renderers.Length == 0)
            {
                _activeFlashes.Remove(defender);
                yield break;
            }

            List<Renderer> tracked = new List<Renderer>(renderers.Length);
            List<Color> originals = new List<Color>(renderers.Length);
            List<int> propIds = new List<int>(renderers.Length);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                Material sm = r.sharedMaterial;
                if (sm == null) continue;

                int pid;
                Color baseCol;
                if (sm.HasProperty(_baseColorId))
                {
                    pid = _baseColorId;
                    baseCol = sm.GetColor(_baseColorId);
                }
                else if (sm.HasProperty(_colorId))
                {
                    pid = _colorId;
                    baseCol = sm.GetColor(_colorId);
                }
                else
                {
                    continue;
                }

                tracked.Add(r);
                originals.Add(baseCol);
                propIds.Add(pid);
            }

            if (tracked.Count == 0)
            {
                _activeFlashes.Remove(defender);
                yield break;
            }

            Color tint = ElementTint(element);
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (defender == null) yield break;

                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < tracked.Count; i++)
                {
                    Renderer r = tracked[i];
                    if (r == null) continue;

                    Color current;
                    if (t < 0.4f)
                    {
                        current = Color.white;
                    }
                    else
                    {
                        float k = (t - 0.4f) / 0.6f;
                        Color whiteToTint = Color.Lerp(Color.white, tint, k);
                        current = Color.Lerp(whiteToTint, originals[i], k);
                    }

                    r.GetPropertyBlock(mpb);
                    mpb.SetColor(propIds[i], current);
                    r.SetPropertyBlock(mpb);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (defender != null)
            {
                for (int i = 0; i < tracked.Count; i++)
                {
                    Renderer r = tracked[i];
                    if (r == null) continue;
                    r.SetPropertyBlock(null);
                }
            }

            _activeFlashes.Remove(defender);
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
                case ElementType.Neutral:
                default: return new Color(1.00f, 0.95f, 0.85f);
            }
        }
    }
}
