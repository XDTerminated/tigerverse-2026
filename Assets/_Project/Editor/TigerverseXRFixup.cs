#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace Tigerverse.EditorTools
{
    public static class TigerverseXRFixup
    {
        // Parents the TitleCanvas under the rig camera so locomotion can't lose it,
        // enables UI interaction on every interactor in the rig, and disables joystick locomotion.
        [MenuItem("Tigerverse/XR -> Fix Title Buttons + Pin UI to Camera")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);

            var origin = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null)
            {
                Debug.LogError("[Tigerverse] No XR Origin in Title scene.");
                return;
            }

            // Pin canvas to camera (so it follows the headset).
            var canvasGo = GameObject.Find("TitleCanvas");
            if (canvasGo != null)
            {
                canvasGo.transform.SetParent(origin.Camera.transform, worldPositionStays: false);
                canvasGo.transform.localPosition = new Vector3(0, 0, 1.0f); // 1m forward of camera
                canvasGo.transform.localRotation = Quaternion.identity;
                canvasGo.transform.localScale = Vector3.one * 0.0015f;

                var canvas = canvasGo.GetComponent<Canvas>();
                if (canvas != null) canvas.worldCamera = origin.Camera;
                Debug.Log("[Tigerverse] TitleCanvas pinned to camera (1m forward).");
            }

            // Enable UI interaction on every NearFarInteractor / XRRayInteractor in the rig.
            int enabledCount = 0;
            foreach (var nf in origin.gameObject.GetComponentsInChildren<NearFarInteractor>(true))
            {
                if (TrySetUIInteraction(nf)) enabledCount++;
            }
            foreach (var ray in origin.gameObject.GetComponentsInChildren<XRRayInteractor>(true))
            {
                if (TrySetUIInteraction(ray)) enabledCount++;
            }
            Debug.Log($"[Tigerverse] Enabled UI interaction on {enabledCount} interactor(s).");

            // Disable joystick locomotion (continuous move + snap turn) so sticks don't move you off the canvas.
            int disabled = 0;
            foreach (var c in origin.gameObject.GetComponentsInChildren<ContinuousMoveProvider>(true))
            {
                c.enabled = false; disabled++;
            }
            foreach (var c in origin.gameObject.GetComponentsInChildren<SnapTurnProvider>(true))
            {
                c.enabled = false; disabled++;
            }
            foreach (var c in origin.gameObject.GetComponentsInChildren<ContinuousTurnProvider>(true))
            {
                c.enabled = false; disabled++;
            }
            Debug.Log($"[Tigerverse] Disabled {disabled} locomotion provider(s).");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Title scene fixup complete. Press Play and pull either trigger while pointing at the buttons.");
        }

        // Sets enableUIInteraction = true via reflection (handles different XRI versions).
        private static bool TrySetUIInteraction(Component interactor)
        {
            var t = interactor.GetType();

            // XRI 3.x: BaseInteractor.uiInteractionEnabled or NearFarInteractor.uiInteractionEnabled
            var prop = t.GetProperty("uiInteractionEnabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
            {
                prop.SetValue(interactor, true);
                EditorUtility.SetDirty(interactor);
                return true;
            }

            // Legacy XRRayInteractor.enableUIInteraction
            var legacy = t.GetProperty("enableUIInteraction", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (legacy != null && legacy.PropertyType == typeof(bool) && legacy.CanWrite)
            {
                legacy.SetValue(interactor, true);
                EditorUtility.SetDirty(interactor);
                return true;
            }

            // Field fallback
            var field = t.GetField("m_EnableUIInteraction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(interactor, true);
                EditorUtility.SetDirty(interactor);
                return true;
            }

            return false;
        }
    }
}
#endif
