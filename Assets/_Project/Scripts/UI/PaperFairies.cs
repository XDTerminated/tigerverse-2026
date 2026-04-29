using System.Collections.Generic;
using UnityEngine;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    public class PaperFairies : MonoBehaviour
    {
        private static Texture2D _softTex;
        private static readonly Dictionary<int, Material> _matCache = new Dictionary<int, Material>();

        private struct Fairy
        {
            public Transform tr;
            public float radius;
            public float baseHeight;
            public float angle;
            public float angularSpeed;
            public float bobAmp;
            public float bobFreq;
            public float bobPhase;
            public float baseScale;
            public float pulsePhase;
        }

        private readonly List<Fairy> _fairies = new List<Fairy>();
        private Vector3 _center;
        private int _count = 6;

        public static GameObject Spawn(Vector3 center, int count = 6)
        {
            var go = new GameObject("PaperFairies");
            go.transform.position = center;
            var pf = go.AddComponent<PaperFairies>();
            pf._center = center;
            pf._count = Mathf.Clamp(count, 1, 32);
            return go;
        }

        private void Start()
        {
            if (_count <= 0) _count = Random.Range(4, 9);
            _center = transform.position;
            BuildFairies(_count);
        }

        private void BuildFairies(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.name = "Fairy_" + i;
                quad.transform.SetParent(transform, false);

                Color tint = (Random.value < 0.5f)
                    ? new Color(1.0f, 0.95f, 0.6f, 0.85f)
                    : new Color(1.0f, 0.7f, 0.85f, 0.85f);

                var rend = quad.GetComponent<MeshRenderer>();
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.sharedMaterial = GetMaterial(tint);

                float radius = Random.Range(3f, 7f);
                float height = Random.Range(1f, 3f);
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float scale = Random.Range(0.10f, 0.20f);

                quad.transform.localScale = new Vector3(scale, scale, scale);
                quad.transform.position = _center + new Vector3(
                    Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);

                var f = new Fairy
                {
                    tr = quad.transform,
                    radius = radius,
                    baseHeight = height,
                    angle = angle,
                    angularSpeed = Random.Range(0.2f, 0.6f) * (Random.value < 0.5f ? -1f : 1f),
                    bobAmp = 0.15f,
                    bobFreq = Random.Range(0.5f, 1.0f),
                    bobPhase = Random.Range(0f, Mathf.PI * 2f),
                    baseScale = scale,
                    pulsePhase = Random.Range(0f, Mathf.PI * 2f)
                };
                _fairies.Add(f);
            }
        }

        private void LateUpdate()
        {
            float t = Time.time;
            var cam = Camera.main;
            for (int i = 0; i < _fairies.Count; i++)
            {
                var f = _fairies[i];
                f.angle += f.angularSpeed * Time.deltaTime;
                float y = f.baseHeight + Mathf.Sin(t * Mathf.PI * 2f * f.bobFreq + f.bobPhase) * f.bobAmp;
                Vector3 p = _center + new Vector3(
                    Mathf.Cos(f.angle) * f.radius, y, Mathf.Sin(f.angle) * f.radius);
                f.tr.position = p;

                float pulse = 1f + Mathf.Sin(t * Mathf.PI * 2f * 1.2f + f.pulsePhase) * 0.10f;
                float s = f.baseScale * pulse;
                f.tr.localScale = new Vector3(s, s, s);

                if (cam != null)
                {
                    Vector3 fwd = f.tr.position - cam.transform.position;
                    if (fwd.sqrMagnitude > 0.0001f)
                        f.tr.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                }

                _fairies[i] = f;
            }
        }

        private static Material GetMaterial(Color tint)
        {
            int key = ((int)(tint.r * 255) << 16) | ((int)(tint.g * 255) << 8) | (int)(tint.b * 255);
            if (_matCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");

            var mat = new Material(sh);
            mat.mainTexture = SoftCircleTex();

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHATEST_ON");

            _matCache[key] = mat;
            return mat;
        }

        private static Texture2D SoftCircleTex()
        {
            if (_softTex != null) return _softTex;
            const int N = 64;
            _softTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            _softTex.wrapMode = TextureWrapMode.Clamp;
            _softTex.filterMode = FilterMode.Bilinear;
            float c = (N - 1) * 0.5f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    _softTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            _softTex.Apply(false, false);
            return _softTex;
        }
    }
}
