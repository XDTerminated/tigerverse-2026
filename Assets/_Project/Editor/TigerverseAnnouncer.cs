#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Tigerverse.Voice;

namespace Tigerverse.EditorTools
{
    public static class TigerverseAnnouncer
    {
        [MenuItem("Tigerverse/Voice -> Add Announcer to Bootstrap")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);
            var bootstrap = GameObject.Find("Bootstrap");
            if (bootstrap == null) { Debug.LogError("[Tigerverse] Bootstrap missing."); return; }

            var ann = bootstrap.GetComponent<Announcer>();
            if (ann == null) ann = bootstrap.AddComponent<Announcer>();
            // RequireComponent(AudioSource) on Announcer ensures one is added.

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Announcer added to Bootstrap.");
        }
    }
}
#endif
