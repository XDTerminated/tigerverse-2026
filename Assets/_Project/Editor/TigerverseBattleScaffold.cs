#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Tigerverse.Net;
using Tigerverse.Core;
using Tigerverse.MR;

namespace Tigerverse.EditorTools
{
    public static class TigerverseBattleScaffold
    {
        [MenuItem("Tigerverse/Battle -> Enable Mock + Wire Spawn Pivots")]
        public static void Apply()
        {
            // 1) Flip BackendConfig.useMock = true
            var cfg = AssetDatabase.LoadAssetAtPath<BackendConfig>("Assets/_Project/Resources/BackendConfig.asset");
            if (cfg == null)
            {
                Debug.LogError("[Tigerverse] BackendConfig.asset missing, run 'Setup → Generate Default Moves & Catalog' first.");
                return;
            }
            var cfgSo = new SerializedObject(cfg);
            cfgSo.FindProperty("useMock").boolValue = true;
            cfgSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cfg);
            Debug.Log("[Tigerverse] BackendConfig.useMock = true");

            // 2) Open Title scene, add monster spawn pivots at the spawn markers, wire to GSM.tabletAnchors.
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);

            var gsm = GameObject.FindFirstObjectByType<GameStateManager>();
            if (gsm == null) { Debug.LogError("[Tigerverse] GameStateManager not in scene."); return; }

            // Pivots = empty GO transforms where monsters spawn. Use the existing SpawnP0/SpawnP1 markers as world reference.
            Transform pivotA = EnsurePivot("MonsterSpawnPivotA", new Vector3(-1.2f, 0.5f, 0));
            Transform pivotB = EnsurePivot("MonsterSpawnPivotB", new Vector3( 1.2f, 0.5f, 0));

            // Wrap them in TabletAnchor so GameStateManager's tabletAnchors[i].anchorTransform works.
            var anchorA = EnsureTabletAnchor(pivotA);
            var anchorB = EnsureTabletAnchor(pivotB);

            var so = new SerializedObject(gsm);
            var arr = so.FindProperty("tabletAnchors");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = anchorA;
            arr.GetArrayElementAtIndex(1).objectReferenceValue = anchorB;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Battle scaffold complete: mock mode + 2 monster spawn pivots wired to GameStateManager.");
        }

        private static Transform EnsurePivot(string name, Vector3 worldPos)
        {
            var existing = GameObject.Find(name);
            if (existing != null) { existing.transform.position = worldPos; return existing.transform; }
            var go = new GameObject(name);
            go.transform.position = worldPos;
            return go.transform;
        }

        private static TabletAnchor EnsureTabletAnchor(Transform pivot)
        {
            var ta = pivot.GetComponent<TabletAnchor>();
            if (ta == null) ta = pivot.gameObject.AddComponent<TabletAnchor>();
            // Use reflection to set the private anchorTransform if needed (most simple: ensure the TabletAnchor
            // exposes an Awake-created child transform; we reuse the GO itself as the anchor).
            return ta;
        }
    }
}
#endif
