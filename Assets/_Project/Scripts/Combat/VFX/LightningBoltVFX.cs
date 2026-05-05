using UnityEngine;
using System.Collections;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class LightningBoltVFX
    {
        private const int VertexCount = 12;
        private const float TickInterval = 0.05f;
        private const float JiggleAmplitude = 0.18f;

        public static void Spawn(Vector3 from, Vector3 to, ElementType element, float duration, MonoBehaviour coroutineRunner)
        {
            if (coroutineRunner == null) return;

            GameObject go = new GameObject("LightningBolt_" + element);
            go.transform.position = from;

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.positionCount = VertexCount;
            lr.useWorldSpace = true;
            lr.startWidth = 0.10f;
            lr.endWidth = 0.04f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            Color tint = ElementTint(element);

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            Material mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            else mat.color = tint;
            lr.material = mat;

            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(tint, 0.5f),
                    new GradientColorKey(tint, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            lr.colorGradient = grad;

            Jiggle(lr, from, to);

            coroutineRunner.StartCoroutine(Run(go, lr, from, to, tint, duration));
        }

        private static IEnumerator Run(GameObject go, LineRenderer lr, Vector3 from, Vector3 to, Color tint, float duration)
        {
            float elapsed = 0f;
            float tickTimer = 0f;
            while (elapsed < duration && go != null)
            {
                elapsed += Time.deltaTime;
                tickTimer += Time.deltaTime;
                if (tickTimer >= TickInterval)
                {
                    tickTimer = 0f;
                    Jiggle(lr, from, to);
                }

                float a = Mathf.Clamp01(1f - (elapsed / Mathf.Max(0.0001f, duration)));
                Gradient g = new Gradient();
                g.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(tint, 0.5f),
                        new GradientColorKey(tint, 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(a, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                lr.colorGradient = g;
                yield return null;
            }

            if (go != null) Object.Destroy(go);
        }

        private static void Jiggle(LineRenderer lr, Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            Vector3 perp;
            if (dir.sqrMagnitude < 0.0000001f)
            {
                perp = Vector3.right;
            }
            else
            {
                Vector3 d = dir.normalized;
                Vector3 c = Vector3.Cross(d, Vector3.up);
                if (c.sqrMagnitude < 0.0001f) perp = Vector3.right;
                else perp = c.normalized;
            }

            int count = lr.positionCount;
            for (int i = 0; i < count; i++)
            {
                float t = (count <= 1) ? 0f : (float)i / (count - 1);
                Vector3 basePos = Vector3.Lerp(from, to, t);
                float falloff = Mathf.Sin(t * Mathf.PI);
                Vector3 perturb = perp * Random.Range(-JiggleAmplitude, JiggleAmplitude) * falloff;
                lr.SetPosition(i, basePos + perturb);
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
