using UnityEngine;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PaperMountains : MonoBehaviour
    {
        private struct RingSpec
        {
            public float radius;
            public int   count;
            public float width;
            public float height;
            public Color tint;
        }

        private static readonly RingSpec[] Rings =
        {
            new RingSpec { radius =  60f, count =  8, width = 32f, height = 16f,
                           tint = new Color(0.45f, 0.45f, 0.55f, 1f) },
            new RingSpec { radius =  90f, count =  9, width = 36f, height = 18f,
                           tint = new Color(0.62f, 0.62f, 0.70f, 1f) },
            new RingSpec { radius = 130f, count = 10, width = 40f, height = 20f,
                           tint = new Color(0.78f, 0.78f, 0.84f, 1f) },
        };

        private static readonly Color PaperCream = new Color(0.96f, 0.92f, 0.82f, 1f);
        private const int TexW = 256;
        private const int TexH = 128;

        public static GameObject Spawn(Vector3 center)
        {
            var go = new GameObject("PaperMountains");
            go.transform.position = center;
            go.AddComponent<PaperMountains>();
            return go;
        }

        private void Start()
        {
            Vector3 center = transform.position;
            for (int r = 0; r < Rings.Length; r++)
            {
                var spec = Rings[r];
                var ringRoot = new GameObject("Ring_" + r);
                ringRoot.transform.SetParent(transform, worldPositionStays: true);
                ringRoot.transform.position = center;

                Texture2D tex = BuildSilhouetteTexture(r);
                Material mat = BuildMaterial(tex, spec.tint);

                for (int i = 0; i < spec.count; i++)
                {
                    float ang = (i / (float)spec.count) * Mathf.PI * 2f;
                    Vector3 pos = center + new Vector3(
                        Mathf.Cos(ang) * spec.radius, 0f,
                        Mathf.Sin(ang) * spec.radius);

                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "Mountain_" + r + "_" + i;
                    var col = quad.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    quad.transform.SetParent(ringRoot.transform, worldPositionStays: true);
                    // Anchor base near ground (y=0), height extends upward.
                    quad.transform.position = pos + Vector3.up * (spec.height * 0.5f);
                    quad.transform.localScale = new Vector3(spec.width, spec.height, 1f);

                    var mr = quad.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        mr.sharedMaterial = mat;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                    }

                    var bb = quad.AddComponent<MountainBillboard>();
                    bb.center = center;
                }
            }
        }

        private static Material BuildMaterial(Texture2D tex, Color tint)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else mat.mainTexture = tex;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            else mat.color = tint;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }

        private static Texture2D BuildSilhouetteTexture(int seed)
        {
            var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[TexW * TexH];

            // Generate peak heights along a row of control points.
            var rng = new System.Random(1337 + seed * 31);
            int peakCount = 7 + seed; // varies per ring
            float[] peakX = new float[peakCount];
            float[] peakH = new float[peakCount];
            for (int p = 0; p < peakCount; p++)
            {
                // Even spacing with slight jitter for natural feel.
                float frac = (p + 0.5f) / peakCount;
                float jitter = ((float)rng.NextDouble() - 0.5f) * (0.6f / peakCount);
                peakX[p] = Mathf.Clamp01(frac + jitter) * (TexW - 1);
                peakH[p] = Mathf.Lerp(0.35f, 0.95f, (float)rng.NextDouble()) * TexH;
            }

            for (int x = 0; x < TexW; x++)
            {
                // Find the two nearest peaks bracketing x and interpolate
                // along the triangle edges to get the silhouette top.
                float top = 0f;
                for (int p = 0; p < peakCount; p++)
                {
                    // Each peak is a triangle whose base width spans to its
                    // neighbors; height tapers linearly to zero at neighbors.
                    float left  = p == 0 ? -peakX[0] : peakX[p - 1];
                    float right = p == peakCount - 1 ? (2f * (TexW - 1) - peakX[p]) : peakX[p + 1];
                    if (x >= left && x <= right)
                    {
                        float t;
                        if (x <= peakX[p])
                            t = Mathf.InverseLerp(left, peakX[p], x);
                        else
                            t = 1f - Mathf.InverseLerp(peakX[p], right, x);
                        float h = peakH[p] * Mathf.Clamp01(t);
                        if (h > top) top = h;
                    }
                }

                int topI = Mathf.Clamp(Mathf.RoundToInt(top), 0, TexH - 1);
                for (int y = 0; y < TexH; y++)
                {
                    if (y <= topI)
                    {
                        // Soft 1-px anti-alias band at the silhouette edge.
                        float edge = Mathf.Clamp01(topI - top + 1f);
                        float a = (y == topI) ? edge : 1f;
                        pixels[y * TexW + x] = new Color(PaperCream.r, PaperCream.g, PaperCream.b, a);
                    }
                    else
                    {
                        pixels[y * TexW + x] = new Color(0f, 0f, 0f, 0f);
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private class MountainBillboard : MonoBehaviour
        {
            public Vector3 center;
            private void LateUpdate()
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                Vector3 to = cam.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-6f) return;
                // Quad faces +Z; aim that face toward the camera around Y only.
                transform.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
            }
        }
    }
}
