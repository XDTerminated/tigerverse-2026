using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class ElementOrbVFX
    {
        public static GameObject Spawn(Vector3 startPos, Transform defender, ElementType element, float duration, MonoBehaviour coroutineRunner)
        {
            Color tint = ElementTint(element);

            var root = new GameObject($"ElementOrb_{element}");
            root.transform.position = startPos;

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            StripCollider(core);
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = new Vector3(0.18f, 0.18f, 0.32f);
            var coreRenderer = core.GetComponent<MeshRenderer>();
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            coreRenderer.receiveShadows = false;
            coreRenderer.sharedMaterial = MakeUnlitMaterial(tint, 1f);

            var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "Halo";
            StripCollider(halo);
            halo.transform.SetParent(core.transform, false);
            halo.transform.localScale = Vector3.one * 1.6f;
            var haloRenderer = halo.GetComponent<MeshRenderer>();
            haloRenderer.shadowCastingMode = ShadowCastingMode.Off;
            haloRenderer.receiveShadows = false;
            Color haloColor = new Color(tint.r * 1.5f, tint.g * 1.5f, tint.b * 1.5f, 0.45f);
            haloRenderer.sharedMaterial = MakeUnlitMaterial(haloColor, 0.45f);

            var trail = core.AddComponent<TrailRenderer>();
            trail.time = 0.20f;
            trail.startWidth = 0.20f;
            trail.endWidth = 0.02f;
            trail.minVertexDistance = 0.005f;
            trail.material = MakeUnlitMaterial(tint, 1f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            trail.colorGradient = grad;

            var psGo = new GameObject("Stream");
            psGo.transform.SetParent(root.transform, false);
            var ps = psGo.AddComponent<ParticleSystem>();
            var psRenderer = psGo.GetComponent<ParticleSystemRenderer>();
            psRenderer.material = MakeParticleMaterial(tint);
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            var main = ps.main;
            main.duration = Mathf.Max(0.5f, duration + 0.5f);
            main.loop = true;
            main.startLifetime = 0.25f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor = tint;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            var emission = ps.emission;
            emission.rateOverTime = 10f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            float ttl = duration + 1.0f;
            coroutineRunner.StartCoroutine(AnimateOrb(root.transform, halo.transform, startPos, defender, duration));
            Object.Destroy(root, ttl);
            return root;
        }

        private static IEnumerator AnimateOrb(Transform root, Transform halo, Vector3 startPos, Transform defender, float duration)
        {
            float elapsed = 0f;
            Vector3 baseHaloScale = halo.localScale;
            while (elapsed < duration && root != null)
            {
                float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
                Vector3 target = defender != null ? defender.position : startPos;
                Vector3 pos = Vector3.Lerp(startPos, target, t);
                pos.y += 0.30f * Mathf.Sin(t * Mathf.PI);
                root.position = pos;

                if (defender != null)
                {
                    Vector3 dir = defender.position - root.position;
                    if (dir.sqrMagnitude > 0.0001f)
                        root.rotation = Quaternion.LookRotation(dir);
                }

                halo.localScale = baseHaloScale * (1f + 0.15f * Mathf.Sin(elapsed * 12f));

                elapsed += Time.deltaTime;
                yield return null;
            }
            if (root != null)
            {
                Vector3 finalTarget = defender != null ? defender.position : startPos;
                root.position = finalTarget;
            }
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }

        private static Material MakeUnlitMaterial(Color tint, float alpha)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh);
            Color c = tint;
            if (alpha < 1f)
            {
                c.a = alpha;
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
            return mat;
        }

        private static Material MakeParticleMaterial(Color tint)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            return mat;
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
