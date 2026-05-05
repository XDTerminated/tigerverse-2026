using UnityEngine;
using Tigerverse.Combat;

namespace Tigerverse.Combat.VFX
{
    public static class ElementImpactBurst
    {
        public static void Spawn(Vector3 position, ElementType element)
        {
            GameObject go = new GameObject($"ImpactBurst_{element}");
            go.transform.position = position;

            Color tint = ElementTint(element);

            switch (element)
            {
                case ElementType.Fire:
                    ConfigureFire(go, tint);
                    break;
                case ElementType.Water:
                    ConfigureWater(go, tint);
                    break;
                case ElementType.Electric:
                    ConfigureElectric(go, tint);
                    break;
                case ElementType.Earth:
                    ConfigureEarth(go, tint);
                    break;
                case ElementType.Grass:
                    ConfigureGrass(go, tint);
                    break;
                case ElementType.Ice:
                    ConfigureIce(go, tint);
                    break;
                case ElementType.Dark:
                    ConfigureDark(go, tint);
                    break;
                default:
                    ConfigureNeutral(go, tint);
                    break;
            }

            Object.Destroy(go, 2f);
        }

        private static ParticleSystem AddPS(GameObject parent, string childName, Color tint)
        {
            GameObject child = parent;
            if (!string.IsNullOrEmpty(childName))
            {
                child = new GameObject(childName);
                child.transform.SetParent(parent.transform, false);
            }
            ParticleSystem ps = child.GetComponent<ParticleSystem>();
            if (ps == null) ps = child.AddComponent<ParticleSystem>();
            ApplyMaterial(ps, tint);
            return ps;
        }

        private static void ApplyMaterial(ParticleSystem ps, Color tint)
        {
            ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            Material mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        private static void BaseMain(ParticleSystem ps, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, Color tint, float gravity = 0f)
        {
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = tint;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
        }

        private static void SetBurst(ParticleSystem ps, short count)
        {
            var emission = ps.emission;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void SetSphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        private static void ColorGradient(ParticleSystem ps, Color a, Color b, Color c, float endAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(a, 0f),
                    new GradientColorKey(b, 0.5f),
                    new GradientColorKey(c, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.6f),
                    new GradientAlphaKey(endAlpha, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        private static void ConfigureFire(GameObject go, Color tint)
        {
            ParticleSystem ps = AddPS(go, null, tint);
            BaseMain(ps, 0.4f, 0.7f, 3f, 6f, 0.06f, 0.15f, tint, 0f);
            SetSphere(ps, 0.05f);
            SetBurst(ps, 50);
            ColorGradient(ps, new Color(1f, 0.55f, 0.15f, 1f), new Color(1f, 0.9f, 0.3f, 1f), new Color(0.4f, 0.05f, 0.0f, 0f), 0f);
        }

        private static void ConfigureWater(GameObject go, Color tint)
        {
            ParticleSystem spray = AddPS(go, "Spray", tint);
            BaseMain(spray, 0.3f, 0.6f, 2f, 4f, 0.04f, 0.10f, tint, 0f);
            SetSphere(spray, 0.05f);
            SetBurst(spray, 40);
            ColorGradient(spray, new Color(0.7f, 0.85f, 1f, 1f), new Color(0.3f, 0.6f, 1f, 1f), new Color(0.2f, 0.4f, 0.9f, 0f), 0f);
            var sol = spray.sizeOverLifetime;
            sol.enabled = true;
            AnimationCurve shrink = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, shrink);

            ParticleSystem ring = AddPS(go, "Ring", tint);
            BaseMain(ring, 0.5f, 0.5f, 0.5f, 0.5f, 0.05f, 0.10f, tint, 0f);
            SetBurst(ring, 30);
            var rshape = ring.shape;
            rshape.enabled = true;
            rshape.shapeType = ParticleSystemShapeType.Donut;
            rshape.radius = 0.3f;
            rshape.donutRadius = 0.05f;
            rshape.rotation = new Vector3(90f, 0f, 0f);
        }

        private static void ConfigureElectric(GameObject go, Color tint)
        {
            ParticleSystem ps = AddPS(go, null, tint);
            BaseMain(ps, 0.15f, 0.30f, 4f, 8f, 0.03f, 0.08f, tint, 0f);
            SetSphere(ps, 0.02f);
            SetBurst(ps, 60);
            ColorGradient(ps, new Color(1f, 0.95f, 0.3f, 1f), new Color(1f, 1f, 1f, 1f), new Color(1f, 0.95f, 0.3f, 0f), 0f);
        }

        private static void ConfigureEarth(GameObject go, Color tint)
        {
            ParticleSystem ps = AddPS(go, null, tint);
            BaseMain(ps, 0.6f, 1.0f, 2f, 4f, 0.08f, 0.18f, tint, 1.5f);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            SetBurst(ps, 25);
        }

        private static void ConfigureGrass(GameObject go, Color tint)
        {
            ParticleSystem ps = AddPS(go, null, tint);
            BaseMain(ps, 0.8f, 1.2f, 1f, 2f, 0.06f, 0.12f, tint, 0.3f);
            SetSphere(ps, 0.1f);
            SetBurst(ps, 30);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.orbitalY = new ParticleSystem.MinMaxCurve(1f);

            ColorGradient(ps, new Color(0.5f, 0.9f, 0.45f, 1f), new Color(0.4f, 0.85f, 0.4f, 1f), new Color(0.25f, 0.6f, 0.25f, 0f), 0f);
        }

        private static void ConfigureIce(GameObject go, Color tint)
        {
            ParticleSystem ps = AddPS(go, null, tint);
            BaseMain(ps, 0.5f, 0.9f, 3f, 5f, 0.05f, 0.12f, tint, 1.0f);
            SetSphere(ps, 0.04f);
            SetBurst(ps, 35);
            ColorGradient(ps, new Color(1f, 1f, 1f, 1f), new Color(0.8f, 0.95f, 1f, 1f), new Color(0.5f, 0.8f, 1f, 0f), 0f);
        }

        private static void ConfigureDark(GameObject go, Color tint)
        {
            ParticleSystem ps = AddPS(go, null, tint);
            BaseMain(ps, 0.8f, 1.5f, 0.5f, 1.5f, 0.10f, 0.20f, tint, 0f);
            SetSphere(ps, 0.15f);
            SetBurst(ps, 25);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(tint, 0f),
                    new GradientColorKey(tint, 1f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.6f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.y = new ParticleSystem.MinMaxCurve(0.5f);
        }

        private static void ConfigureNeutral(GameObject go, Color tint)
        {
            ParticleSystem ps = AddPS(go, null, tint);
            BaseMain(ps, 0.25f, 0.45f, 2f, 4f, 0.05f, 0.12f, tint, 0f);
            SetSphere(ps, 0.06f);
            SetBurst(ps, 30);
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
