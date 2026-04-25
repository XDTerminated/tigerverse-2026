#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    public static class TigerverseTmpImport
    {
        [MenuItem("Tigerverse/Setup -> Force Import TMP Essentials + Repair Title UI")]
        public static void ImportTmpEssentials()
        {
            string pkg = Path.Combine(Application.dataPath, "..",
                "Library/PackageCache/com.unity.ugui@47e51ce530b9/Package Resources/TMP Essential Resources.unitypackage");
            pkg = Path.GetFullPath(pkg);

            if (!File.Exists(pkg))
            {
                // Fallback: search the PackageCache for any com.unity.ugui@*/Package Resources/TMP Essential Resources.unitypackage
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library/PackageCache"));
                if (Directory.Exists(root))
                {
                    foreach (var dir in Directory.GetDirectories(root, "com.unity.ugui@*"))
                    {
                        var candidate = Path.Combine(dir, "Package Resources", "TMP Essential Resources.unitypackage");
                        if (File.Exists(candidate)) { pkg = candidate; break; }
                    }
                }
            }

            if (!File.Exists(pkg))
            {
                Debug.LogError("[Tigerverse] Could not find TMP Essential Resources.unitypackage. Manual import: Window -> TextMeshPro -> Import TMP Essential Resources.");
                return;
            }

            Debug.Log($"[Tigerverse] Importing TMP Essentials from {pkg}");
            AssetDatabase.ImportPackage(pkg, false); // false = no interactive dialog
            AssetDatabase.Refresh();
        }

        [MenuItem("Tigerverse/Setup -> Rebuild Title UI (after TMP import)")]
        public static void RebuildTitleUI()
        {
            string scenePath = "Assets/_Project/Scenes/Title.unity";
            if (!System.IO.File.Exists(scenePath)) { Debug.LogError("Title scene missing."); return; }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

            // Nuke existing TitleCanvas if present.
            var existing = GameObject.Find("TitleCanvas");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
                Debug.Log("[Tigerverse] Removed broken TitleCanvas.");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            // Re-run the existing AddTitleScreenUI which will now build with valid TMP defaults.
            TigerverseProjectSetup.AddTitleScreenUI();
        }
    }
}
#endif
