using UnityEngine;
using UnityEngine.Rendering;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class CastChargeVFX
    {
        public static void Spawn(Vector3 casterPos, ElementType element, float duration)
        {
            if (duration <= 0f) duration = 0.5f;
            Color tint = ElementTint(element);

            GameObject root = new GameObject("CastCharge_" + element.ToString());
            root.transform.position = casterPos + Vector3.up * 1.0f;

            Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (pShader == null) pShader = Shader.Find("Sprites/Default");

            BuildSpiral(root, tint, duration, pShader);
            BuildRing(root, tint, duration, pShader);

            Object.Destroy(root, duration + 1.0f);
        }

        private static void BuildSpiral(GameObject root, Color tint, float duration, Shader shader)
        {
            GameObject go = new GameObject("Spiral");
            go.transform.SetParent(root.transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.0f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startColor = tint;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 80f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.radial = new ParticleSystem.MinMaxCurve(-1.5f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(2.0f);

            ConfigureRenderer(ps, shader);
        }

        private static void BuildRing(GameObject root, Color tint, float duration, Shader shader)
        {
            GameObject go = new GameObject("Ring");
            go.transform.SetParent(root.transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = duration * 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = Color.Lerp(tint, Color.white, 0.4f);
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 30) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = 0.30f;
            shape.donutRadius = 0.04f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            ConfigureRenderer(ps, shader);
        }

        private static void ConfigureRenderer(ParticleSystem ps, Shader shader)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (shader != null)
                {
                    Material mat = new Material(shader);
                    renderer.sharedMaterial = mat;
                }
            }
        }

        private static Color ElementTint(ElementType e)
        {
            switch (e)
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
