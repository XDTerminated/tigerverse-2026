using UnityEngine;
using UnityEngine.Rendering;

namespace Tigerverse.UI
{
    [DisallowMultipleComponent]
    public class PaperSkybox : MonoBehaviour
    {
        // Greyscale paper. No warmth, no color — just paper-light at the
        // top fading to paper-shadow at the horizon.
        static readonly Color BgMid = new Color(0.86f, 0.86f, 0.86f, 1f);
        static readonly Color AmbientCream = new Color(0.92f, 0.92f, 0.92f, 1f);
        static readonly Color GradientTop = new Color(0.97f, 0.97f, 0.97f, 1f);
        static readonly Color GradientHorizon = new Color(0.74f, 0.74f, 0.74f, 1f);

        const float DomeRadius = 450f;
        const int GradientHeight = 512;

        GameObject dome;
        Transform followTarget;

        public static GameObject Spawn()
        {
            var existing = FindFirstObjectByType<PaperSkybox>();
            if (existing != null) return existing.gameObject;
            var go = new GameObject("PaperSkybox");
            go.AddComponent<PaperSkybox>();
            return go;
        }

        void Start()
        {
            ApplyCameraAndAmbient();
            BuildDome();
        }

        void LateUpdate()
        {
            if (dome == null) return;
            if (followTarget == null)
            {
                var cam = Camera.main;
                if (cam != null) followTarget = cam.transform;
            }
            if (followTarget != null)
            {
                dome.transform.position = followTarget.position;
            }
        }

        void ApplyCameraAndAmbient()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = BgMid;
                followTarget = cam.transform;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientCream;
        }

        void BuildDome()
        {
            dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "PaperSkyDome";
            var col = dome.GetComponent<Collider>();
            if (col != null) Destroy(col);

            dome.transform.SetParent(transform, false);
            dome.transform.localScale = new Vector3(-DomeRadius, -DomeRadius, -DomeRadius);

            var tex = BuildGradientTexture();
            var mat = BuildDomeMaterial(tex);

            var mr = dome.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Texture2D BuildGradientTexture()
        {
            var tex = new Texture2D(1, GradientHeight, TextureFormat.RGBA32, false, true)
            {
                name = "PaperSkyGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[GradientHeight];
            for (int y = 0; y < GradientHeight; y++)
            {
                float t = y / (float)(GradientHeight - 1);
                Color c = Color.Lerp(GradientHorizon, GradientTop, t);
                float n = (Mathf.PerlinNoise(y * 0.137f, 3.21f) - 0.5f) * 0.018f;
                c.r = Mathf.Clamp01(c.r + n);
                c.g = Mathf.Clamp01(c.g + n);
                c.b = Mathf.Clamp01(c.b + n);
                pixels[y] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Material BuildDomeMaterial(Texture2D tex)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            var mat = new Material(sh) { name = "PaperSkyDomeMat", hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            mat.renderQueue = (int)RenderQueue.Background + 1;
            return mat;
        }

        void OnDestroy()
        {
            if (dome != null) Destroy(dome);
        }
    }
}
