using UnityEngine;

namespace Tigerverse.Drawing
{
    /// <summary>
    /// Replaces every renderer's material on the GameObject with a triplanar-projected
    /// drawing material — so the mesh takes the drawing's dominant color as its tint
    /// and shows hints of the drawing detail wrapping around 3 axes (no flat sticker).
    /// </summary>
    public static class DrawingColorize
    {
        public static void Apply(GameObject root, Texture2D drawingTex, float drawingStrength = 0.18f, float projectionScale = 1.5f)
        {
            if (root == null)
            {
                Debug.LogWarning("[DrawingColorize] root is null — skipping.");
                return;
            }
            if (drawingTex == null)
            {
                Debug.LogWarning($"[DrawingColorize] drawingTex is null on '{root.name}' — material won't be replaced. Check that the session's imageUrl is set + reachable.");
                return;
            }

            Color tint = SampleDominantColor(drawingTex);
            // Boost the tint a bit so the dominant color pops — sampling sometimes
            // returns muted greys when ink is thin or anti-aliased.
            tint = BoostSaturation(tint, 1.4f);

            Shader stylized = Shader.Find("Tigerverse/DrawingStylized");
            Shader triplanar = Shader.Find("Tigerverse/DrawingTriplanar");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (stylized == null)
            {
                Debug.LogWarning("[DrawingColorize] Stylized shader 'Tigerverse/DrawingStylized' not found. " +
                                 "Falling back to URP/Lit. (Shader probably failed to compile — check Console for shader errors.)");
            }

            // Load Paper003 textures from Resources (download via Tigerverse → Textures → Download Paper003).
            Texture2D paperColor = LoadPaperTex("Color");
            Texture2D paperNormal = LoadPaperTex("NormalGL");
            if (paperColor == null)
            {
                Debug.LogWarning("[DrawingColorize] No Paper003_Color texture in Resources/PaperTextures. Run 'Tigerverse → Textures → Download Paper003' to fetch it; shader will fall back to flat tint until then.");
            }

            // Compute the model's WORLD-space bounding box. The shader
            // projects the drawing using positionWS, so we need world
            // bounds to align it correctly. Object-space bounds were
            // unreliable on nested glTFast hierarchies — every leaf mesh
            // had its own local origin and the drawing landed on a tiny
            // corner of the model.
            Bounds wsBounds = ComputeWorldBounds(root);
            Vector4 bboxMin = new Vector4(wsBounds.min.x, wsBounds.min.y, wsBounds.min.z, 0);
            Vector4 bboxSize = new Vector4(
                Mathf.Max(wsBounds.size.x, 0.001f),
                Mathf.Max(wsBounds.size.y, 0.001f),
                Mathf.Max(wsBounds.size.z, 0.001f),
                0);

            // drawingStrength here doubles as the "drawing watermark hint" on the
            // stylized shader. >0.5 still routes to the legacy triplanar in case
            // someone wants the all-over wrap.
            bool useLegacyTriplanar = drawingStrength > 0.5f && triplanar != null;

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            string usedShader = useLegacyTriplanar ? "DrawingTriplanar(legacy)"
                              : (stylized != null ? "DrawingStylized" : "URP/Lit fallback");
            Debug.Log($"[DrawingColorize] '{root.name}' tint=#{ColorUtility.ToHtmlStringRGB(tint)} " +
                      $"renderers={renderers.Length} shader={usedShader} bbox={wsBounds.size}");
            foreach (var rend in renderers)
            {
                Material mat;
                if (useLegacyTriplanar)
                {
                    mat = new Material(triplanar);
                    mat.SetTexture("_DrawingTex", drawingTex);
                    mat.SetColor("_BaseColor", tint);
                    mat.SetFloat("_ProjectionScale", projectionScale);
                    mat.SetFloat("_DrawingStrength", drawingStrength);
                }
                else if (stylized != null)
                {
                    mat = new Material(stylized);
                    // Body is pure white paper now — tint is intentionally NOT used.
                    mat.SetTexture("_DrawingTex", drawingTex);
                    mat.SetVector("_BBoxMin", bboxMin);
                    mat.SetVector("_BBoxSize", bboxSize);
                    // Higher default hint so the doodle is the primary identifier of each monster.
                    mat.SetFloat("_DrawingHint", Mathf.Clamp(drawingStrength <= 0.0001f ? 0.55f : drawingStrength, 0.05f, 0.95f));
                    // Force pencil-hatch off across every monster body — the
                    // user wants a clean texture without the cross-hatch
                    // overlay regardless of the shader's default value.
                    mat.SetFloat("_PencilStrength", 0f);
                    if (paperColor != null) mat.SetTexture("_PaperTex", paperColor);
                    if (paperNormal != null) mat.SetTexture("_PaperNormalTex", paperNormal);
                }
                else
                {
                    mat = new Material(litShader != null ? litShader : Shader.Find("Standard"));
                    mat.SetColor("_BaseColor", tint);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                    // Match the matte look of DrawingStylized on the URP/Lit
                    // fallback path so the model never gains a shiny ball
                    // highlight when our custom shader is missing.
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
                    if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0f);
                    if (mat.HasProperty("_SpecularHighlights"))     mat.SetFloat("_SpecularHighlights",     0f);
                    if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
                }

                int slotCount = rend.sharedMaterials != null ? rend.sharedMaterials.Length : 1;
                if (slotCount <= 1)
                {
                    rend.material = mat;
                }
                else
                {
                    var arr = new Material[slotCount];
                    for (int i = 0; i < slotCount; i++) arr[i] = mat;
                    rend.materials = arr;
                }
            }
        }

