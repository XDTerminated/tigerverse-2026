using System.Collections;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Visual hit-feedback layered on top of the existing HP-changed haptics:
    /// brief red tint quad in front of the local camera + subtle XR rig
    /// position nudge using Perlin noise. Intensity scales with damage size.
    /// VR-safe: keeps shake amplitude tiny (sub-cm) so motion sickness isn't
    /// a concern. Static Trigger so any system can fire it without holding a
    /// component reference.
    /// </summary>
    [DisallowMultipleComponent]
    public class HitImpactFx : MonoBehaviour
    {
        private static HitImpactFx _instance;

        public static void Trigger(float damageAmount)
        {
            if (_instance == null)
            {
                var go = new GameObject("HitImpactFx");
                _instance = go.AddComponent<HitImpactFx>();
            }
            _instance.PlayHit(damageAmount);
        }

        private void PlayHit(float damage)
        {
            // Map damage to intensity 0…1 (capped). Scrape damage of 1-2 still
            // gets a visible flash, big-30+ damage maxes out the screen.
            float intensity = Mathf.Clamp01(damage / 30f);
            StartCoroutine(FlashCoroutine(intensity));
            StartCoroutine(ShakeCoroutine(intensity));
        }

        // ─── Flash ──────────────────────────────────────────────────────
        private IEnumerator FlashCoroutine(float intensity)
        {
            var cam = Camera.main;
            if (cam == null) yield break;

            // Spawn a quad ~0.5 m in front of the near clip plane, sized so
            // it fills the field of view at that distance. Material is
            // unlit transparent red.
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "HitFlash";
            var col = quad.GetComponent<Collider>(); if (col != null) Destroy(col);
            quad.transform.SetParent(cam.transform, false);
            float zDist = Mathf.Max(0.15f, cam.nearClipPlane + 0.05f);
            quad.transform.localPosition = new Vector3(0f, 0f, zDist);
            quad.transform.localRotation = Quaternion.identity;
            // Quad is 1×1 facing +Z; size to fill FOV at zDist.
            float halfH = zDist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            quad.transform.localScale = new Vector3(halfW * 2.4f, halfH * 2.4f, 1f);

            var sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            mat.mainTexture = Tex1x1();
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.GetComponent<Renderer>().receiveShadows = false;

            float startA = Mathf.Lerp(0.18f, 0.45f, intensity);
            const float dur = 0.30f;
            float t = 0f;
            while (t < dur && quad != null)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float a = startA * (1f - p);
                Color c = new Color(0.9f, 0.10f, 0.10f, a);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                else mat.color = c;
                yield return null;
            }
            if (quad != null) Destroy(quad);
        }

        // ─── Shake ──────────────────────────────────────────────────────
        private IEnumerator ShakeCoroutine(float intensity)
        {
            // Shake the XR rig (XROrigin) rather than the head camera —
            // moving the camera directly under a player in VR causes
            // immediate motion sickness. Rig-shake at sub-cm amplitude is
            // perceptible without being uncomfortable.
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null) yield break;
            Transform t = origin.transform;
            Vector3 baseLocal = t.localPosition;

            float amp = Mathf.Lerp(0.012f, 0.040f, intensity);
            const float dur = 0.18f;
            const float freq = 30f;
            float elapsed = 0f;
            float seed = Random.value * 100f;
            while (elapsed < dur && t != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(elapsed / dur);
                float decay = 1f - p;
                float ox = (Mathf.PerlinNoise(seed + elapsed * freq, 0f) * 2f - 1f) * amp * decay;
                float oz = (Mathf.PerlinNoise(0f, seed + elapsed * freq) * 2f - 1f) * amp * decay;
                t.localPosition = baseLocal + new Vector3(ox, 0f, oz);
                yield return null;
            }
            if (t != null) t.localPosition = baseLocal;
        }

        private static Texture2D _white;
        private static Texture2D Tex1x1()
        {
            if (_white != null) return _white;
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
            return _white;
        }
    }
}
