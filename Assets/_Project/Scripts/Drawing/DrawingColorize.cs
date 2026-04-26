using UnityEngine;

namespace Tigerverse.Drawing
{
    /// <summary>
    /// Replaces every renderer's material on the GameObject with a triplanar-projected
    /// drawing material, so the mesh takes the drawing's dominant color as its tint
    /// and shows hints of the drawing detail wrapping around 3 axes (no flat sticker).
    /// </summary>
    public static class DrawingColorize
    {
        public static void Apply(GameObject root, Texture2D drawingTex, float drawingStrength = 0.18f, float projectionScale = 1.5f)
        {
            if (root == null)
            {
                Debug.LogWarning("[DrawingColorize] root is null, skipping.");
                return;
            }

            // No drawing? Still apply the paper material, without it the
            // monster keeps Meshy's native peachy/skin-toned material and
            // looks nothing like a paper-craft figure. Use a 1×1 white
            // texture so the shader's drawing-watermark sampler has a
            // valid input but contributes nothing visually.
            bool hasDrawing = drawingTex != null;
            if (!hasDrawing)
            {
                Debug.LogWarning($"[DrawingColorize] drawingTex is null on '{root.name}'. Applying paper material with no drawing watermark.");
                drawingTex = GetWhiteTexture();
            }

            Color tint = hasDrawing ? SampleDominantColor(drawingTex) : new Color(0.7f, 0.7f, 0.7f);
            // Boost the tint a bit so the dominant color pops, sampling sometimes
            // returns muted greys when ink is thin or anti-aliased.
            if (hasDrawing) tint = BoostSaturation(tint, 1.4f);

            Shader stylized = Shader.Find("Tigerverse/DrawingStylized");
            Shader triplanar = Shader.Find("Tigerverse/DrawingTriplanar");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (stylized == null)
            {
                Debug.LogWarning("[DrawingColorize] Stylized shader 'Tigerverse/DrawingStylized' not found. " +
                                 "Falling back to URP/Lit. (Shader probably failed to compile, check Console for shader errors.)");
            }

            // (Previously this also tried to clone TestSphere_Red's
            // sharedMaterial. Removed: TigerverseShaderTest.SpawnSphereRed
            // creates a fresh TestSphere_Red with a default white URP/Lit
            // material then immediately calls Apply, so cloning from it
            // would copy the default material, not the scene-configured
            // paper one. Building explicitly from the shader with the
            // exact scene values is deterministic.)

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
            // unreliable on nested glTFast hierarchies, every leaf mesh
            // had its own local origin and the drawing landed on a tiny
            // corner of the model.
            Bounds wsBounds = ComputeWorldBounds(root);
            Vector4 bboxMin = new Vector4(wsBounds.min.x, wsBounds.min.y, wsBounds.min.z, 0);
            Vector4 bboxSize = new Vector4(
                Mathf.Max(wsBounds.size.x, 0.001f),
                Mathf.Max(wsBounds.size.y, 0.001f),
                Mathf.Max(wsBounds.size.z, 0.001f),
                0);

            // (Legacy DrawingTriplanar branch removed: it tinted the mesh in
            // the drawing's dominant ink color which produced peachy /
            // skin-toned monsters when drawings had warm-colored ink.
            // We now always go through the paper-stylized path.)

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            string usedShader = stylized != null ? "DrawingStylized (built from shader)" : "URP/Lit fallback";
            Debug.Log($"[DrawingColorize] '{root.name}' tint=#{ColorUtility.ToHtmlStringRGB(tint)} " +
                      $"renderers={renderers.Length} shader={usedShader} bbox={wsBounds.size}");
            foreach (var rend in renderers)
            {
                Material mat;
                if (stylized != null)
                {
                    mat = new Material(stylized);
                    // Set EVERY property explicitly. The shader CBUFFER's
                    // serialized defaults aren't always honoured by `new
                    // Material(shader)` on URP, fields can come back zero,
                    // which makes _PaperTexScale=0 → divide-by-zero in the
                    // triplanar UV → garbage paper sampling → monsters
                    // either look black or mirror-shiny.
                    mat.SetColor("_PaperColor",          new Color(0.97f, 0.95f, 0.91f, 1f));
                    mat.SetColor("_PaperShadowTint",     new Color(0.85f, 0.82f, 0.78f, 1f));
                    mat.SetFloat("_PaperTexScale",       0.6f);
                    mat.SetFloat("_PaperTexBlend",       0.85f);
                    mat.SetFloat("_PaperNormalStrength", 0.7f);
                    mat.SetColor("_OutlineColor",        new Color(0.05f, 0.05f, 0.08f, 1f));
                    mat.SetFloat("_OutlineThickness",    0.015f);
                    mat.SetFloat("_PencilStrength",      0f);   // user wants clean paper, no hatch
                    mat.SetFloat("_PencilContrast",      1.2f);
                    mat.SetFloat("_PencilScale",         140f);
                    mat.SetColor("_PencilColor",         new Color(0.05f, 0.05f, 0.08f, 1f));
                    mat.SetFloat("_Smoothness",          0.04f);
                    mat.SetFloat("_Metallic",            0f);
                    mat.SetFloat("_DrawingHint",         hasDrawing ? Mathf.Clamp(drawingStrength <= 0.0001f ? 0.55f : drawingStrength, 0.05f, 0.95f) : 0f);
                    mat.SetTexture("_DrawingTex", drawingTex);
                    mat.SetVector("_BBoxMin", bboxMin);
                    mat.SetVector("_BBoxSize", bboxSize);
                    if (paperColor != null) mat.SetTexture("_PaperTex", paperColor);
                    if (paperNormal != null) mat.SetTexture("_PaperNormalTex", paperNormal);

                    Debug.Log($"[DrawingColorize] paperColor={(paperColor != null ? paperColor.name : "MISSING")} paperNormal={(paperNormal != null ? paperNormal.name : "MISSING")}");
                }
                else
                {
                    // URP/Lit fallback. Stylized shader was missing, most
                    // likely failed to compile or wasn't included in the
                    // build. Use the paper texture as the base map with a
                    // warm-white tint, *not* tinted by the drawing's
                    // dominant ink color (which is usually dark for
                    // black-ink-on-white-paper drawings and would make
                    // the monster read as black/grey).
                    mat = new Material(litShader != null ? litShader : Shader.Find("Standard"));
                    Color paperWhite = new Color(0.97f, 0.95f, 0.91f, 1f);
                    if (paperColor != null)
                    {
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", paperColor);
                        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", paperColor);
                    }
                    if (paperNormal != null)
                    {
                        if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", paperNormal);
                        if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", paperNormal);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    mat.SetColor("_BaseColor", paperWhite);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", paperWhite);
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

        // Resolve the scene's reference paper material (TestSphere_Red's
        // shared material). NOT cached, caching across edit-mode menu
        // invocations risks holding a destroyed Material reference, and
        // resolving every spawn is cheap. Returns null if the sphere has
        // been removed; caller falls back to building a fresh material
        // from the shader.
        private static Material LoadReferencePaperMaterial()
        {
            // Includes inactive objects so a hidden TestSphere_Red still works.
            var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Renderer match = null;
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (r == null) continue;
                if (r.gameObject != null && r.gameObject.name == "TestSphere_Red")
                {
                    match = r;
                    break;
                }
            }
            if (match == null)
            {
                Debug.LogWarning("[DrawingColorize] TestSphere_Red not found in scene, falling back to building a paper material from shader defaults. Spawn one via 'Tigerverse → Test → Spawn Shader Test Sphere'.");
                return null;
            }
            var refMat = match.sharedMaterial;
            if (refMat == null)
            {
                Debug.LogWarning("[DrawingColorize] TestSphere_Red has no shared material.");
                return null;
            }
            Debug.Log($"[DrawingColorize] Reference material resolved from TestSphere_Red: '{refMat.name}' (shader='{refMat.shader?.name}').");
            return refMat;
        }

        // 1×1 pure-white fallback for when a player's drawing image isn't
        // available. The DrawingStylized shader's drawing-watermark sampler
        // still needs a valid Texture2D; sampling white contributes nothing.
        private static Texture2D _whiteTex;
        private static Texture2D GetWhiteTexture()
        {
            if (_whiteTex != null) return _whiteTex;
            _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply(false, false);
            _whiteTex.name = "DrawingColorize_WhiteFallback";
            return _whiteTex;
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
