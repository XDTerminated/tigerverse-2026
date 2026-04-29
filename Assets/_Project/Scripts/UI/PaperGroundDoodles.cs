using UnityEngine;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    public class PaperGroundDoodles : MonoBehaviour
    {
        private const int TexSize = 64;
        private const float MinExclusion = 1.0f;
        private const float GroundLift = 0.01f;

        private static readonly Color InkColor = new Color(0.10f, 0.08f, 0.20f, 1f);
        private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        private float _radius = 8f;
        private int _count = 30;

        public static GameObject Spawn(Vector3 center, float radius = 8f, int count = 30)
        {
            var go = new GameObject("PaperGroundDoodles");
            go.transform.position = center;
            var c = go.AddComponent<PaperGroundDoodles>();
            c._radius = radius;
            c._count = count;
            return go;
        }

        private void Start()
        {
            int total = _count > 0 ? _count : Random.Range(20, 41);
            for (int i = 0; i < total; i++)
            {
                SpawnOne(i);
            }
        }

        private void SpawnOne(int index)
        {
            Vector2 offset;
            int safety = 0;
            do
            {
                Vector2 dir = Random.insideUnitCircle;
                offset = dir * _radius;
                safety++;
            } while (offset.magnitude < MinExclusion && safety < 16);

            var pos = transform.position + new Vector3(offset.x, GroundLift, offset.y);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Doodle_" + index;
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            quad.transform.SetParent(transform, true);
            quad.transform.position = pos;
            quad.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            float s = Random.Range(0.20f, 0.50f);
            quad.transform.localScale = new Vector3(s, s, s);

            int kind = Random.Range(0, 4);
            var tex = BuildDoodleTexture(kind);
            var mat = BuildMaterial(tex);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = mat;
        }

        private static Texture2D BuildDoodleTexture(int kind)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color[TexSize * TexSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Transparent;

            switch (kind)
            {
                case 0: DrawSquiggle(pixels); break;
                case 1: DrawStarburst(pixels); break;
                case 2: DrawDots(pixels); break;
                default: DrawSpiral(pixels); break;
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static void DrawSquiggle(Color[] pixels)
        {
            float amp = Random.Range(8f, 14f);
            float freq = Random.Range(0.18f, 0.32f);
            float phase = Random.Range(0f, Mathf.PI * 2f);
            int yMid = TexSize / 2;
            for (int x = 4; x < TexSize - 4; x++)
            {
                float y = yMid + Mathf.Sin((x + phase) * freq) * amp;
                PlotThick(pixels, x, Mathf.RoundToInt(y), 2);
            }
        }

        private static void DrawStarburst(Color[] pixels)
        {
            int cx = TexSize / 2;
            int cy = TexSize / 2;
            int rays = Random.Range(5, 9);
            float baseAngle = Random.Range(0f, Mathf.PI * 2f);
            for (int r = 0; r < rays; r++)
            {
                float a = baseAngle + (Mathf.PI * 2f / rays) * r;
                int len = Random.Range(10, 22);
                int innerGap = 4;
                float dx = Mathf.Cos(a);
                float dy = Mathf.Sin(a);
                for (int t = innerGap; t <= len; t++)
                {
                    int x = cx + Mathf.RoundToInt(dx * t);
                    int y = cy + Mathf.RoundToInt(dy * t);
                    PlotThick(pixels, x, y, 2);
                }
            }
        }

        private static void DrawDots(Color[] pixels)
        {
            for (int i = 0; i < 3; i++)
            {
                int x = Random.Range(10, TexSize - 10);
                int y = Random.Range(10, TexSize - 10);
                int radius = Random.Range(3, 6);
                FillDisk(pixels, x, y, radius);
            }
        }

        private static void DrawSpiral(Color[] pixels)
        {
            int cx = TexSize / 2;
            int cy = TexSize / 2;
            float maxR = Random.Range(14f, 22f);
            float turns = Random.Range(1.5f, 2.5f);
            int steps = 200;
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / steps;
                float a = t * turns * Mathf.PI * 2f;
                float r = t * maxR;
                int x = cx + Mathf.RoundToInt(Mathf.Cos(a) * r);
                int y = cy + Mathf.RoundToInt(Mathf.Sin(a) * r);
                PlotThick(pixels, x, y, 2);
            }
        }

        private static void PlotThick(Color[] pixels, int cx, int cy, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || x >= TexSize || y < 0 || y >= TexSize) continue;
                    pixels[y * TexSize + x] = InkColor;
                }
            }
        }

        private static void FillDisk(Color[] pixels, int cx, int cy, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || x >= TexSize || y < 0 || y >= TexSize) continue;
                    pixels[y * TexSize + x] = InkColor;
                }
            }
        }

        private static Material BuildMaterial(Texture2D tex)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            var mat = new Material(sh);
            mat.mainTexture = tex;

            float tint = Random.Range(0.85f, 1.0f);
            var c = new Color(tint, tint, tint, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");

            return mat;
        }
    }
}
