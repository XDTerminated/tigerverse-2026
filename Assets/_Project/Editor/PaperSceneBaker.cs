#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// One-click utility to drop the full paper-craft scenery layer into
    /// the currently-open scene. Without this, the PaperSceneEnhancer only
    /// runs in Play mode (because its Start coroutine runs at runtime), so
    /// the Scene view stays empty even though everything's wired up.
    /// </summary>
    public static class PaperSceneBaker
    {
        [MenuItem("Tigerverse/Bake Paper Scenery into Scene")]
        public static void Bake()
        {
            // Remove previous bake so re-running doesn't stack copies.
            var existing = Object.FindFirstObjectByType<Tigerverse.UI.PaperSceneEnhancer>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // Use the Scene-view camera position if available so the
            // enhancer centers on what the dev is currently looking at.
            var sceneCam = SceneView.lastActiveSceneView?.camera;
            Vector3 spawnAt = sceneCam != null
                ? sceneCam.transform.position + sceneCam.transform.forward * 4f
                : Vector3.zero;
            // Snap Y to ground level approximation so flora / doodles
            // land on the floor.
            spawnAt.y = 0f;

            var go = Tigerverse.UI.PaperSceneEnhancer.Spawn(spawnAt, sceneCam != null ? sceneCam.transform : null);
            go.name = "PaperScenery";

            // Force-run Start now so the user sees content in the Scene
            // view immediately (Start fires automatically thanks to the
            // [ExecuteAlways] attribute we added, but if the enhancer was
            // pasted from a prefab the Start might already have run).
            EditorApplication.QueuePlayerLoopUpdate();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = go;

            Debug.Log("[PaperSceneBaker] Spawned PaperScenery at " + spawnAt + ". Save the scene to persist.");
        }

        [MenuItem("Tigerverse/Bake Ground Extension (Paper)")]
        public static void BakeGroundExtension()
        {
            var enh = Object.FindFirstObjectByType<Tigerverse.UI.PaperSceneEnhancer>();
            Vector3 center = enh != null ? enh.transform.position : Vector3.zero;
            Transform parent = enh != null ? enh.transform : null;

            foreach (var c in Object.FindObjectsByType<Tigerverse.UI.PaperGroundExtension>(FindObjectsSortMode.None))
                Object.DestroyImmediate(c.gameObject);

            var ext = Tigerverse.UI.PaperGroundExtension.Spawn(center);
            if (ext != null && parent != null) ext.transform.SetParent(parent, worldPositionStays: true);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[PaperSceneBaker] Baked PaperGroundExtension at " + center + ".");
        }

        [MenuItem("Tigerverse/Bake Light Flora + Doodles")]
        public static void BakeLightFloraDoodles()
        {
            var enh = Object.FindFirstObjectByType<Tigerverse.UI.PaperSceneEnhancer>();
            Vector3 center = enh != null ? enh.transform.position : Vector3.zero;
            Transform parent = enh != null ? enh.transform : null;

            // Strip any prior bakes so re-running doesn't stack copies.
            foreach (var c in Object.FindObjectsByType<Tigerverse.UI.PaperFlora>(FindObjectsSortMode.None))
                Object.DestroyImmediate(c.gameObject);
            foreach (var c in Object.FindObjectsByType<Tigerverse.UI.PaperGroundDoodles>(FindObjectsSortMode.None))
                Object.DestroyImmediate(c.gameObject);

            // Sparse counts — tune by hand if still too dense / sparse.
            var flora   = Tigerverse.UI.PaperFlora.Spawn(center, radius: 7f, count: 6);
            var doodles = Tigerverse.UI.PaperGroundDoodles.Spawn(center, radius: 14f, count: 30);
            if (parent != null)
            {
                if (flora   != null) flora.transform.SetParent(parent, worldPositionStays: true);
                if (doodles != null) doodles.transform.SetParent(parent, worldPositionStays: true);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[PaperSceneBaker] Baked sparse flora (6) + ground doodles (30) at " + center + ". Save the scene to persist.");
        }

        [MenuItem("Tigerverse/Remove Paper Scenery from Scene")]
        public static void Remove()
        {
            var existing = Object.FindFirstObjectByType<Tigerverse.UI.PaperSceneEnhancer>();
            if (existing == null)
            {
                Debug.Log("[PaperSceneBaker] No PaperSceneEnhancer in scene.");
                return;
            }
            Object.DestroyImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[PaperSceneBaker] Removed PaperScenery.");
        }
    }
}
#endif
