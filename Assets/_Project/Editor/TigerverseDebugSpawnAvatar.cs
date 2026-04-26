#if UNITY_EDITOR
using Tigerverse.Net;
using UnityEditor;
using UnityEngine;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Editor shortcut for solo testing: spawns a stand-in PaperHumanoid in
    /// front of the local camera so you can see the avatar's head sphere,
    /// doodle face, and floating "Player N" name tag without needing a
    /// second client to connect over Photon.
    /// </summary>
    public static class TigerverseDebugSpawnAvatar
    {
        [MenuItem("Tigerverse/Debug -> Spawn Test Avatar (Play mode)")]
        public static void SpawnNow()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Enter Play mode first",
                    "This shortcut wires runtime components and only works in Play mode.", "OK");
                return;
            }

            var existing = GameObject.Find("DEBUG_TestAvatar");
            if (existing != null) Object.DestroyImmediate(existing);

            // Place 1.5m in front of the main camera so it's immediately visible.
            var cam = Camera.main;
            Vector3 spawnPos = Vector3.zero;
            if (cam != null) spawnPos = cam.transform.position + cam.transform.forward * 1.5f;
            spawnPos.y = 0f;

            var root = new GameObject("DEBUG_TestAvatar");
            root.transform.position = spawnPos;

            // Synced-transform stand-ins: head + two hands. PaperHumanoid
            // builds the body around these, so they need to exist before
            // EnsureHead runs in LateUpdate.
            var head = new GameObject("FakeHead").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);
            head.localRotation = Quaternion.identity;

            var leftHand = new GameObject("FakeLeftHand").transform;
            leftHand.SetParent(root.transform, false);
            leftHand.localPosition = new Vector3(-0.3f, 1.0f, 0.2f);

            var rightHand = new GameObject("FakeRightHand").transform;
            rightHand.SetParent(root.transform, false);
            rightHand.localPosition = new Vector3(0.3f, 1.0f, 0.2f);

            var humanoid = root.AddComponent<PaperHumanoid>();
            humanoid.headSrc = head;
            humanoid.leftHandSrc = leftHand;
            humanoid.rightHandSrc = rightHand;
            humanoid.SetBodyColor(new Color(0.30f, 0.55f, 1.00f), new Color(0.10f, 0.20f, 0.55f));
            humanoid.SetDisplayName("Player 1");

            Debug.Log("[Debug] Spawned DEBUG_TestAvatar 1.5m in front of camera.");
        }

        [MenuItem("Tigerverse/Debug -> Despawn Test Avatar (Play mode)")]
        public static void DespawnNow()
        {
            if (!EditorApplication.isPlaying) return;
            var go = GameObject.Find("DEBUG_TestAvatar");
            if (go != null) Object.Destroy(go);
        }
    }
}
#endif
