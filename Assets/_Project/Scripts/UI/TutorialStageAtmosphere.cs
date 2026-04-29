using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Visual dressing for the Professor tutorial stage: a warm key light
    /// and a paper-cream "stage ring" decal on the floor. All purely
    /// cosmetic — no gameplay. Children are parented to this GameObject
    /// so Unity tears them down automatically when the parent tutorial
    /// is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialStageAtmosphere : MonoBehaviour
    {
        private Vector3 _stageCenter;
        private Vector3 _stageForward;

        // Tunables — kept as constants so designers can tweak in one place.
        private const float StageRingDiameter   = 3.5f;
        private const float StageRingHeightOff  = 0.02f;   // ~2cm off floor, avoids z-fight
        private const float KeyLightIntensity   = 1.25f;

        // Warm 3500K-ish — gentle yellow tint without going orange.
        private static readonly Color WarmKey       = new Color(1.00f, 0.93f, 0.78f, 1f);
        private static readonly Color PaperCream    = new Color(0.98f, 0.94f, 0.82f, 1f);

        public void Setup(Vector3 stageCenter, Vector3 stageForward)
        {
            _stageCenter = stageCenter;
            _stageForward = stageForward.sqrMagnitude > 1e-4f
                ? stageForward.normalized
                : Vector3.forward;

            BuildKeyLight();
            BuildStageRing();
        }

        // ─── Key light ──────────────────────────────────────────────────
        private void BuildKeyLight()
        {
            var go = new GameObject("StageKeyLight");
            go.transform.SetParent(transform, worldPositionStays: true);
            // Sit the light up-and-behind the player, aimed at the stage.
            // _stageForward points stage->player, so the light originates
            // on the player side and looks back at the stage from above.
            Vector3 origin = _stageCenter + _stageForward * 1.6f + Vector3.up * 3.2f;
            go.transform.position = origin;
            Vector3 aim = (_stageCenter + Vector3.up * 0.8f) - origin;
            if (aim.sqrMagnitude > 1e-6f)
                go.transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = WarmKey;
            light.intensity = KeyLightIntensity;
            light.range = 8f;
            light.spotAngle = 70f;
            light.innerSpotAngle = 35f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
        }

        // ─── Floor ring ─────────────────────────────────────────────────
        private void BuildStageRing()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "StageRing";
            // Strip collider — purely visual.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = _stageCenter + Vector3.up * StageRingHeightOff;
            // Quad faces +Z by default; rotate flat onto the floor.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(StageRingDiameter, StageRingDiameter, 1f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                var mat = new Material(sh);
                var tex = BuildRingTexture(128);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                else mat.mainTexture = tex;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", PaperCream);
                else mat.color = PaperCream;
                // Transparent surface so the soft falloff edge fades into the floor.
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        // Procedural soft-edge ring: opaque-ish near center, fades to zero
        // at the rim. Cheap and avoids shipping a texture asset.
        private static Texture2D BuildRingTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Soft disc: alpha=0.55 in center, fades to 0 at r=1.
                    float a = Mathf.Clamp01(1f - r);
                    a = Mathf.SmoothStep(0f, 1f, a) * 0.55f;
                    // Add a faint inner ring highlight for personality.
                    float ringBand = Mathf.Exp(-Mathf.Pow((r - 0.85f) / 0.05f, 2f));
                    a = Mathf.Clamp01(a + ringBand * 0.25f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private void OnDestroy()
        {
            // Children are parented to this GameObject — Unity destroys them
            // automatically. Reset cached state for safety.
            _stageCenter = Vector3.zero;
            _stageForward = Vector3.zero;
        }
    }
}
