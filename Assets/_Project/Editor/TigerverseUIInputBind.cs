#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Tigerverse.EditorTools
{
    public static class TigerverseUIInputBind
    {
        private const string ActionsAssetPath =
            "Assets/Samples/XR Interaction Toolkit/3.0.7/Starter Assets/XRI Default Input Actions.inputactions";

        [MenuItem("Tigerverse/UI -> Wire EventSystem Input Actions (mouse + XR)")]
        public static void Apply()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsAssetPath);
            if (actions == null)
            {
                Debug.LogError($"[Tigerverse] Couldn't load XRI Default Input Actions at {ActionsAssetPath}. Run XRI sample import first.");
                return;
            }

            // Find every action reference asset that points into this map and bucket by name.
            var allRefs = AssetDatabase.LoadAllAssetsAtPath(ActionsAssetPath)
                .OfType<InputActionReference>()
                .ToList();

            // Helper: find the InputActionReference whose underlying action.name matches.
            InputActionReference FindRef(string mapName, string actionName)
            {
                return allRefs.FirstOrDefault(r =>
                    r != null && r.action != null
                    && r.action.actionMap != null
                    && r.action.actionMap.name == mapName
                    && r.action.name == actionName);
            }

            // Apply to Title scene first; same EventSystem object should exist there.
            string[] scenes = {
                "Assets/_Project/Scenes/Title.unity",
                "Assets/_Project/Scenes/Lobby.unity",
                "Assets/_Project/Scenes/Battle.unity",
            };

            foreach (var scenePath in scenes)
            {
                if (!System.IO.File.Exists(scenePath)) continue;
                var s = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                var es = GameObject.FindFirstObjectByType<EventSystem>();
                if (es == null)
                {
                    Debug.LogWarning($"[Tigerverse] No EventSystem in {scenePath}");
                    continue;
                }

                // Strip XRUIInputModule and use plain InputSystemUIInputModule (mouse + keyboard reliably).
                // The Tracked-Device raycaster on the canvas still drives XR pointer input via this module.
                var xrModule = es.GetComponent<XRUIInputModule>();
                if (xrModule != null) Object.DestroyImmediate(xrModule);

                var stdModule = es.GetComponent<InputSystemUIInputModule>();
                if (stdModule == null) stdModule = es.gameObject.AddComponent<InputSystemUIInputModule>();

                // Wire action references.
                var so = new SerializedObject(stdModule);
                BindIfExists(so, "m_PointAction",            FindRef("XRI UI", "Point"));
                BindIfExists(so, "m_LeftClickAction",        FindRef("XRI UI", "Click"));
                BindIfExists(so, "m_MiddleClickAction",      FindRef("XRI UI", "Middle Click"));
                BindIfExists(so, "m_RightClickAction",       FindRef("XRI UI", "Right Click"));
                BindIfExists(so, "m_ScrollWheelAction",      FindRef("XRI UI", "Scroll Wheel"));
                BindIfExists(so, "m_MoveAction",             FindRef("XRI UI", "Navigate"));
                BindIfExists(so, "m_SubmitAction",           FindRef("XRI UI", "Submit"));
                BindIfExists(so, "m_CancelAction",           FindRef("XRI UI", "Cancel"));
                BindIfExists(so, "m_TrackedDevicePosition",  FindRef("XRI UI", "Tracked Device Position"));
                BindIfExists(so, "m_TrackedDeviceOrientation", FindRef("XRI UI", "Tracked Device Orientation"));
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(s);
                EditorSceneManager.SaveScene(s);
                Debug.Log($"[Tigerverse] {scenePath}: replaced XRUIInputModule with InputSystemUIInputModule and bound XRI UI action references.");
            }
        }

        private static void BindIfExists(SerializedObject so, string fieldName, InputActionReference value)
        {
            var p = so.FindProperty(fieldName);
            if (p == null)
            {
                Debug.LogWarning($"[Tigerverse]   field '{fieldName}' not found");
                return;
            }
            if (value == null)
            {
                Debug.LogWarning($"[Tigerverse]   no action reference for '{fieldName}' (left as previous)");
                return;
            }
            p.objectReferenceValue = value;
        }
    }
}
#endif
