#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    public static class TigerverseWireSessionPrefab
    {
        [MenuItem("Tigerverse/Net -> Wire SessionManager prefab into SessionRunner")]
        public static void Wire()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);

            var bootstrap = GameObject.Find("Bootstrap");
            if (bootstrap == null) { Debug.LogError("[Tigerverse] Bootstrap GO not found in Title."); return; }

            var runner = bootstrap.GetComponent<Tigerverse.Net.SessionRunner>();
            if (runner == null) { Debug.LogError("[Tigerverse] SessionRunner missing on Bootstrap."); return; }

            string prefabPath = "Assets/_Project/Prefabs/Players/SessionManager.prefab";
            var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabGo == null) { Debug.LogError($"[Tigerverse] Prefab not found at {prefabPath}"); return; }

            var so = new SerializedObject(runner);
            var prefabProp = so.FindProperty("sessionManagerPrefab");
            if (prefabProp == null) { Debug.LogError("[Tigerverse] 'sessionManagerPrefab' field not found."); return; }
            prefabProp.objectReferenceValue = prefabGo;

            var configProp = so.FindProperty("config");
            if (configProp != null && configProp.objectReferenceValue == null)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<Tigerverse.Net.BackendConfig>(
                    "Assets/_Project/Resources/BackendConfig.asset");
                if (cfg != null) configProp.objectReferenceValue = cfg;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Wired SessionManager.prefab -> SessionRunner.sessionManagerPrefab.");
        }
    }
}
#endif
