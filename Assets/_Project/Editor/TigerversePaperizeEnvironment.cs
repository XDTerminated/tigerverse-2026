#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Applies the Paper003 texture (Color + NormalGL + Roughness) to every
    /// MeshRenderer under "LobbyEnv" in the active scene. Each renderer gets
    /// one of a small palette of grey-tinted material instances so the
    /// environment reads as different "shades of paper" instead of one flat
    /// surface. Run from Tigerverse menu, idempotent, safe to re-run.
    /// </summary>
    public static class TigerversePaperizeEnvironment
    {
        private const string PaperColorPath = "Assets/_Project/Resources/PaperTextures/Paper003_1K-JPG_Color.jpg";
        private const string PaperNormalPath = "Assets/_Project/Resources/PaperTextures/Paper003_1K-JPG_NormalGL.jpg";
        private const string PaperRoughPath = "Assets/_Project/Resources/PaperTextures/Paper003_1K-JPG_Roughness.jpg";
        private const string MatFolder = "Assets/_Project/Materials/Paper";

        // Greyscale palette (R = G = B). Indexed by a stable hash of the
        // renderer's name so the same object always gets the same shade
        // across re-runs. Light to dark; lots of mid-tones for natural variation.
        private static readonly float[] GreyShades = new[]
        {
            0.92f,  // near white
            0.82f,
            0.72f,
            0.62f,
            0.52f,
            0.42f,
            0.32f,  // near charcoal
        };

        [MenuItem("Tigerverse/Lobby -> Paperize Environment (varying greys)")]
        public static void Apply()
        {
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>(PaperColorPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(PaperNormalPath);
            var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(PaperRoughPath);
            if (color == null)
            {
                Debug.LogError($"[Paperize] Paper color texture missing at {PaperColorPath}. Run 'Tigerverse → Textures → Download Paper003 from ambientCG' first.");
                return;
            }

            var lobbyEnv = GameObject.Find("LobbyEnv");
            if (lobbyEnv == null)
            {
                Debug.LogError("[Paperize] No 'LobbyEnv' GameObject in active scene. Run 'Tigerverse → Lobby → Add Floor + Spawn Markers' first.");
                return;
            }

            Directory.CreateDirectory(MatFolder);
            // Pre-build one material per grey shade so we share them across all
            // renderers that fall into the same bucket. Cuts material count
            // dramatically vs. one-mat-per-renderer.
            var matsByShade = new Material[GreyShades.Length];
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            for (int i = 0; i < GreyShades.Length; i++)
            {
                string path = $"{MatFolder}/Paper_Grey_{(int)(GreyShades[i] * 100):D2}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                ConfigureMat(mat, color, normal, rough, GreyShades[i]);
                matsByShade[i] = mat;
            }

            int touched = 0, skipped = 0;
            foreach (var r in lobbyEnv.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Skip the egg-spawn marker discs; they're a colored gameplay
                // cue and shouldn't get the paper treatment.
                string n = r.gameObject.name;
                if (n.StartsWith("Spawn", System.StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf("Marker", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    skipped++;
                    continue;
                }
                int bucket = Mathf.Abs(n.GetHashCode()) % GreyShades.Length;
                r.sharedMaterial = matsByShade[bucket];
                EditorUtility.SetDirty(r);
                touched++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Paperize] Applied paper texture to {touched} renderers under LobbyEnv across {GreyShades.Length} grey shades. Skipped {skipped} spawn markers.");
        }

        private static void ConfigureMat(Material mat, Texture2D color, Texture2D normal, Texture2D rough, float grey)
        {
            // URP/Lit property names. _BaseMap, _BaseColor, _BumpMap, _Smoothness.
            mat.SetTexture("_BaseMap", color);
            // Greyscale tint (RGB equal, full alpha) modulates the paper color
            // toward the chosen shade while keeping the paper grain intact.
            mat.SetColor("_BaseColor", new Color(grey, grey, grey, 1f));
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            // Paper is matte; roughness map already darkens specular but force
            // smoothness low for safety.
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_Metallic", 0f);
            // Tile the texture across larger surfaces so the grain stays
            // visible at 10x10 floor scale.
            mat.SetTextureScale("_BaseMap", new Vector2(4f, 4f));
            if (normal != null) mat.SetTextureScale("_BumpMap", new Vector2(4f, 4f));
        }
    }
}
#endif
