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
        public static void Apply(GameObject root, Texture2D drawingTex, float drawingStrength = 0.55f, float projectionScale = 0.6f)
        {
            if (root == null || drawingTex == null) return;

            var shader = Shader.Find("Tigerverse/DrawingTriplanar");
            if (shader == null)
            {
                Debug.LogWarning("[DrawingColorize] Shader 'Tigerverse/DrawingTriplanar' not found; skipping colorize.");
                return;
            }

            Color tint = SampleDominantColor(drawingTex);

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var rend in renderers)
            {
                var mat = new Material(shader);
                mat.SetTexture("_DrawingTex", drawingTex);
                mat.SetColor("_BaseColor", tint);
                mat.SetFloat("_ProjectionScale", projectionScale);
                mat.SetFloat("_DrawingStrength", drawingStrength);

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
