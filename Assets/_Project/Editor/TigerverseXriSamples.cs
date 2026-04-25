#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    public static class TigerverseXriSamples
    {
        [MenuItem("Tigerverse/XR -> Import XRI Starter Assets Sample")]
        public static void ImportStarterAssets()
        {
            var samples = Sample.FindByPackage("com.unity.xr.interaction.toolkit", null).ToList();
            if (samples.Count == 0)
            {
                Debug.LogError("[Tigerverse] No XRI samples found. Is com.unity.xr.interaction.toolkit installed?");
                return;
            }

            foreach (var s in samples)
            {
                Debug.Log($"[Tigerverse] Available XRI sample: '{s.displayName}'");
            }

            int idx = samples.FindIndex(s => s.displayName == "Starter Assets");
            if (idx < 0) idx = samples.FindIndex(s => s.displayName != null && s.displayName.Contains("Starter"));
            if (idx < 0) idx = 0;
            if (samples.Count == 0)
            {
                Debug.LogError("[Tigerverse] Could not locate the Starter Assets sample.");
                return;
            }

            var starter = samples[idx];
            bool ok = starter.Import(Sample.ImportOptions.OverridePreviousImports);
            Debug.Log($"[Tigerverse] Imported '{starter.displayName}': {ok}. New folder: Assets/Samples/XR Interaction Toolkit/...");
            AssetDatabase.Refresh();
        }

        // Replaces the hand-built XR Origin in all 3 scenes with the official Starter Assets XR Origin (XR Rig) prefab,
        // which has controllers + XRI Default Input Actions pre-wired to OpenXR Touch.
        [MenuItem("Tigerverse/XR -> Swap to XR Origin (XR Rig) prefab in ALL scenes")]
        public static void SwapToRigPrefab()
        {
            // Locate the prefab.
            string[] candidates = AssetDatabase.FindAssets("XR Origin (XR Rig) t:Prefab");
            string prefabPath = null;
            foreach (var g in candidates)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("Starter Assets")) { prefabPath = p; break; }
            }
            if (prefabPath == null)
            {
                Debug.LogError("[Tigerverse] XR Origin (XR Rig).prefab not found. Run 'Import XRI Starter Assets Sample' first.");
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Debug.Log($"[Tigerverse] Found rig prefab: {prefabPath}");

            string[] scenes = {
                "Assets/_Project/Scenes/Title.unity",
                "Assets/_Project/Scenes/Lobby.unity",
                "Assets/_Project/Scenes/Battle.unity",
            };
            foreach (var scenePath in scenes)
            {
                if (!System.IO.File.Exists(scenePath)) continue;
                var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

                // Remove our hand-built XR Origin (and any leftover Main Camera at root).
                var existing = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (existing != null)
                {
                    Debug.Log($"[Tigerverse] Removing hand-built XR Origin from {s.name}.");
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }
                foreach (var cam in GameObject.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (cam.CompareTag("MainCamera"))
                    {
                        Debug.Log($"[Tigerverse] Removing leftover Main Camera from {s.name}.");
                        UnityEngine.Object.DestroyImmediate(cam.gameObject);
                    }
                }

                // Instantiate the real rig prefab as a scene instance.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "XR Origin (XR Rig)";
                instance.transform.position = Vector3.zero;

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(s);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s);
                Debug.Log($"[Tigerverse] Swapped {s.name} to use the rig prefab.");
            }
        }

        // Disables the duplicate runtime-loader feature on OpenXR (we keep MetaQuestFeature, drop OculusQuestFeature).
        [MenuItem("Tigerverse/XR -> Resolve OpenXR runtime-loader conflict")]
        public static void ResolveLoaderConflict()
        {
            const string path = "Assets/XR/Settings/OpenXR Package Settings.asset";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            int disabled = 0;
            foreach (var a in assets)
            {
                if (a == null || a.name == null) continue;
                if (!a.name.StartsWith("OculusQuestFeature")) continue;
                var so = new SerializedObject(a);
                var prop = so.FindProperty("m_enabled");
                if (prop != null && prop.boolValue)
                {
                    prop.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    disabled++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Tigerverse] Disabled {disabled} OculusQuestFeature instance(s). MetaQuestFeature remains active.");
        }

        // Re-bind TitleCanvas.worldCamera to the new rig's camera after the swap.
        [MenuItem("Tigerverse/XR -> Re-bind TitleCanvas to rig camera")]
        public static void RebindTitleCanvas()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/Title.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
            var origin = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var canvasGo = GameObject.Find("TitleCanvas");
            if (origin == null || canvasGo == null)
            {
                Debug.LogWarning("[Tigerverse] Missing XR Origin or TitleCanvas in Title scene.");
                return;
            }
            var canvas = canvasGo.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.worldCamera = origin.Camera;
                Debug.Log($"[Tigerverse] TitleCanvas.worldCamera -> {origin.Camera.name}");
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
    }
}
#endif
