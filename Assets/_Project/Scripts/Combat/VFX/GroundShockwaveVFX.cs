using UnityEngine;
using System.Collections;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class GroundShockwaveVFX
    {
        private const int Segments = 48;

        public static void Spawn(Vector3 groundPos, ElementType element, float duration, MonoBehaviour coroutineRunner)
        {
            if (coroutineRunner == null) return;
            if (duration <= 0f) duration = 0.4f;

            GameObject go = new GameObject("GroundShockwave_" + element.ToString());
            go.transform.position = groundPos + Vector3.up * 0.02f;
            go.transform.localScale = Vector3.one * 0.05f;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.useWorldSpace = false;
            lr.positionCount = Segments;
            lr.startWidth = 0.06f;
            lr.endWidth = 0.06f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;

            Vector3[] positions = new Vector3[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * 2f * Mathf.PI / Segments;
                positions[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            lr.SetPositions(positions);

            Color tint = ElementTint(element);

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            Material mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            lr.material = mat;

            Color start = tint; start.a = 1f;
            lr.startColor = start;
            lr.endColor = start;

            coroutineRunner.StartCoroutine(Animate(go, lr, mat, tint, duration));
        }

        private static IEnumerator Animate(GameObject go, LineRenderer lr, Material mat, Color tint, float duration)
        {
            float t = 0f;
            const float startRadius = 0.05f;
            const float endRadius = 1.5f;
            const float startWidth = 0.06f;
            const float endWidth = 0.02f;

            while (t < duration)
            {
                if (go == null) yield break;
                float u = Mathf.Clamp01(t / duration);
                float eased = 1f - Mathf.Pow(1f - u, 2f);

                float radius = Mathf.Lerp(startRadius, endRadius, eased);
                go.transform.localScale = new Vector3(radius, radius, radius);

                float width = Mathf.Lerp(startWidth, endWidth, u);
                lr.startWidth = width;
                lr.endWidth = width;

                float alpha = 1f - u;
                Color c = new Color(tint.r, tint.g, tint.b, alpha);
                lr.startColor = c;
                lr.endColor = c;
                if (mat != null && mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat != null && mat.HasProperty("_Color")) mat.SetColor("_Color", c);

                t += Time.deltaTime;
                yield return null;
            }

            if (go != null) Object.Destroy(go);
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
