using UnityEngine;
using System.Collections;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class CasterAuraVFX
    {
        public static GameObject Spawn(Transform caster, ElementType element, float duration, MonoBehaviour coroutineRunner)
        {
            if (caster == null) return null;

            Color tint = ElementTint(element);

            GameObject root = new GameObject("CasterAura_" + element);
            root.transform.SetParent(caster, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            ParticleSystem ps = root.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = Mathf.Max(0.1f, duration);
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            Color tintBright = Color.Lerp(tint, Color.white, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(tint, tintBright);
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 100f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = 0.30f;
            shape.donutRadius = 0.05f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.position = Vector3.zero;

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.Local;
            vol.y = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            vol.orbitalY = new ParticleSystem.MinMaxCurve(2.0f);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystemRenderer psr = root.GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            Material mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            psr.material = mat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            ps.Play();

            if (coroutineRunner != null)
            {
                coroutineRunner.StartCoroutine(StopAndDestroy(root, ps, duration));
            }
            else
            {
                Object.Destroy(root, duration + 0.5f);
            }

            return root;
        }

        private static IEnumerator StopAndDestroy(GameObject root, ParticleSystem ps, float duration)
        {
            yield return new WaitForSeconds(duration);
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            yield return new WaitForSeconds(0.5f);
            if (root != null) Object.Destroy(root);
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