        // Union of all renderer world-space AABBs under root.
        private static Bounds ComputeWorldBounds(GameObject root)
        {
            var rends = root.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds(root.transform.position, Vector3.one);
            bool any = false;
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // Compute the union of all child mesh bounds in the root's local space.
        private static Bounds ComputeObjectBounds(GameObject root)
        {
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            var skinneds = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            Bounds b = new Bounds(Vector3.zero, Vector3.one); bool any = false;

            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                if (!any) { b = mb; any = true; } else b.Encapsulate(mb);
            }
            foreach (var s in skinneds)
            {
                if (s == null || s.sharedMesh == null) continue;
                var mb = s.sharedMesh.bounds;
                if (!any) { b = mb; any = true; } else b.Encapsulate(mb);
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one);
            return b;
        }

        // Loads the Paper003 texture variant from Resources/PaperTextures/.
        // The ambientCG zip extracts files like "Paper003_1K-JPG_Color.jpg",
        // "Paper003_1K-JPG_NormalGL.jpg", etc. We scan the Resources subfolder
        // by suffix so any naming variant works.
        private static Texture2D[] _allPaperTextures;
        private static Texture2D LoadPaperTex(string suffix)
        {
            if (_allPaperTextures == null)
            {
                _allPaperTextures = Resources.LoadAll<Texture2D>("PaperTextures");
            }
            foreach (var t in _allPaperTextures)
            {
                if (t == null) continue;
                if (t.name.IndexOf(suffix, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            return null;
        }

        private static Color BoostSaturation(Color c, float factor)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * factor);
            v = Mathf.Clamp01(v * 1.1f); // slight value boost too
            return Color.HSVToRGB(h, s, v);
        }

        // Average ink color from non-white pixels. Walks every Nth pixel for speed.
        public static Color SampleDominantColor(Texture2D tex)
        {
            if (tex == null) return Color.gray;

            // Need pixel access; if texture isn't readable, copy via RenderTexture.
            Texture2D readable = tex;
            bool createdCopy = false;
            try { tex.GetPixel(0, 0); }
            catch { readable = MakeReadable(tex); createdCopy = true; }

            int w = readable.width, h = readable.height;
            int step = Mathf.Max(1, Mathf.Min(w, h) / 64);
            float r = 0, g = 0, b = 0; int count = 0;
            const float INK_THRESHOLD = 0.94f; // anything not basically white

            for (int y = 0; y < h; y += step)
            {
                for (int x = 0; x < w; x += step)
                {
                    Color c = readable.GetPixel(x, y);
                    if (c.r < INK_THRESHOLD || c.g < INK_THRESHOLD || c.b < INK_THRESHOLD)
                    {
                        r += c.r; g += c.g; b += c.b; count++;
                    }
                }
            }

            if (createdCopy) Object.Destroy(readable);

            if (count == 0) return new Color(0.7f, 0.7f, 0.7f);
            return new Color(r / count, g / count, b / count);
        }

        private static Texture2D MakeReadable(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
