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
