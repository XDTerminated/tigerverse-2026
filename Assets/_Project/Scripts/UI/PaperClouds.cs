using UnityEngine;
using System.Collections.Generic;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PaperClouds : MonoBehaviour
    {
        private const int CloudCountMin = 6;
        private const int CloudCountMax = 8;
        private const float RingDistMin = 80f;
        private const float RingDistMax = 120f;
        private const float AltitudeMin = 15f;
        private const float AltitudeMax = 30f;
        private const float ScaleMin = 8f;
        private const float ScaleMax = 18f;
        private const float DriftMin = 0.1f;
        private const float DriftMax = 0.3f;
        private const int TexW = 256;
        private const int TexH = 128;

        // Greyscale paper — no warmth.
        private static readonly Color PaperCream = new Color(0.92f, 0.92f, 0.92f, 0.80f);

        private readonly List<Cloud> _clouds = new List<Cloud>();
        private Camera _cam;

        private struct Cloud
        {
            public Transform t;
            public float driftSpeed;
            public Vector3 driftDir;
            public float spinSpeed;
        }

        public static GameObject Spawn()
        {
            var go = new GameObject("PaperClouds");
            go.AddComponent<PaperClouds>();
            return go;
        }

        private void Start()
        {
            _cam = Camera.main;
            int count = Random.Range(CloudCountMin, CloudCountMax + 1);
            var sharedTex = BuildCloudTexture(TexW, TexH);
            var sharedMat = BuildCloudMaterial(sharedTex);

            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                float dist = Random.Range(RingDistMin, RingDistMax);
                float alt = Random.Range(AltitudeMin, AltitudeMax);
                Vector3 pos = new Vector3(Mathf.Cos(angle) * dist, alt, Mathf.Sin(angle) * dist);

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "PaperCloud_" + i;
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);

                quad.transform.SetParent(transform, worldPositionStays: true);
                quad.transform.position = pos;
                float widthScale = Random.Range(ScaleMin, ScaleMax);
                float heightScale = widthScale * 0.5f;
                quad.transform.localScale = new Vector3(widthScale, heightScale, 1f);

                var mr = quad.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = sharedMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }

                Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                if (Random.value < 0.5f) tangent = -tangent;

                _clouds.Add(new Cloud
                {
                    t = quad.transform,
                    driftSpeed = Random.Range(DriftMin, DriftMax),
                    driftDir = tangent,
                    spinSpeed = Random.Range(-3f, 3f)
                });
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            float dt = Time.deltaTime;
            for (int i = 0; i < _clouds.Count; i++)
            {
                var c = _clouds[i];
                if (c.t == null) continue;
                c.t.position += c.driftDir * c.driftSpeed * dt;

                if (_cam != null)
                {
                    Vector3 toCam = _cam.transform.position - c.t.position;
                    if (toCam.sqrMagnitude > 1e-4f)
                    {
                        Quaternion face = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                        Quaternion spin = Quaternion.AngleAxis(c.spinSpeed * Time.time, Vector3.up);
                        c.t.rotation = spin * face;
                    }
                }
            }
        }

        private static Material BuildCloudMaterial(Texture2D tex)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else mat.mainTexture = tex;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", PaperCream);
            else mat.color = PaperCream;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }

        private static Texture2D BuildCloudTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            int blobCount = Random.Range(5, 8);
            var blobs = new Vector4[blobCount];
            for (int i = 0; i < blobCount; i++)
            {
                float cx = Random.Range(w * 0.18f, w * 0.82f);
                float cy = Random.Range(h * 0.35f, h * 0.65f);
                float r = Random.Range(h * 0.28f, h * 0.48f);
                blobs[i] = new Vector4(cx, cy, r, 0f);
            }
            // Anchor blobs at the ends so silhouette spans width.
            blobs[0] = new Vector4(w * 0.22f, h * 0.5f, h * 0.42f, 0f);
            blobs[blobCount - 1] = new Vector4(w * 0.78f, h * 0.5f, h * 0.42f, 0f);

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float field = 0f;
                    for (int i = 0; i < blobCount; i++)
                    {
                        float dx = x - blobs[i].x;
                        float dy = y - blobs[i].y;
                        float r = blobs[i].z;
                        float d2 = dx * dx + dy * dy;
                        float t = 1f - Mathf.Clamp01(d2 / (r * r));
                        field += t * t;
                    }
                    float a = Mathf.Clamp01(Mathf.SmoothStep(0.35f, 0.95f, field));
                    pixels[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
