#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Scatters a dense ring of trees in the donut between the playable
    /// spawn barrier and the mountain ring, so the wilderness past the
    /// invisible walls reads as a forest the player can see but not enter.
    /// Distinct from TigerverseScatterEnvironment, which targets the OLD
    /// 10x10 lobby floor — this tool is tuned for the current 50x50 spawn
    /// + mountain rings at 30/44/62m.
    ///
    /// Idempotent: deletes the prior forest root before rebuilding.
    /// </summary>
    public static class TigerverseScatterForest
    {
        private const string ModelFolder = "Assets/_Project/Models/Environment";
        private const string ScatterRoot = "EnvScatter_Forest";
        private const int Seed = 90210;

        // Donut bounds. InnerRadius sits just past the playable barrier (25m
        // half-extent in PaperGroundExtension) so trees never poke into the
        // arena. OuterRadius reaches the far mountain ring (62m).
        private const float InnerRadius = 26f;
        private const float OuterRadius = 60f;

        // Density. ~140 trees gives a clear forest mass without choking
        // editor perf on Quest builds; tune lower if frame time tanks.
        private const int TreeCount = 140;

        // Don't drop a tree within this distance of an existing mountain
        // billboard quad — overlapping a mountain reads as Z-fighting.
        private const float MountainKeepout = 3.5f;

        [MenuItem("Tigerverse/Lobby -> Scatter Forest Ring")]
        public static void Apply()
        {
            var prior = GameObject.Find(ScatterRoot);
            if (prior != null) Object.DestroyImmediate(prior);

            var allModels = AssetDatabase.FindAssets("t:Model", new[] { ModelFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(o => o != null)
                .ToList();
            if (allModels.Count == 0)
            {
                Debug.LogError($"[ScatterForest] No models under {ModelFolder}.");
                return;
            }

            // Trees only — no cactus (desert), no rocks, no bushes. Mix
            // standard, autumn, snow, and dead variants for visual variety
            // since this is wilderness ringing the arena.
            var trees = allModels.Where(m =>
            {
                string n = m.name.ToLowerInvariant();
                if (n.Contains("cactus")) return false;
                if (n.Contains("rock") || n.Contains("bush") || n.Contains("plant") || n.Contains("grass")) return false;
                if (n.Contains("lilypad") || n.Contains("woodlog") || n.Contains("treestump")) return false;
                return n.Contains("tree") || n.StartsWith("willow") || n.StartsWith("palmtree");
            }).ToList();

            if (trees.Count == 0)
            {
                Debug.LogError("[ScatterForest] No tree models matched. Check ModelFolder names.");
                return;
            }

            // Mountain billboard positions to avoid (built by PaperMountains
            // at runtime, but the rings are static so we mirror the math
            // here at scatter time).
            var mountainPoints = ComputeMountainPoints();

            var root = new GameObject(ScatterRoot);
            var rng = new System.Random(Seed);
            int placed = 0, attempts = 0;
            int maxAttempts = TreeCount * 10;

            while (placed < TreeCount && attempts < maxAttempts)
            {
                attempts++;

                // Polar sample biased toward mid-radius via sqrt-weighted
                // distribution so the outer rim isn't visibly thinner.
                double u = rng.NextDouble();
                float r = Mathf.Sqrt(Mathf.Lerp(InnerRadius * InnerRadius, OuterRadius * OuterRadius, (float)u));
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                var pos = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);

                if (TooCloseToMountain(pos, mountainPoints)) continue;

                var pick = trees[rng.Next(trees.Count)];
                Spawn(root.transform, pick, pos, rng, scaleMin: 0.55f, scaleMax: 1.30f);
                placed++;
            }

            int paperized = TigerverseScatterEnvironment.PaperizeRoot(root);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[ScatterForest] Scattered {placed} trees in donut [{InnerRadius}, {OuterRadius}] across {trees.Count} variants. Paperized {paperized} renderers.");
        }

        // Mirrors PaperMountains.cs ring spec so we can avoid stamping a tree
        // exactly where a mountain billboard will spawn at runtime.
        private static List<Vector3> ComputeMountainPoints()
        {
            var pts = new List<Vector3>();
            (float radius, int count)[] rings = { (30f, 8), (44f, 9), (62f, 10) };
            foreach (var (radius, count) in rings)
            {
                for (int i = 0; i < count; i++)
                {
                    float ang = (i / (float)count) * Mathf.PI * 2f;
                    pts.Add(new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
                }
            }
            return pts;
        }

        private static bool TooCloseToMountain(Vector3 p, List<Vector3> mountains)
        {
            for (int i = 0; i < mountains.Count; i++)
            {
                var m = mountains[i];
                float dx = p.x - m.x;
                float dz = p.z - m.z;
                if (dx * dx + dz * dz < MountainKeepout * MountainKeepout) return true;
            }
            return false;
        }

        private static void Spawn(Transform parent, GameObject prefab, Vector3 pos, System.Random rng, float scaleMin, float scaleMax)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) go = Object.Instantiate(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
            float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            go.transform.localScale = Vector3.one * s;

        }
    }
}
#endif
