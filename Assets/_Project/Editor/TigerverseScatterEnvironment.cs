#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Scatters the imported nature assets (trees, rocks, bushes from
    /// Assets/_Project/Models/Environment/) around the lobby floor.
    /// Trees ring the perimeter; rocks and small foliage sprinkle randomly
    /// in the interior, with a no-spawn keep-out radius around the player
    /// spawn markers so we don't crowd the play area.
    ///
    /// Idempotent: re-running deletes the previous scatter and rebuilds
    /// with the same RNG seed so the layout is stable.
    /// </summary>
    public static class TigerverseScatterEnvironment
    {
        private const string ModelFolder = "Assets/_Project/Models/Environment";
        private const string ScatterRoot = "EnvScatter";
        private const int Seed = 1337;

        // Floor is 10x10 cube centered at origin (per LobbyEnv scaffold).
        private const float FloorHalf = 5f;
        // Trees ring the perimeter just outside the floor edge so they form
        // a visible boundary without standing inside the play area.
        private const float TreeRingRadius = 5.5f;
        private const int TreesAroundRing = 6;
        // Interior scatter for small foliage + rocks. Kept light so the play
        // space stays open.
        private const int InteriorRocks = 3;
        private const int InteriorBushes = 3;
        // Don't drop anything within this radius of either spawn marker.
        private const float SpawnKeepout = 1.6f;

        // Paper material palette — reused from TigerversePaperizeEnvironment so
        // scattered props match the floor's aesthetic.
        private const string PaperMatFolder = "Assets/_Project/Materials/Paper";
        private static readonly float[] GreyShades = { 0.92f, 0.82f, 0.72f, 0.62f, 0.52f, 0.42f, 0.32f };

        [MenuItem("Tigerverse/Lobby -> Scatter Trees + Rocks")]
        public static void Apply()
        {
            // Prior scatter is wiped before rebuilding so re-running doesn't
            // duplicate everything.
            var prior = GameObject.Find(ScatterRoot);
            if (prior != null) Object.DestroyImmediate(prior);

            // Pull every imported .obj as a prefab-like reference. Bucket by
            // category from the filename prefix so we can pick "trees" vs
            // "rocks" vs "bushes" semantically.
            var allModels = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(o => o != null)
                .ToList();
            if (allModels.Count == 0)
            {
                Debug.LogError($"[ScatterEnv] No model assets found under {ModelFolder}. Make sure the .obj files are imported.");
                return;
            }

            // Drop snow / dead variants for the lobby (we want a "green"
            // overworld feel). Trim to the standard + autumn variants.
            bool IsLobbyFriendly(GameObject m)
            {
                string n = m.name.ToLowerInvariant();
                if (n.Contains("snow")) return false;
                if (n.Contains("dead")) return false;
                if (n.Contains("cactus")) return false; // desert vibe doesn't fit
                return true;
            }

            var trees   = allModels.Where(m => IsLobbyFriendly(m) && (m.name.Contains("Tree") || m.name.StartsWith("Willow") || m.name.StartsWith("PalmTree"))).ToList();
            var rocks   = allModels.Where(m => IsLobbyFriendly(m) && m.name.StartsWith("Rock")).ToList();
            var bushes  = allModels.Where(m => IsLobbyFriendly(m) && (m.name.StartsWith("Bush") || m.name.StartsWith("Plant") || m.name.StartsWith("Grass"))).ToList();
            if (trees.Count == 0)
            {
                Debug.LogError("[ScatterEnv] No tree models matched. Check ModelFolder path / filenames.");
                return;
            }

            var root = new GameObject(ScatterRoot);
            var rng = new System.Random(Seed);

            // Spawn keep-outs (use SpawnP0/P1 if present, else default to the
            // hardcoded positions).
            var spawnPositions = new List<Vector3>();
            foreach (var n in new[] { "SpawnP0", "SpawnP1" })
            {
                var go = GameObject.Find(n);
                if (go != null) spawnPositions.Add(go.transform.position);
            }
            if (spawnPositions.Count == 0)
            {
                spawnPositions.Add(new Vector3(-1.2f, 0f, 0f));
                spawnPositions.Add(new Vector3( 1.2f, 0f, 0f));
            }

            // Ring of trees around the perimeter.
            for (int i = 0; i < TreesAroundRing; i++)
            {
                float angle = (i + (float)rng.NextDouble() * 0.4f) * Mathf.PI * 2f / TreesAroundRing;
                float r = TreeRingRadius + (float)rng.NextDouble() * 1.5f;
                var pos = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                var pick = trees[rng.Next(trees.Count)];
                Spawn(root.transform, pick, pos, rng, scaleMin: 0.85f, scaleMax: 1.25f);
            }

            // Interior rocks (small, random).
            int placed = 0, attempts = 0;
            while (placed < InteriorRocks && attempts < 200)
            {
                attempts++;
                var pos = RandomInteriorPos(rng, FloorHalf - 0.6f);
                if (TooCloseToSpawn(pos, spawnPositions)) continue;
                var pick = rocks.Count > 0 ? rocks[rng.Next(rocks.Count)] : trees[rng.Next(trees.Count)];
                Spawn(root.transform, pick, pos, rng, scaleMin: 0.4f, scaleMax: 0.8f);
                placed++;
            }

            // Bushes / plants / grass tufts in the interior too.
            placed = 0; attempts = 0;
            while (placed < InteriorBushes && attempts < 200)
            {
                attempts++;
                var pos = RandomInteriorPos(rng, FloorHalf - 0.6f);
                if (TooCloseToSpawn(pos, spawnPositions)) continue;
                var pick = bushes.Count > 0 ? bushes[rng.Next(bushes.Count)] : trees[rng.Next(trees.Count)];
                Spawn(root.transform, pick, pos, rng, scaleMin: 0.5f, scaleMax: 1.0f);
                placed++;
            }

            // Paperize: every renderer under the new scatter root gets a
            // grey-tinted paper material so the props sit on the same
            // visual layer as the floor.
            int paperized = PaperizeAll(root);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[ScatterEnv] Scattered {root.transform.childCount} env props (trees={TreesAroundRing}, rocks={InteriorRocks}, bushes={InteriorBushes}). Paperized {paperized} renderers.");
        }

        private static int PaperizeAll(GameObject root)
        {
            // Reuse the paper materials made by TigerversePaperizeEnvironment
            // when available; otherwise build them inline so this script
            // works standalone.
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Resources/PaperTextures/Paper003_1K-JPG_Color.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Resources/PaperTextures/Paper003_1K-JPG_NormalGL.jpg");
            if (color == null) { Debug.LogWarning("[ScatterEnv] Paper textures missing — props won't be paperized."); return 0; }

            System.IO.Directory.CreateDirectory(PaperMatFolder);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mats = new Material[GreyShades.Length];
            for (int i = 0; i < GreyShades.Length; i++)
            {
                string path = $"{PaperMatFolder}/Paper_Grey_{(int)(GreyShades[i] * 100):D2}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader);
                    AssetDatabase.CreateAsset(m, path);
                }
                m.SetTexture("_BaseMap", color);
                m.SetColor("_BaseColor", new Color(GreyShades[i], GreyShades[i], GreyShades[i], 1f));
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
                m.SetFloat("_Smoothness", 0.05f);
                m.SetFloat("_Metallic", 0f);
                mats[i] = m;
            }

            int count = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // Multi-submesh models (tree trunk + leaves) carry one
                // material per submesh. Replace EVERY slot so the whole
                // model paperizes, varying the grey per slot so trunk vs.
                // canopy still read as different surfaces.
                int slotCount = r.sharedMaterials.Length;
                if (slotCount == 0) continue;
                var newMats = new Material[slotCount];
                int seed = Mathf.Abs(r.gameObject.name.GetHashCode());
                for (int s = 0; s < slotCount; s++)
                {
                    int bucket = (seed + s * 3) % GreyShades.Length;
                    newMats[s] = mats[bucket];
                }
                r.sharedMaterials = newMats;
                EditorUtility.SetDirty(r);
                count++;
            }
            return count;
        }

        private static Vector3 RandomInteriorPos(System.Random rng, float halfExtent)
        {
            float x = (float)(rng.NextDouble() * 2 - 1) * halfExtent;
            float z = (float)(rng.NextDouble() * 2 - 1) * halfExtent;
            return new Vector3(x, 0f, z);
        }

        private static bool TooCloseToSpawn(Vector3 pos, List<Vector3> spawns)
        {
            foreach (var s in spawns)
            {
                if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(s.x, 0, s.z)) < SpawnKeepout)
                    return true;
            }
            return false;
        }

        private static void Spawn(Transform parent, GameObject prefab, Vector3 pos, System.Random rng, float scaleMin, float scaleMax)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) go = Object.Instantiate(prefab); // fallback for non-prefab-able assets
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            // Random Y-rotation for variety.
            go.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
            float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            go.transform.localScale = Vector3.one * s;
        }
    }
}
#endif
