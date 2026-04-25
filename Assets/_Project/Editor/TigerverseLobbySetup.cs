#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Tigerverse.Net;
using Tigerverse.Core;

namespace Tigerverse.EditorTools
{
    public static class TigerverseLobbySetup
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Players/PlayerAvatar.prefab";

        [MenuItem("Tigerverse/Lobby -> Build Avatar Prefab + Wire Spawner + WASD + Joystick")]
        public static void Apply()
        {
            CreateAvatarPrefab();
            WireSpawnerAndStatus();
            EnableLocomotionAndAddWasd();
            Debug.Log("[Tigerverse] Lobby setup complete: avatar prefab, spawner, status label, joystick locomotion, WASD.");
        }

        // 1) Create PlayerAvatar prefab: head cube + 2 hand cubes + NetworkObject + PlayerAvatar.
        private static void CreateAvatarPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) AssetDatabase.DeleteAsset(PrefabPath);

            var root = new GameObject("PlayerAvatar");

#if FUSION2
            root.AddComponent<Fusion.NetworkObject>();
#endif
            var avatar = root.AddComponent<PlayerAvatar>();

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localScale = new Vector3(0.18f, 0.22f, 0.22f);
            Object.DestroyImmediate(head.GetComponent<Collider>());

            var leftHand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftHand.name = "LeftHand";
            leftHand.transform.SetParent(root.transform, false);
            leftHand.transform.localScale = new Vector3(0.08f, 0.04f, 0.12f);
            Object.DestroyImmediate(leftHand.GetComponent<Collider>());

            var rightHand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightHand.name = "RightHand";
            rightHand.transform.SetParent(root.transform, false);
            rightHand.transform.localScale = new Vector3(0.08f, 0.04f, 0.12f);
            Object.DestroyImmediate(rightHand.GetComponent<Collider>());

            // Wire serialised fields.
            var so = new SerializedObject(avatar);
            so.FindProperty("headVisual").objectReferenceValue = head.transform;
            so.FindProperty("leftHandVisual").objectReferenceValue = leftHand.transform;
            so.FindProperty("rightHandVisual").objectReferenceValue = rightHand.transform;
            var arr = so.FindProperty("colorTargets");
            arr.arraySize = 3;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = head.GetComponent<Renderer>();
            arr.GetArrayElementAtIndex(1).objectReferenceValue = leftHand.GetComponent<Renderer>();
            arr.GetArrayElementAtIndex(2).objectReferenceValue = rightHand.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            // Ensure folder exists.
            string folder = "Assets/_Project/Prefabs/Players";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
                    AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
                AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Players");
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Tigerverse] PlayerAvatar prefab saved to {PrefabPath}");
        }

        // 2) Add LobbyAvatarSpawner + LobbyStatus to Bootstrap and TitleCanvas.
        private static void WireSpawnerAndStatus()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);

            var bootstrap = GameObject.Find("Bootstrap");
            if (bootstrap == null) { Debug.LogError("[Tigerverse] Bootstrap missing."); return; }

            // LobbyAvatarSpawner on Bootstrap.
            var spawner = bootstrap.GetComponent<LobbyAvatarSpawner>();
            if (spawner == null) spawner = bootstrap.AddComponent<LobbyAvatarSpawner>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var spso = new SerializedObject(spawner);
            spso.FindProperty("playerAvatarPrefab").objectReferenceValue = prefab;
            spso.ApplyModifiedPropertiesWithoutUndo();

            // LobbyStatus on TitleCanvas, label = StatusLabel; hide HOST/JOIN/CodeInput/SoftKeyboard once both joined.
            var canvas = GameObject.Find("TitleCanvas");
            var status = GameObject.Find("StatusLabel");
            if (canvas != null && status != null)
            {
                var ls = canvas.GetComponent<LobbyStatus>();
                if (ls == null) ls = canvas.AddComponent<LobbyStatus>();
                var lso = new SerializedObject(ls);
                lso.FindProperty("label").objectReferenceValue = status.GetComponent<TMP_Text>();

                // Hide-on-ready set
                var hostBtn2 = GameObject.Find("HostButton");
                var joinBtn2 = GameObject.Find("JoinButton");
                var input2   = GameObject.Find("CodeInput");
                var keyb     = GameObject.Find("SoftKeyboard");
                var hide = lso.FindProperty("hideOnReady");
                int n = 0;
                if (hostBtn2 != null) n++;
                if (joinBtn2 != null) n++;
                if (input2 != null) n++;
                if (keyb != null) n++;
                hide.arraySize = n;
                int idx = 0;
                if (hostBtn2 != null) hide.GetArrayElementAtIndex(idx++).objectReferenceValue = hostBtn2;
                if (joinBtn2 != null) hide.GetArrayElementAtIndex(idx++).objectReferenceValue = joinBtn2;
                if (input2 != null)   hide.GetArrayElementAtIndex(idx++).objectReferenceValue = input2;
                if (keyb != null)     hide.GetArrayElementAtIndex(idx++).objectReferenceValue = keyb;

                // Shrink the status label up to the top once ready
                lso.FindProperty("shrinkTarget").objectReferenceValue = status.transform;
                lso.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // 3) Re-enable joystick locomotion in all scenes + add WASD FlatMoveController to XR Origin.
        private static void EnableLocomotionAndAddWasd()
        {
            string[] scenes = {
                "Assets/_Project/Scenes/Title.unity",
                "Assets/_Project/Scenes/Lobby.unity",
                "Assets/_Project/Scenes/Battle.unity",
            };
            foreach (var path in scenes)
            {
                if (!System.IO.File.Exists(path)) continue;
                var s = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var origin = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (origin == null) { Debug.LogWarning($"[Tigerverse] No XR Origin in {path}"); continue; }

                int re = 0;
                foreach (var c in origin.gameObject.GetComponentsInChildren<ContinuousMoveProvider>(true)) { c.enabled = true; re++; }
                foreach (var c in origin.gameObject.GetComponentsInChildren<SnapTurnProvider>(true)) { c.enabled = true; re++; }
                foreach (var c in origin.gameObject.GetComponentsInChildren<ContinuousTurnProvider>(true)) { c.enabled = true; re++; }

                var fmc = origin.GetComponent<FlatMoveController>();
                if (fmc == null) origin.gameObject.AddComponent<FlatMoveController>();

                EditorSceneManager.MarkSceneDirty(s);
                EditorSceneManager.SaveScene(s);
                Debug.Log($"[Tigerverse] {path}: re-enabled {re} locomotion provider(s) + added FlatMoveController.");
            }
        }
    }
}
#endif
