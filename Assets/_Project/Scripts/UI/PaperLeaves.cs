using UnityEngine;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PaperLeaves : MonoBehaviour
    {
        private static PaperLeaves _instance;
        private Transform _follow;
        private ParticleSystem _ps;

        public static GameObject Spawn(Transform follow = null)
        {
            if (_instance == null)
            {
                var go = new GameObject("PaperLeaves");
                _instance = go.AddComponent<PaperLeaves>();
            }
            _instance._follow = follow;
            if (follow != null)
            {
                _instance.transform.SetParent(follow, false);
                _instance.transform.localPosition = Vector3.zero;
            }
            else
            {
                _instance.transform.SetParent(null);
                _instance.transform.position = Vector3.zero;
            }
            return _instance.gameObject;
        }

        private void Start()
        {
            BuildSystem();
        }

        private void BuildSystem()
        {
            if (_ps != null) return;

            _ps = gameObject.GetComponent<ParticleSystem>();
            if (_ps == null) _ps = gameObject.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.startColor = new ParticleSystem.MinMaxGradient(BuildColorGradient());

            var emission = _ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(3f, 6f);

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(10f, 4f, 10f);
            shape.position = new Vector3(0f, 1.5f, 0f);

            var velocity = _ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.2f;
            noise.damping = true;

            var rotation = _ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.0f, 1.0f);

            var color = _ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 0.85f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(fade);

            var renderer = _ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = BuildMaterial();
        }

        private static Gradient BuildColorGradient()
        {
            var g = new Gradient();
            var cream = new Color(1.0f, 0.95f, 0.85f);
            var yellow = new Color(1.0f, 0.9f, 0.6f);
            var green = new Color(0.7f, 0.85f, 0.6f);
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(cream, 0f),
                    new GradientColorKey(yellow, 0.5f),
                    new GradientColorKey(green, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            return g;
        }

        private static Material _sharedMat;
        private static Material BuildMaterial()
        {
            if (_sharedMat != null) return _sharedMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _sharedMat = new Material(sh);
            _sharedMat.mainTexture = WhiteTex();
            if (_sharedMat.HasProperty("_BaseColor")) _sharedMat.SetColor("_BaseColor", Color.white);
            if (_sharedMat.HasProperty("_Color")) _sharedMat.SetColor("_Color", Color.white);
            if (_sharedMat.HasProperty("_Surface")) _sharedMat.SetFloat("_Surface", 1f);
            if (_sharedMat.HasProperty("_Blend")) _sharedMat.SetFloat("_Blend", 0f);
            return _sharedMat;
        }

        private static Texture2D _whiteTex;
        private static Texture2D WhiteTex()
        {
            if (_whiteTex != null) return _whiteTex;
            _whiteTex = new Texture2D(2, 2);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    _whiteTex.SetPixel(x, y, Color.white);
            _whiteTex.Apply();
            return _whiteTex;
        }
    }
}
