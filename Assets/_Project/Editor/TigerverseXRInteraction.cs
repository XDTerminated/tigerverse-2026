#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;

using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Tigerverse.EditorTools
{
    public static class TigerverseXRInteraction
    {
        [MenuItem("Tigerverse/XR -> Add Controllers + UI Raycasters (active scene)")]
        public static void Apply()
        {
            // 1. Add LeftHand + RightHand controllers under XR Origin (with ray interactor + line renderer).
            var origin = GameObject.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null)
            {
                Debug.LogError("[Tigerverse] No XR Origin in active scene. Run 'Replace Main Camera with XR Origin' first.");
                return;
            }

            var offset = origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : origin.transform;

            EnsureController(offset, "LeftHand Controller", isLeft: true);
            EnsureController(offset, "RightHand Controller", isLeft: false);

            // 2. EventSystem -> swap InputSystemUIInputModule for XRUIInputModule.
            var eventSystem = GameObject.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null)
            {
                var oldModule = eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                if (oldModule != null && eventSystem.GetComponent<XRUIInputModule>() == null)
                {
                    UnityEngine.Object.DestroyImmediate(oldModule);
                    eventSystem.gameObject.AddComponent<XRUIInputModule>();
                    Debug.Log("[Tigerverse] EventSystem -> XR UI Input Module.");
                }
                else if (eventSystem.GetComponent<XRUIInputModule>() == null)
                {
                    eventSystem.gameObject.AddComponent<XRUIInputModule>();
                }
            }

            // 3. TitleCanvas: shrink, set worldCamera, swap GraphicRaycaster for TrackedDeviceGraphicRaycaster.
            var canvasGo = GameObject.Find("TitleCanvas");
            if (canvasGo != null)
            {
                var rt = canvasGo.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(640, 480);
                canvasGo.transform.position = new Vector3(0, 1.5f, 1.0f); // 1m forward, eye height
                canvasGo.transform.localScale = Vector3.one * 0.0015f;     // ~1m wide instead of 4m

                var canvas = canvasGo.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.worldCamera = origin.Camera;
                }

                var oldRaycaster = canvasGo.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (oldRaycaster != null && !(oldRaycaster is TrackedDeviceGraphicRaycaster))
                {
                    UnityEngine.Object.DestroyImmediate(oldRaycaster);
                }
                if (canvasGo.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                {
                    canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
                }
                Debug.Log("[Tigerverse] TitleCanvas resized + Tracked-Device raycaster wired.");
            }

            EditorSceneManager.MarkSceneDirty(origin.gameObject.scene);
            EditorSceneManager.SaveScene(origin.gameObject.scene);
            Debug.Log("[Tigerverse] XR controllers + UI raycasting setup complete.");
        }

        private static void EnsureController(Transform parent, string name, bool isLeft)
        {
            var existing = parent.Find(name);
            if (existing != null) { Debug.Log($"[Tigerverse] '{name}' already present."); return; }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var driver = go.AddComponent<TrackedPoseDriver>();
            // Configure the action references manually inside the inspector if you want — defaults work for OpenXR Touch.

            var ray = go.AddComponent<NearFarInteractor>();
            // NearFarInteractor is the XRI 3.x replacement for XRRayInteractor on hand controllers.

            var line = go.AddComponent<LineRenderer>();
            line.widthMultiplier = 0.005f;
            line.positionCount = 2;
            line.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            line.startColor = isLeft ? Color.cyan : Color.magenta;
            line.endColor = new Color(1, 1, 1, 0.2f);

            var visual = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            visual.lineLength = 5f;
            visual.invalidColorGradient = ColorGradient(Color.gray);
            visual.validColorGradient = ColorGradient(line.startColor);

            Debug.Log($"[Tigerverse] Built '{name}' with NearFarInteractor + line visual.");
        }

        private static Gradient ColorGradient(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.2f, 1f) });
            return g;
        }
    }
}
#endif
