#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Tigerverse.UI;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// One-stop "make every UI element match the website" pass.
    ///
    /// What it does:
    ///   1. Imports sophiecomic.ttf as a TMP SDF font asset (skips if it exists).
    ///   2. Generates two 9-sliced sprites, a 2px-bordered outline panel and a
    ///      filled black panel, both with the website's rounded-sm corners.
    ///   3. Walks every scene + every prefab under Assets/_Project, replacing:
    ///        - TMP fonts with Sophiecomic SDF
    ///        - Image background sprites on Buttons / Panels with the theme sprites
    ///        - Default Unity blue/grey colors with b&w
    ///
    /// Run from the Tigerverse menu. Can be re-run safely.
    /// </summary>
    public static class TigerverseRestyle
    {
        private const int SpriteSize = 64;
        private const int CornerRadius = 8;
        private const int BorderThickness = 4; // 2 logical px @ 2x oversample

        // ============================================================
        // MENU ITEMS
        // ============================================================

        [MenuItem("Tigerverse/Restyle/1 - Generate Font Asset (Sophiecomic SDF)")]
        public static void GenerateFontAsset()
        {
            var ttf = AssetDatabase.LoadAssetAtPath<Font>(TigerverseTheme.FontTtfPath);
            if (ttf == null)
            {
                Debug.LogError($"[Restyle] Could not find {TigerverseTheme.FontTtfPath}. " +
                    "Make sure the file is at that path and Unity has imported it.");
                return;
            }

            // If the SDF asset already exists, delete it and rebuild, older
            // runs created the asset without saving its atlas texture as a
            // sub-asset, which leaves runtime text invisible.
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TigerverseTheme.FontAssetPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(TigerverseTheme.FontAssetPath);
            }

            // Programmatic TMP_FontAsset creation (Unity 2022+ API).
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                ttf,
                samplingPointSize: 90,
                atlasPadding: 9,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024);

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.name = "sophiecomic SDF";

            Directory.CreateDirectory(Path.GetDirectoryName(TigerverseTheme.FontAssetPath));
            AssetDatabase.CreateAsset(fontAsset, TigerverseTheme.FontAssetPath);

            // CreateFontAsset builds the atlas texture and material in-memory
            // but does not parent them to the saved asset. Without this they
            // get GC'd on domain reload and every TMP_Text logs
            // "Font Atlas Texture ... is missing" at runtime.
            if (fontAsset.atlasTextures != null)
            {
                foreach (var tex in fontAsset.atlasTextures)
                {
                    if (tex != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex)))
                    {
                        tex.name = "sophiecomic SDF Atlas";
                        AssetDatabase.AddObjectToAsset(tex, fontAsset);
                    }
                }
            }
            if (fontAsset.material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fontAsset.material)))
            {
                fontAsset.material.name = "sophiecomic SDF Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(TigerverseTheme.FontAssetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[Restyle] Created {TigerverseTheme.FontAssetPath}");
        }

        [MenuItem("Tigerverse/Restyle/2 - Generate Rounded-sm Sprites")]
        public static void GenerateSprites()
        {
            EnsureSpriteDir();
            CreateRoundedSprite(TigerverseTheme.PanelSpritePath, filled: false, borderThickness: BorderThickness);
            CreateRoundedSprite(TigerverseTheme.FilledSpritePath, filled: true, borderThickness: BorderThickness);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Restyle] Generated rounded-sm sprites.");
        }

        [MenuItem("Tigerverse/Restyle/3 - Apply Theme to All Scenes & Prefabs")]
        public static void ApplyAcrossProject()
        {
            // Make sure foundations exist.
            GenerateFontAsset();
            GenerateSprites();

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TigerverseTheme.FontAssetPath);
            var outlineSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TigerverseTheme.PanelSpritePath);
            var filledSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TigerverseTheme.FilledSpritePath);
            if (font == null || outlineSprite == null || filledSprite == null)
            {
                Debug.LogError("[Restyle] Missing font or sprite assets, aborting.");
                return;
            }

            int scenesTouched = 0, prefabsTouched = 0, gosTouched = 0;

            // -- Scenes
            string[] scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes" });
            foreach (var guid in scenePaths)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int touched = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    touched += RestyleHierarchy(root, font, outlineSprite, filledSprite);
                }
                if (touched > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    scenesTouched++;
                    gosTouched += touched;
                }
            }

            // -- Prefabs (anywhere under _Project)
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" });
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                int touched = RestyleHierarchy(root, font, outlineSprite, filledSprite);
                if (touched > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabsTouched++;
                    gosTouched += touched;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Restyle] Done. {gosTouched} GameObjects updated across " +
                      $"{scenesTouched} scenes and {prefabsTouched} prefabs.");
        }

        // ============================================================
        // HIERARCHY WALKER
        // ============================================================

        private static int RestyleHierarchy(GameObject root, TMP_FontAsset font, Sprite outline, Sprite filled)
        {
            int count = 0;

            // Pre-compute which TMP texts live inside a Button so we can color
            // them as button-labels (default black, flipped to white on hover by
            // TigerverseHoverFlip) instead of as raw label text.
            var buttonLabels = new HashSet<TMP_Text>();
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                    buttonLabels.Add(t);
            }

            // 1. All TMP text components → Sophiecomic + black.
            //    Standalone text and button labels both end up black; the
            //    button hover flip handles inversion at runtime.
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                bool changed = false;
                if (tmp.font != font) { tmp.font = font; changed = true; }
                if (tmp.color != TigerverseTheme.Black)
                {
                    tmp.color = TigerverseTheme.Black;
                    changed = true;
                }
                if (changed)
                {
                    EditorUtility.SetDirty(tmp);
                    count++;
                }
            }

            // 2. All Buttons → swap to outline sprite, b&w colors, 2px-equivalent slice.
            //    Also attach TigerverseHoverFlip so the inner label inverts in
            //    step with the Selectable's tint.
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                var image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = outline;
                    image.type = Image.Type.Sliced;
                    image.color = TigerverseTheme.White;
                    EditorUtility.SetDirty(image);
                }
                var colors = button.colors;
                colors.normalColor = TigerverseTheme.White;
                colors.highlightedColor = TigerverseTheme.Black;
                colors.pressedColor = TigerverseTheme.Black;
                colors.selectedColor = TigerverseTheme.Black;
                colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                colors.colorMultiplier = 1f;
                button.colors = colors;
                EditorUtility.SetDirty(button);

                if (button.GetComponent<TigerverseHoverFlip>() == null)
                {
                    button.gameObject.AddComponent<TigerverseHoverFlip>();
                    EditorUtility.SetDirty(button.gameObject);
                }

                count++;
            }

            // 3. Standalone panel Images (no Button on the same GO) → outline panel.
            //    Only restyle Images that look like decorative backgrounds.
            //    Anything with a Filled image type, a non-default sprite, or a
            //    name suggesting it carries semantic color (Fill, Bar,
            //    Indicator, Health, Hp) is left alone so gameplay-meaningful
            //    visuals don't get flattened to b&w.
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.GetComponent<Button>() != null) continue;
                if (IsSemanticIndicator(image)) continue;

                if (image.GetComponent<TMP_InputField>() != null ||
                    image.GetComponentInParent<TMP_InputField>() != null)
                {
                    image.sprite = outline;
                    image.type = Image.Type.Sliced;
                    image.color = TigerverseTheme.White;
                    EditorUtility.SetDirty(image);
                    count++;
                    continue;
                }

                bool isDefaultUiSprite = image.sprite == null
                    || image.sprite.name.Contains("UISprite")
                    || image.sprite.name.Contains("Background")
                    || image.sprite.name.Contains("InputFieldBackground")
                    || image.sprite.name == "UIMask";

                if (isDefaultUiSprite && IsBackgroundLike(image))
                {
                    image.sprite = outline;
                    image.type = Image.Type.Sliced;
                    image.color = TigerverseTheme.White;
                    EditorUtility.SetDirty(image);
                    count++;
                }
            }

            return count;
        }

        // Filled-method Images and anything named like a meter/indicator carry
        // gameplay state (HP bar fill, mana, charge meter). Don't repaint them.
        private static bool IsSemanticIndicator(Image image)
        {
            if (image.type == Image.Type.Filled) return true;
            string n = image.name.ToLowerInvariant();
            return n.Contains("fill")
                || n.Contains("bar")
                || n.Contains("indicator")
                || n.Contains("meter")
                || n == "health" || n == "hp" || n == "mana";
        }

        // Treat as a decorative background if the Image either has no sibling
        // children (a leaf chrome element) OR its name looks like a panel.
        private static bool IsBackgroundLike(Image image)
        {
            string n = image.name.ToLowerInvariant();
            if (n.Contains("bg") || n.Contains("background") || n.Contains("panel")
                || n.Contains("card") || n.Contains("frame") || n.Contains("box"))
                return true;
            // Also accept small leaf images (no children) with an alpha, likely
            // chrome rather than a hero artwork.
            return image.transform.childCount == 0;
        }

        // ============================================================
        // SPRITE GENERATION (rounded-sm with 2px black border)
        // ============================================================

        private static void EnsureSpriteDir()
        {
            string dir = Path.GetDirectoryName(TigerverseTheme.PanelSpritePath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }
        }

        private static void CreateRoundedSprite(string assetPath, bool filled, int borderThickness)
        {
            var tex = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < SpriteSize; y++)
            {
                for (int x = 0; x < SpriteSize; x++)
                {
                    Color c = SamplePixel(x, y, filled, borderThickness);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;

            int border = CornerRadius + borderThickness;
            importer.spriteBorder = new Vector4(border, border, border, border);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }

        private static Color SamplePixel(int x, int y, bool filled, int borderThickness)
        {
            float cx = Mathf.Min(x, SpriteSize - 1 - x);
            float cy = Mathf.Min(y, SpriteSize - 1 - y);

            if (cx < CornerRadius && cy < CornerRadius)
            {
                float dx = CornerRadius - cx;
                float dy = CornerRadius - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > CornerRadius) return Color.clear;

                bool inBorder = dist > CornerRadius - borderThickness;
                if (inBorder) return TigerverseTheme.Black;
                return filled ? TigerverseTheme.Black : TigerverseTheme.White;
            }

            bool nearEdge = cx < borderThickness || cy < borderThickness;
            if (nearEdge) return TigerverseTheme.Black;
            return filled ? TigerverseTheme.Black : TigerverseTheme.White;
        }
    }
}
#endif
